using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Scheduling;
using Miqat.Application.Specifications.Events;
using Miqat.Domain.Entities;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class CalendarFileService : ICalendarFileService
    {
        /// <summary>The largest file an import accepts.</summary>
        public const int MaxFileBytes = 2 * 1024 * 1024;

        private const int MaxEventsPerImport = 2000;

        /// <summary>Marks the events this server wrote, so its own file can be read back without making copies.</summary>
        private const string OwnUidSuffix = "@miqat";

        // Column sizes of the Events table: what a file brings is cut to fit.
        private const int MaxTitle = 200, MaxDescription = 2000, MaxLocation = 300, MaxUid = 300;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly ICalendarService _calendars;

        public CalendarFileService(IUnitOfWork unitOfWork, ICurrentUserService currentUser, ICalendarService calendars)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _calendars = calendars;
        }

        private IGenericRepository<CalendarEvent> Events => _unitOfWork.Repository<CalendarEvent>();

        public async Task<string> ExportAsync(Guid? calendarId)
        {
            var owner = await RequireCallerAsync();

            // Checked even when nothing is filtered: an id that is not a
            // calendar of the caller is a mistake worth reporting.
            if (calendarId.HasValue) await _calendars.ResolveAsync(owner.Id, calendarId);

            var events = await Events.ListAsync(new OwnEventsSpec(owner.Id, calendarId));

            return IcsFormat.Write(
                "Miqat", events.SelectMany(ToFileEvents), TimeZones.Resolve(owner.TimeZone), DateTime.UtcNow);
        }

        public async Task<ImportResultDto> ImportAsync(Stream file, Guid? calendarId)
        {
            var owner = await RequireCallerAsync();
            var calendar = await _calendars.ResolveAsync(owner.Id, calendarId);

            using var reader = new StreamReader(file);
            var text = await reader.ReadToEndAsync();

            if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
                throw new ApiException("That is not a calendar (.ics) file.", 400);

            var parsed = IcsFormat.Read(text, TimeZones.Resolve(owner.TimeZone));
            if (parsed.Events.Count > MaxEventsPerImport)
                throw new ApiException($"A file can hold up to {MaxEventsPerImport} events.", 400);

            var mine = (await Events.FindAsync(e => e.OwnerId == owner.Id && !e.IsDeleted)).ToList();
            var byUid = mine.Where(e => e.ExternalUid != null)
                .GroupBy(e => e.ExternalUid!).ToDictionary(g => g.Key, g => g.First());
            var byId = mine.ToDictionary(e => e.Id);

            // A file this server wrote lists each moved date as an entry of its
            // own. Those dates are already here; reading them back would copy them.
            var ownMovedDates = (await _unitOfWork.Repository<EventException>()
                    .SelectAsync(x => x.Event.OwnerId == owner.Id, x => x.Id))
                .Select(id => id + OwnUidSuffix)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = new ImportResultDto { CalendarId = calendar.Id, Skipped = parsed.Skipped };

            foreach (var item in parsed.Events)
            {
                var uid = Cut(item.Uid, MaxUid);
                if (uid != null && ownMovedDates.Contains(uid))
                {
                    result.Skipped++;
                    continue;
                }

                var existing = FindExisting(uid, byUid, byId);

                if (existing == null)
                {
                    existing = new CalendarEvent(
                        Cut(item.Title, MaxTitle)!, item.Start, item.End, owner, calendar.Id, item.IsAllDay)
                    {
                        ExternalUid = uid
                    };
                    await Events.AddAsync(existing);
                    if (uid != null) byUid[uid] = existing;
                    result.Imported++;
                }
                else
                {
                    existing.Title = Cut(item.Title, MaxTitle)!;
                    existing.Reschedule(item.Start, item.End, item.IsAllDay);
                    existing.SetUpdated(owner.Id.ToString());
                    result.Updated++;
                }

                existing.Description = Cut(item.Description, MaxDescription);
                existing.Location = Cut(item.Location, MaxLocation);
                existing.Repeat(item.Rule.Pattern, item.Rule.Interval, item.Rule.Until);

                await SkipDatesAsync(existing, item.SkippedStarts);
            }

            await _unitOfWork.CompleteAsync();
            return result;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private async Task<User> RequireCallerAsync() =>
            await _unitOfWork.Repository<User>().GetByIdAsync(_currentUser.RequireUserId())
            ?? throw new ApiException("Not signed in.", 401);

        /// <summary>
        /// The event this entry was imported into before - or, for a file this
        /// server wrote itself, the very event it was exported from.
        /// </summary>
        private static CalendarEvent? FindExisting(
            string? uid, Dictionary<string, CalendarEvent> byUid, Dictionary<Guid, CalendarEvent> byId)
        {
            if (uid == null) return null;
            if (byUid.TryGetValue(uid, out var imported)) return imported;

            return uid.EndsWith(OwnUidSuffix, StringComparison.OrdinalIgnoreCase) &&
                   Guid.TryParse(uid[..^OwnUidSuffix.Length], out var id)
                ? byId.GetValueOrDefault(id)
                : null;
        }

        /// <summary>Dates the file says do not happen (EXDATE) become skipped dates of the series.</summary>
        private async Task SkipDatesAsync(CalendarEvent calendarEvent, IReadOnlyList<DateTime>? skippedStarts)
        {
            if (!calendarEvent.IsRecurring || skippedStarts is not { Count: > 0 }) return;

            // An event that is updated was not loaded with its exceptions.
            var known = (await _unitOfWork.Repository<EventException>()
                    .SelectAsync(x => x.EventId == calendarEvent.Id, x => x.OccurrenceStart))
                .ToHashSet();

            foreach (var start in skippedStarts.Distinct())
            {
                if (!known.Add(start) || !EventDates.IsSeriesDate(calendarEvent, start)) continue;

                var exception = new EventException(calendarEvent.Id, start);
                exception.Cancel();
                await _unitOfWork.Repository<EventException>().AddAsync(exception);
            }
        }

        /// <summary>
        /// One stored event as file entries: the event itself (skipped dates
        /// listed on it) plus one stand-alone entry for each date that was
        /// moved, so other calendars show it where it really is.
        /// </summary>
        private static IEnumerable<IcsEvent> ToFileEvents(CalendarEvent calendarEvent)
        {
            yield return new IcsEvent(
                calendarEvent.ExternalUid ?? calendarEvent.Id + OwnUidSuffix,
                calendarEvent.Title, calendarEvent.Description, calendarEvent.Location,
                calendarEvent.StartsAt, calendarEvent.EndsAt, calendarEvent.IsAllDay,
                RecurrenceRule.Of(calendarEvent),
                calendarEvent.Exceptions.Select(x => x.OccurrenceStart).ToList());

            foreach (var moved in calendarEvent.Exceptions.Where(x => !x.IsCancelled && x.StartsAt.HasValue))
                yield return new IcsEvent(
                    moved.Id + OwnUidSuffix,
                    moved.Title ?? calendarEvent.Title, calendarEvent.Description, moved.Location ?? calendarEvent.Location,
                    moved.StartsAt!.Value, moved.EndsAt!.Value, calendarEvent.IsAllDay, default);
        }

        private static string? Cut(string? value, int max) =>
            value == null || value.Length <= max ? value : value[..max];
    }
}
