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
    public class EventService : IEventService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IAccessPolicy _access;
        private readonly ICalendarService _calendars;
        private readonly IReminderService _reminders;
        private readonly IEventAttendeeService _attendees;
        private readonly PeopleDirectory _people;
        private readonly EventAnnouncer _announcer;

        public EventService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IAccessPolicy access,
            ICalendarService calendars,
            IReminderService reminders,
            IEventAttendeeService attendees,
            PeopleDirectory people,
            EventAnnouncer announcer)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _access = access;
            _calendars = calendars;
            _reminders = reminders;
            _attendees = attendees;
            _people = people;
            _announcer = announcer;
        }

        private IGenericRepository<CalendarEvent> Events => _unitOfWork.Repository<CalendarEvent>();

        // ── Reading ───────────────────────────────────────────────────────────

        public async Task<List<EventDto>> GetInRangeAsync(DateRange range, Guid? calendarId)
        {
            var userId = _currentUser.RequireUserId();
            var projectIds = await _people.GetProjectIdsAsync(userId);

            var events = await Events.ListAsync(
                new EventsInRangeSpec(new[] { userId }, projectIds, range, withAttendeeNames: true));

            // A repeating event is found by its series; it only belongs in the
            // answer when one of its dates really falls inside the range.
            return events
                .Where(e => calendarId == null || e.CalendarId == calendarId)
                .Where(e => EventDates.In(e, range).Any())
                .Select(e => EventMapper.ToDto(e, userId))
                .ToList();
        }

        public async Task<EventDto?> GetByIdAsync(Guid id)
        {
            var calendarEvent = (await Events.ListAsync(new EventsWithDetailsSpec(id))).FirstOrDefault();
            if (calendarEvent == null) return null;

            await _access.RequireAsync(
                _access.CanViewEventAsync(id), "You do not have access to this event.");

            return EventMapper.ToDto(calendarEvent, _currentUser.RequireUserId());
        }

        // ── Writing ───────────────────────────────────────────────────────────

        public async Task<EventDto> CreateAsync(SaveEventDto dto)
        {
            // The owner comes from the token, never from the payload.
            var userId = _currentUser.RequireUserId();
            var owner = await _unitOfWork.Repository<User>().GetByIdAsync(userId)
                ?? throw new ApiException("Not signed in.", 401);

            var calendar = await _calendars.ResolveAsync(userId, dto.CalendarId);
            await RequireProjectAsync(dto.GroupId);

            var calendarEvent = new CalendarEvent(
                dto.Title, dto.StartsAt, dto.EndsAt, owner, calendar.Id, dto.IsAllDay)
            {
                Description = dto.Description,
                Location = dto.Location,
                GroupId = dto.GroupId
            };
            calendarEvent.Repeat(dto.Recurrence, dto.RecurrenceInterval, dto.RecurrenceEndDate);

            await Events.AddAsync(calendarEvent);

            // Stored in the same save as the event: an event is never left
            // behind without the invitations and reminders it was created with.
            var invited = await _attendees.StageInvitationsAsync(calendarEvent, dto.AttendeeIds ?? new List<Guid>());
            await _reminders.StageForEventAsync(calendarEvent, userId, dto.ReminderMinutes);
            await _unitOfWork.CompleteAsync();

            await _attendees.NotifyInvitedAsync(calendarEvent, invited);
            await _announcer.ChangedAsync(calendarEvent, "created");

            return (await GetByIdAsync(calendarEvent.Id))!;
        }

        public async Task<EventDto?> UpdateAsync(Guid id, SaveEventDto dto)
        {
            // Loaded before the permission check so an id that does not exist
            // answers 404 rather than 403.
            var calendarEvent = await LoadForChangeAsync(id, "Only the owner of an event can change it.");
            if (calendarEvent == null) return null;

            var actorId = _currentUser.RequireUserId();

            // An admin editing someone's event still files it in the calendars of its owner.
            var calendar = await _calendars.ResolveAsync(calendarEvent.OwnerId, dto.CalendarId ?? calendarEvent.CalendarId);
            if (dto.GroupId != calendarEvent.GroupId) await RequireProjectAsync(dto.GroupId);

            var rescheduled = IsRescheduledBy(calendarEvent, dto);
            var worthTelling = rescheduled || !string.Equals(calendarEvent.Location, dto.Location);

            calendarEvent.Title = dto.Title.Trim();
            calendarEvent.Description = dto.Description;
            calendarEvent.Location = dto.Location;
            calendarEvent.CalendarId = calendar.Id;
            calendarEvent.GroupId = dto.GroupId;

            if (rescheduled)
            {
                calendarEvent.Reschedule(dto.StartsAt, dto.EndsAt, dto.IsAllDay);
                calendarEvent.Repeat(dto.Recurrence, dto.RecurrenceInterval, dto.RecurrenceEndDate);

                // Changes to single dates were made against the old series and
                // no longer line up with the new one.
                calendarEvent.Exceptions.Clear();

                await _reminders.RescheduleAsync(calendarEvent);
            }

            calendarEvent.SetUpdated(actorId.ToString());
            await _unitOfWork.CompleteAsync();

            if (worthTelling)
            {
                await _announcer.NotifyAttendeesAsync(
                    calendarEvent, actorId, NotificationType.EventChanged, "Event changed",
                    actor => $"{actor} changed \"{calendarEvent.Title}\".");
            }

            await _announcer.ChangedAsync(calendarEvent, "updated");
            return await GetByIdAsync(id);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var calendarEvent = await LoadForChangeAsync(id, "Only the owner of an event can delete it.");
            if (calendarEvent == null) return false;

            var actorId = _currentUser.RequireUserId();

            calendarEvent.SoftDelete(actorId.ToString());
            await _reminders.RemoveAsync(CalendarItemKind.Event, id);
            await _unitOfWork.CompleteAsync();

            await _announcer.NotifyAttendeesAsync(
                calendarEvent, actorId, NotificationType.EventCancelled, "Event cancelled",
                actor => $"{actor} cancelled \"{calendarEvent.Title}\".");
            await _announcer.ChangedAsync(calendarEvent, "deleted");
            return true;
        }

        // ── Single dates of a repeating event ─────────────────────────────────

        public async Task<EventDto?> SaveOccurrenceAsync(Guid id, SaveOccurrenceDto dto)
        {
            var calendarEvent = await LoadForChangeAsync(id, "Only the owner of an event can change it.");
            if (calendarEvent == null) return null;

            if (!calendarEvent.IsRecurring)
                throw new ApiException("This event does not repeat. Change the event itself instead.", 400);

            var exception = calendarEvent.Exceptions.FirstOrDefault(x => x.OccurrenceStart == dto.OccurrenceStart);
            if (exception == null)
            {
                if (!EventDates.IsSeriesDate(calendarEvent, dto.OccurrenceStart))
                    throw new ApiException("\"occurrenceStart\" is not one of the dates of this event.", 400);

                exception = new EventException(id, dto.OccurrenceStart);
                await _unitOfWork.Repository<EventException>().AddAsync(exception);
            }

            if (dto.Cancelled)
                exception.Cancel();
            else if (dto.StartsAt.HasValue && dto.EndsAt.HasValue)
                exception.Change(dto.StartsAt.Value, dto.EndsAt.Value, dto.Title, dto.Location);
            else
                throw new ApiException("Send \"startsAt\" and \"endsAt\" for this date, or \"cancelled\": true to skip it.", 400);

            return await SaveSeriesChangeAsync(calendarEvent, dto.Cancelled ? "cancelled one date of" : "changed one date of");
        }

        public async Task<bool> RestoreOccurrenceAsync(Guid id, DateTime occurrenceStart)
        {
            var calendarEvent = await LoadForChangeAsync(id, "Only the owner of an event can change it.");
            if (calendarEvent == null) return false;

            var exception = calendarEvent.Exceptions.FirstOrDefault(x => x.OccurrenceStart == occurrenceStart);
            if (exception == null) return false;

            // Taking it out of the collection deletes the row on save.
            calendarEvent.Exceptions.Remove(exception);

            await SaveSeriesChangeAsync(calendarEvent, "restored one date of");
            return true;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private async Task<EventDto?> SaveSeriesChangeAsync(CalendarEvent calendarEvent, string whatHappened)
        {
            var actorId = _currentUser.RequireUserId();

            await _reminders.RescheduleAsync(calendarEvent);
            calendarEvent.SetUpdated(actorId.ToString());
            await _unitOfWork.CompleteAsync();

            await _announcer.NotifyAttendeesAsync(
                calendarEvent, actorId, NotificationType.EventChanged, "Event changed",
                actor => $"{actor} {whatHappened} \"{calendarEvent.Title}\".");
            await _announcer.ChangedAsync(calendarEvent, "updated");

            return await GetByIdAsync(calendarEvent.Id);
        }

        /// <summary>The event, tracked and with its details; null when it does not exist, 403 when the caller may not change it.</summary>
        private async Task<CalendarEvent?> LoadForChangeAsync(Guid id, string refusal)
        {
            var calendarEvent = await Events.GetEntityWithSpec(new EventsWithDetailsSpec(id));
            if (calendarEvent == null) return null;

            await _access.RequireAsync(_access.CanManageEventAsync(id), refusal);
            return calendarEvent;
        }

        /// <summary>An event can only be filed under a project the caller belongs to.</summary>
        private async Task RequireProjectAsync(Guid? groupId)
        {
            if (!groupId.HasValue) return;

            await _access.RequireAsync(
                _access.CanViewGroupAsync(groupId.Value), "You are not a member of that project.");
        }

        private static bool IsRescheduledBy(CalendarEvent calendarEvent, SaveEventDto dto) =>
            calendarEvent.StartsAt != dto.StartsAt ||
            calendarEvent.EndsAt != dto.EndsAt ||
            calendarEvent.IsAllDay != dto.IsAllDay ||
            RecurrenceRule.Of(calendarEvent) != new RecurrenceRule(
                dto.Recurrence,
                dto.Recurrence == RecurrencePattern.None ? 1 : Math.Max(1, dto.RecurrenceInterval),
                dto.Recurrence == RecurrencePattern.None ? null : dto.RecurrenceEndDate);
    }
}
