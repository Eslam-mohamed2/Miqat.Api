using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// What a notification says and what it points at.
    /// <paramref name="Message"/> receives the name of the person who caused it
    /// ("Someone" when unknown), so callers do not have to look that name up.
    /// </summary>
    public sealed record NotificationContent(
        NotificationType Type,
        string Title,
        Func<string, string> Message,
        Guid? ActorId = null,
        Guid? LinkedEntityId = null,
        string? LinkedEntityType = null,
        Guid? LinkedCommentId = null);

    /// <summary>Notifications that were added to the unit of work and are waiting to be saved and delivered.</summary>
    public sealed record StagedNotifications(IReadOnlyList<Notification> Items, string? ActorName)
    {
        public static readonly StagedNotifications None = new(Array.Empty<Notification>(), null);
    }

    /// <summary>
    /// The one place a notification is created and sent from. Every module
    /// goes through it, so they all store the same thing and all honour the
    /// same settings of the recipient: live pop-up, devices, email.
    /// </summary>
    public interface INotificationSender
    {
        /// <summary>
        /// Adds one notification per recipient to the unit of work, without
        /// saving. The person who caused it never gets one. Use it when the
        /// notification must be saved together with the change it is about,
        /// then call <see cref="DeliverAsync"/> after saving.
        /// </summary>
        Task<StagedNotifications> StageAsync(NotificationContent content, IEnumerable<Guid> recipientIds);

        /// <summary>Delivers saved notifications. Never throws: they are stored and stay in the feed whatever happens here.</summary>
        Task DeliverAsync(StagedNotifications staged);

        /// <summary>
        /// Stage, save and deliver in one call, for when the change itself is
        /// already saved. Never throws: failing to notify must not turn a
        /// successful change into an error.
        /// </summary>
        Task SendAsync(NotificationContent content, IEnumerable<Guid> recipientIds);
    }
}
