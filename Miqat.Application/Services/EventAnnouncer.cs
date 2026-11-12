using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <summary>
    /// Tells people about an event: a live "refresh your calendar" to everyone
    /// who has it on theirs, and a notification to the people invited to it.
    /// Shared by the event module and the invitation module so both announce
    /// the same way.
    /// </summary>
    public class EventAnnouncer
    {
        /// <summary>Name of the live (SignalR) event; the calendar reloads when it arrives.</summary>
        public const string RealtimeEvent = "eventChanged";

        private readonly IRealtimeNotifier _realtime;
        private readonly INotificationSender _notifications;
        private readonly PeopleDirectory _people;

        public EventAnnouncer(IRealtimeNotifier realtime, INotificationSender notifications, PeopleDirectory people)
        {
            _realtime = realtime;
            _notifications = notifications;
            _people = people;
        }

        /// <summary>
        /// Refreshes the calendar of everyone who shows the event: its owner,
        /// the invited people and the members of its project. The person who
        /// made the change is included on purpose - their other tabs update too.
        /// </summary>
        public async Task ChangedAsync(CalendarEvent calendarEvent, string action, IEnumerable<Guid>? alsoTell = null)
        {
            var audience = calendarEvent.Attendees.Select(a => a.UserId).ToHashSet();
            audience.Add(calendarEvent.OwnerId);
            if (alsoTell != null) audience.UnionWith(alsoTell);

            if (calendarEvent.GroupId.HasValue)
                audience.UnionWith(await _people.GetProjectAudienceAsync(calendarEvent.GroupId.Value));

            await _realtime.NotifyUsersAsync(audience, RealtimeEvent, new
            {
                eventId = calendarEvent.Id,
                action,
                title = calendarEvent.Title
            });
        }

        /// <summary>A notification to the invited people who have not declined.</summary>
        public Task NotifyAttendeesAsync(
            CalendarEvent calendarEvent, Guid actorId, NotificationType type, string title, Func<string, string> message) =>
            NotifyAsync(
                calendarEvent, actorId, type, title, message,
                calendarEvent.Attendees.Where(a => a.Status != AttendeeStatus.Declined).Select(a => a.UserId));

        public Task NotifyAsync(
            CalendarEvent calendarEvent, Guid actorId, NotificationType type, string title,
            Func<string, string> message, IEnumerable<Guid> recipients) =>
            _notifications.SendAsync(
                new NotificationContent(type, title, message, actorId, calendarEvent.Id, "Event"), recipients);
    }
}
