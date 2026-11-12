using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Events;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class EventAttendeeService : IEventAttendeeService
    {
        private const int MaxAttendees = 100;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IAccessPolicy _access;
        private readonly IReminderService _reminders;
        private readonly PeopleDirectory _people;
        private readonly EventAnnouncer _announcer;

        public EventAttendeeService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IAccessPolicy access,
            IReminderService reminders,
            PeopleDirectory people,
            EventAnnouncer announcer)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _access = access;
            _reminders = reminders;
            _people = people;
            _announcer = announcer;
        }

        private IGenericRepository<CalendarEvent> Events => _unitOfWork.Repository<CalendarEvent>();

        public async Task<List<EventDto>> GetInvitationsAsync()
        {
            var userId = _currentUser.RequireUserId();
            var events = await Events.ListAsync(new PendingInvitationsSpec(userId, DateTime.UtcNow));

            return events.Select(e => EventMapper.ToDto(e, userId)).ToList();
        }

        public async Task<EventDto?> InviteAsync(Guid eventId, IEnumerable<Guid> userIds)
        {
            var calendarEvent = await LoadAsync(eventId);
            if (calendarEvent == null) return null;

            await _access.RequireAsync(
                _access.CanManageEventAsync(eventId), "Only the owner of an event can invite people.");

            var invited = await StageInvitationsAsync(calendarEvent, userIds);
            await _unitOfWork.CompleteAsync();

            await NotifyInvitedAsync(calendarEvent, invited);
            await _announcer.ChangedAsync(calendarEvent, "updated");

            return await ReloadAsync(eventId);
        }

        public async Task<bool> RemoveAsync(Guid eventId, Guid userId)
        {
            var calendarEvent = await LoadAsync(eventId);
            var attendee = calendarEvent?.Attendees.FirstOrDefault(a => a.UserId == userId);
            if (calendarEvent == null || attendee == null) return false;

            // The owner can remove anyone; an invited person can remove themselves.
            if (_currentUser.RequireUserId() != userId)
            {
                await _access.RequireAsync(
                    _access.CanManageEventAsync(eventId), "Only the owner of an event can remove other people.");
            }

            // Taking the row out of the collection deletes it on save.
            calendarEvent.Attendees.Remove(attendee);
            await _reminders.RemoveAsync(CalendarItemKind.Event, eventId, userId);
            await _unitOfWork.CompleteAsync();

            await _announcer.ChangedAsync(calendarEvent, "updated", alsoTell: new[] { userId });
            return true;
        }

        public async Task<EventDto?> RespondAsync(Guid eventId, AttendeeStatus status)
        {
            var userId = _currentUser.RequireUserId();

            var calendarEvent = await LoadAsync(eventId);
            if (calendarEvent == null) return null;

            var attendee = calendarEvent.Attendees.FirstOrDefault(a => a.UserId == userId)
                ?? throw new ApiException("You are not invited to this event.", 403);

            if (status == AttendeeStatus.Pending)
                throw new ApiException("Answer with Accepted, Tentative or Declined.", 400);

            if (attendee.Status != status)
            {
                attendee.Respond(status);

                // Coming (or maybe coming) gets the usual reminder of the person;
                // declining removes any they had.
                if (status == AttendeeStatus.Declined)
                    await _reminders.RemoveAsync(CalendarItemKind.Event, eventId, userId);
                else
                    await _reminders.StageForEventAsync(calendarEvent, userId);

                await _unitOfWork.CompleteAsync();

                await _announcer.NotifyAsync(
                    calendarEvent, userId, NotificationType.EventResponse, "Invitation answered",
                    actor => $"{actor} {Describe(status)} \"{calendarEvent.Title}\".",
                    new[] { calendarEvent.OwnerId });
                await _announcer.ChangedAsync(calendarEvent, "updated");
            }

            return await ReloadAsync(eventId);
        }

        public async Task<List<Guid>> StageInvitationsAsync(CalendarEvent calendarEvent, IEnumerable<Guid> userIds)
        {
            var already = calendarEvent.Attendees.Select(a => a.UserId).ToHashSet();

            var requested = userIds
                .Where(id => id != Guid.Empty && id != calendarEvent.OwnerId && !already.Contains(id))
                .Distinct()
                .ToList();
            if (requested.Count == 0) return requested;

            if (already.Count + requested.Count > MaxAttendees)
                throw new ApiException($"An event can have up to {MaxAttendees} invited people.", 400);

            // The ids come from the client. Without this check anyone could put
            // an event on the calendar of a stranger and send them a notification.
            var contacts = await _people.GetContactIdsAsync(calendarEvent.OwnerId);
            if (requested.Any(id => !contacts.Contains(id)))
                throw new ApiException("You can only invite your friends and the people on your projects.", 400);

            var active = await _unitOfWork.Repository<User>()
                .SelectAsync(u => requested.Contains(u.Id) && !u.IsDeleted && u.IsActive, u => u.Id);

            foreach (var userId in active)
                await _unitOfWork.Repository<EventAttendee>().AddAsync(new EventAttendee(calendarEvent.Id, userId));

            return active;
        }

        public Task NotifyInvitedAsync(CalendarEvent calendarEvent, IEnumerable<Guid> userIds) =>
            _announcer.NotifyAsync(
                calendarEvent, _currentUser.UserId ?? calendarEvent.OwnerId,
                NotificationType.EventInvitation, "Event invitation",
                actor => $"{actor} invited you to \"{calendarEvent.Title}\".",
                userIds);

        // ── helpers ───────────────────────────────────────────────────────────

        private Task<CalendarEvent?> LoadAsync(Guid eventId) =>
            Events.GetEntityWithSpec(new EventsWithDetailsSpec(eventId));

        /// <summary>Read again after saving, so the answer carries the names of the people just added.</summary>
        private async Task<EventDto?> ReloadAsync(Guid eventId)
        {
            var calendarEvent = (await Events.ListAsync(new EventsWithDetailsSpec(eventId))).FirstOrDefault();
            return calendarEvent == null ? null : EventMapper.ToDto(calendarEvent, _currentUser.RequireUserId());
        }

        private static string Describe(AttendeeStatus status) => status switch
        {
            AttendeeStatus.Accepted => "accepted the invitation to",
            AttendeeStatus.Declined => "declined the invitation to",
            _ => "may come to"
        };
    }
}
