using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Scheduling;
using Miqat.Application.Specifications.Events;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class ScheduleService : IScheduleService
    {
        /// <summary>The longest stretch that can be looked at in one request.</summary>
        public const int MaxRangeDays = 31;

        private const int MaxPeople = 25;
        private const int MaxSuggestions = 20;
        private const int SuggestionsPerDay = 3;
        private static readonly TimeSpan DefaultSearchWindow = TimeSpan.FromDays(14);

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly PeopleDirectory _people;

        public ScheduleService(IUnitOfWork unitOfWork, ICurrentUserService currentUser, PeopleDirectory people)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _people = people;
        }

        public async Task<List<UserAvailabilityDto>> GetAvailabilityAsync(IReadOnlyCollection<Guid> userIds, DateRange range)
        {
            var me = _currentUser.RequireUserId();
            var people = await LoadPeopleAsync(me, userIds.Count == 0 ? new[] { me } : userIds, range);

            return people.Select(p => new UserAvailabilityDto
            {
                UserId = p.Id,
                FullName = p.FullName,
                TimeZone = p.Zone.Id,
                Busy = p.Busy.Select(ToSlot).ToList()
            }).ToList();
        }

        public async Task<List<TimeSlotDto>> SuggestAsync(SuggestTimesDto request)
        {
            var me = _currentUser.RequireUserId();
            var now = DateTime.UtcNow;

            // Never suggest a time that has already passed.
            var from = request.From is { } asked && asked > now ? asked : now;
            var range = DateRange.Require(from, request.To ?? from + DefaultSearchWindow, MaxRangeDays);

            var people = await LoadPeopleAsync(me, request.AttendeeIds.Append(me).ToList(), range);
            var personIds = people.Select(p => p.Id).ToList();
            var settings = (await _unitOfWork.Repository<CalendarSettings>()
                    .FindAsync(s => personIds.Contains(s.UserId)))
                .ToDictionary(s => s.UserId);

            var participants = people
                .Select(p =>
                {
                    // Someone who never opened their calendar settings works the default hours.
                    var preferences = settings.GetValueOrDefault(p.Id) ?? new CalendarSettings(p.Id);
                    return new Participant(
                        p.Zone, preferences.WorkDayStartMinutes, preferences.WorkDayEndMinutes,
                        preferences.IsWorkDay, p.Busy);
                })
                .ToList();

            var slots = SlotFinder.Find(
                participants,
                TimeSpan.FromMinutes(request.DurationMinutes),
                range,
                Math.Clamp(request.MaxResults, 1, MaxSuggestions),
                SuggestionsPerDay,
                request.WorkingHoursOnly,
                dayZone: people.First(p => p.Id == me).Zone);

            return slots.Select(ToSlot).ToList();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// The people asked about, each with the times they are busy in the
        /// range. Refuses (403) anyone the caller is not connected to: free
        /// time is only shared between friends and teammates.
        /// </summary>
        private async Task<List<Person>> LoadPeopleAsync(Guid me, IReadOnlyCollection<Guid> requestedIds, DateRange range)
        {
            var ids = requestedIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count > MaxPeople)
                throw new ApiException($"Up to {MaxPeople} people can be looked at together.", 400);

            if (ids.Any(id => id != me))
            {
                var contacts = await _people.GetContactIdsAsync(me);
                if (ids.Any(id => id != me && !contacts.Contains(id)))
                    throw new ApiException(
                        "You can only see the free time of your friends and the people on your projects.", 403);
            }

            var users = await _unitOfWork.Repository<User>().SelectAsync(
                u => ids.Contains(u.Id) && !u.IsDeleted,
                u => new { u.Id, u.FullName, u.TimeZone });

            var busy = await GetBusyTimesAsync(ids, range);

            return users
                .OrderBy(u => u.Id == me ? 0 : 1).ThenBy(u => u.FullName)
                .Select(u => new Person(
                    u.Id, u.FullName, TimeZones.Resolve(u.TimeZone),
                    SlotFinder.Merge(busy.GetValueOrDefault(u.Id) ?? new List<Occurrence>())))
                .ToList();
        }

        /// <summary>
        /// One query for everybody. A person is busy during the events they own
        /// and the ones they said yes or maybe to. All-day events do not block
        /// time: they are birthdays and holidays more often than meetings.
        /// </summary>
        private async Task<Dictionary<Guid, List<Occurrence>>> GetBusyTimesAsync(List<Guid> userIds, DateRange range)
        {
            var events = await _unitOfWork.Repository<CalendarEvent>()
                .ListAsync(new EventsInRangeSpec(userIds, Array.Empty<Guid>(), range));

            var busy = new Dictionary<Guid, List<Occurrence>>();

            foreach (var calendarEvent in events.Where(e => !e.IsAllDay))
            {
                var taking = calendarEvent.Attendees
                    .Where(a => a.Status is AttendeeStatus.Accepted or AttendeeStatus.Tentative)
                    .Select(a => a.UserId)
                    .Append(calendarEvent.OwnerId)
                    .Where(userIds.Contains)
                    .Distinct()
                    .ToList();

                foreach (var date in EventDates.In(calendarEvent, range))
                foreach (var userId in taking)
                {
                    if (!busy.TryGetValue(userId, out var blocks)) busy[userId] = blocks = new List<Occurrence>();
                    blocks.Add(new Occurrence(date.Start, date.End));
                }
            }

            return busy;
        }

        private static TimeSlotDto ToSlot(Occurrence occurrence) =>
            new() { Start = occurrence.Start, End = occurrence.End };

        private sealed record Person(Guid Id, string FullName, TimeZoneInfo Zone, List<Occurrence> Busy);
    }
}
