using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Common;

/// <summary>
/// Notification -> NotificationDto. The triggering user is read through a
/// null-safe navigation (system notifications have none).
/// LinkedEntityName needs a lookup and is filled in by NotificationService.
/// </summary>
public class NotificationMapper
{
    public NotificationDto MapToDto(Notification notification) => new()
    {
        Id = notification.Id,
        Title = notification.Title,
        Message = notification.Message,
        IsRead = notification.IsRead,
        Type = notification.Type.ToString(),
        RecipientUserId = notification.RecipientUserId,
        TriggeredByUserId = notification.TriggeredByUserId,
        TriggeredByUserName = notification.TriggeredByUser?.FullName,
        TriggeredByUserProfilePictureUrl = notification.TriggeredByUser?.ProfilePictureUrl,
        LinkedEntityId = notification.LinkedEntityId,
        LinkedEntityType = notification.LinkedEntityType,
        LinkedCommentId = notification.LinkedCommentId,
        CreatedAt = notification.CreatedAt
    };

    public IEnumerable<NotificationDto> MapToDtos(IEnumerable<Notification> notifications)
        => notifications.Select(MapToDto).ToList();
}
