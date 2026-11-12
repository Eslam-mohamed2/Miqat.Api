using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class NotificationSender : INotificationSender
    {
        /// <summary>Name of the live (SignalR) event the open app listens for.</summary>
        public const string RealtimeEvent = "notification";

        /// <summary>
        /// The kinds worth an email: someone asks the person to do something or
        /// to be somewhere. A friend request or "task completed" is not.
        /// </summary>
        private static readonly HashSet<NotificationType> EmailedTypes = new()
        {
            NotificationType.TaskAssigned,
            NotificationType.TaskDueSoon,
            NotificationType.GroupInvite,
            NotificationType.MentionedInTask,
            NotificationType.EventInvitation,
            NotificationType.EventChanged,
            NotificationType.EventCancelled,
            NotificationType.Reminder
        };

        private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IRealtimeNotifier _realtime;
        private readonly IBackgroundQueue _queue;
        private readonly IEmailService _email;
        private readonly IPushSender _push;
        private readonly NotificationSettings _settings;
        private readonly ILogger<NotificationSender> _logger;

        public NotificationSender(
            IUnitOfWork unitOfWork,
            IRealtimeNotifier realtime,
            IBackgroundQueue queue,
            IEmailService email,
            IPushSender push,
            NotificationSettings settings,
            ILogger<NotificationSender> logger)
        {
            _unitOfWork = unitOfWork;
            _realtime = realtime;
            _queue = queue;
            _email = email;
            _push = push;
            _settings = settings;
            _logger = logger;
        }

        public async Task<StagedNotifications> StageAsync(NotificationContent content, IEnumerable<Guid> recipientIds)
        {
            var targets = recipientIds.Where(id => id != content.ActorId).Distinct().ToList();
            if (targets.Count == 0) return StagedNotifications.None;

            var actorName = await ActorNameAsync(content.ActorId);
            var message = content.Message(actorName ?? "Someone");

            var items = new List<Notification>();
            foreach (var recipientId in targets)
            {
                var notification = new Notification(
                    content.Title, message, content.Type, recipientId, content.ActorId,
                    content.LinkedEntityId, content.LinkedEntityType, content.LinkedCommentId);

                await _unitOfWork.Repository<Notification>().AddAsync(notification);
                items.Add(notification);
            }

            return new StagedNotifications(items, actorName);
        }

        public async Task DeliverAsync(StagedNotifications staged)
        {
            if (staged.Items.Count == 0) return;

            try
            {
                // One query for the settings of every recipient.
                var ids = staged.Items.Select(n => n.RecipientUserId).Distinct().ToList();
                var recipients = (await _unitOfWork.Repository<User>().SelectAsync(
                        u => ids.Contains(u.Id) && !u.IsDeleted && u.IsActive,
                        u => new Recipient(u.Id, u.Email, u.FullName,
                            u.PushNotificationsEnabled, u.EmailNotificationsEnabled)))
                    .ToDictionary(r => r.Id);

                // "Push" in Settings covers both the pop-up in the open app and
                // the notification on a device. The stored notification is not
                // affected: it is in the feed either way.
                var pushed = staged.Items
                    .Where(n => recipients.TryGetValue(n.RecipientUserId, out var r) && r.WantsPush)
                    .ToList();

                foreach (var notification in pushed)
                    await _realtime.NotifyUserAsync(
                        notification.RecipientUserId, RealtimeEvent, Payload(notification, staged.ActorName));

                await QueueDevicePushesAsync(pushed, staged.ActorName);
                QueueEmails(staged.Items, recipients);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Delivering {Count} notification(s) failed; they stay in the feed.", staged.Items.Count);
            }
        }

        public async Task SendAsync(NotificationContent content, IEnumerable<Guid> recipientIds)
        {
            try
            {
                var staged = await StageAsync(content, recipientIds);
                if (staged.Items.Count == 0) return;

                await _unitOfWork.CompleteAsync();
                await DeliverAsync(staged);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sending a {Type} notification failed.", content.Type);
            }
        }

        /// <summary>What the open app receives. Device notifications carry the same values.</summary>
        public static object Payload(Notification notification, string? actorName) => new
        {
            id = notification.Id,
            title = notification.Title,
            message = notification.Message,
            type = notification.Type.ToString(),
            linkedEntityId = notification.LinkedEntityId,
            linkedEntityType = notification.LinkedEntityType,
            linkedCommentId = notification.LinkedCommentId,
            triggeredByUserName = actorName
        };

        /// <summary>Null for system notifications; "Someone" when the person cannot be found.</summary>
        private async Task<string?> ActorNameAsync(Guid? actorId)
        {
            if (!actorId.HasValue) return null;

            var actor = await _unitOfWork.Repository<User>().GetByIdAsync(actorId.Value);
            return string.IsNullOrWhiteSpace(actor?.FullName) ? "Someone" : actor!.FullName;
        }

        private async Task QueueDevicePushesAsync(List<Notification> notifications, string? actorName)
        {
            if (!_push.IsConfigured || notifications.Count == 0) return;

            var userIds = notifications.Select(n => n.RecipientUserId).Distinct().ToList();
            var devices = await _unitOfWork.Repository<Device>()
                .SelectAsync(d => userIds.Contains(d.UserId), d => d);

            foreach (var notification in notifications)
            {
                var payload = JsonSerializer.Serialize(Payload(notification, actorName), PayloadJson);

                foreach (var device in devices.Where(d => d.UserId == notification.RecipientUserId))
                    _queue.Enqueue(services => PushToDeviceAsync(services, device, payload));
            }
        }

        /// <summary>Runs later, with its own services. A subscription the push service no longer knows is forgotten.</summary>
        private static async Task PushToDeviceAsync(IServiceProvider services, Device device, string payload)
        {
            if (await services.GetRequiredService<IPushSender>().SendAsync(device, payload)) return;

            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var stale = await unitOfWork.Repository<Device>().GetByIdAsync(device.Id);
            if (stale == null) return;

            unitOfWork.Repository<Device>().Delete(stale);
            await unitOfWork.CompleteAsync();
        }

        private void QueueEmails(IReadOnlyList<Notification> notifications, Dictionary<Guid, Recipient> recipients)
        {
            if (!_settings.EmailEnabled || !_email.IsConfigured) return;

            foreach (var notification in notifications.Where(n => EmailedTypes.Contains(n.Type)))
            {
                if (!recipients.TryGetValue(notification.RecipientUserId, out var recipient) || !recipient.WantsEmail)
                    continue;

                var (title, message) = (notification.Title, notification.Message);
                _queue.Enqueue(services => services.GetRequiredService<IEmailService>()
                    .SendNotificationAsync(recipient.Email, recipient.FullName, title, message));
            }
        }

        private sealed record Recipient(Guid Id, string Email, string FullName, bool WantsPush, bool WantsEmail);
    }
}
