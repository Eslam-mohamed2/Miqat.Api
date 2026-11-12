namespace Miqat.Application.Modules;

public class NotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public Guid? TriggeredByUserId { get; set; }
    public string? TriggeredByUserName { get; set; }

    /// <summary>Picture of the person who caused it, for the feed avatar.</summary>
    public string? TriggeredByUserProfilePictureUrl { get; set; }
    public Guid? LinkedEntityId { get; set; }
    public string? LinkedEntityType { get; set; }

    /// <summary>Title of the linked task or name of the linked project, when there is one.</summary>
    public string? LinkedEntityName { get; set; }

    /// <summary>Anchor within the linked task, when the notification is about a comment.</summary>
    public Guid? LinkedCommentId { get; set; }
    public DateTime CreatedAt { get; set; }
}