namespace Miqat.Application.Modules;

public class CommentDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid TaskId { get; set; }
    public Guid AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorProfilePictureUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateCommentDto
{
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Who was @mentioned, chosen from the picker. Sent explicitly rather than
    /// parsed out of the text: names are ambiguous and change, ids do not.
    /// </summary>
    public List<Guid> MentionedUserIds { get; set; } = new();
}

/// <summary>Someone who can be @mentioned on a task.</summary>
public class MentionableUserDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ProfilePictureUrl { get; set; }

    /// <summary>"Project member", "Assignee", "Friend" — shown in the picker.</summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>False when mentioning them would also grant them access.</summary>
    public bool HasAccess { get; set; }
}
