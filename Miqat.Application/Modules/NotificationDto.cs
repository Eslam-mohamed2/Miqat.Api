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
    public Guid? LinkedEntityId { get; set; }
    public string? LinkedEntityType { get; set; }
    public DateTime CreatedAt { get; set; }
}