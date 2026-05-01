namespace Miqat.Application.Modules;

public class MentionDto
{
    public Guid Id { get; set; }
    public Guid MentionedByUserId { get; set; }
    public string? MentionedByUserName { get; set; }
    public string? MentionedByUserProfilePictureUrl { get; set; }
    public Guid MentionedUserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
