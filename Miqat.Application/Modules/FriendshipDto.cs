namespace Miqat.Application.Modules;

public class FriendshipDto
{
    public Guid Id { get; set; }
    public Guid SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderProfilePictureUrl { get; set; }
    public Guid ReceiverId { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverProfilePictureUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
