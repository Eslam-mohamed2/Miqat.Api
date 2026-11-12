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

    /// <summary>Pending | Accepted | Rejected | Blocked</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // ── "The other person", from the caller's point of view ──────────────────
    // A friendship row has a sender and a receiver; which of them is the friend
    // depends on who is asking. These save every client from working it out.

    public Guid? FriendId { get; set; }
    public string? FriendName { get; set; }
    public string? FriendEmail { get; set; }
    public string? FriendProfilePictureUrl { get; set; }

    /// <summary>True when the caller sent this request.</summary>
    public bool SentByMe { get; set; }

    // The same people as nested objects.
    public UserCardDto? Sender { get; set; }
    public UserCardDto? Receiver { get; set; }
    public UserCardDto? FriendUser { get; set; }
}

/// <summary>The public part of a user: enough to show a name and an avatar and link to the profile.</summary>
public class UserCardDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? Country { get; set; }
}
