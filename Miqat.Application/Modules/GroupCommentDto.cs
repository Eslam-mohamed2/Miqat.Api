namespace Miqat.Application.Modules;

/// <summary>A comment in a project's discussion, as the caller sees it.</summary>
public class GroupCommentDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid GroupId { get; set; }

    /// <summary>The comment this one answers; null for a comment of its own. Replies are one level deep.</summary>
    public Guid? ParentId { get; set; }

    public Guid AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorProfilePictureUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>True once the author has changed the text.</summary>
    public bool IsEdited { get; set; }

    /// <summary>The caller wrote it, so may rewrite it.</summary>
    public bool CanEdit { get; set; }

    /// <summary>The caller wrote it or owns the project, so may delete it.</summary>
    public bool CanDelete { get; set; }

    /// <summary>
    /// The id the sending app gave the comment before it was saved. Only on the
    /// answer to POST and on the live "added" event, so the app that sent it can
    /// tell its own comment from somebody else's.
    /// </summary>
    public string? ClientId { get; set; }
}

/// <summary>
/// A page of a project's discussion and whether there is more before it.
/// Comments of their own come oldest first, each one followed by its replies
/// (oldest first), so the list can be shown top to bottom as it is.
/// </summary>
public class GroupCommentPageDto
{
    public List<GroupCommentDto> Comments { get; set; } = new();

    /// <summary>There are older comments: ask again with "before" = the first comment of this page.</summary>
    public bool HasMore { get; set; }

    /// <summary>Comments and replies in the whole discussion.</summary>
    public int Total { get; set; }
}

/// <summary>Body of POST /api/Group/{groupId}/comments.</summary>
public class CreateGroupCommentDto
{
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The comment being answered. Answering a reply puts the new comment under
    /// the same comment as that reply.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Who was @mentioned, chosen from the picker (GET .../mentionable).
    /// Anyone who is not on the project is ignored.
    /// </summary>
    public List<Guid> MentionedUserIds { get; set; } = new();

    /// <summary>Optional, at most 64 characters: handed back as "clientId" on the answer and the live event.</summary>
    public string? ClientId { get; set; }
}

/// <summary>
/// What the live event "projectCommentChanged" carries, to everyone on the
/// project. "comment" is filled for "added" and "edited", with canEdit and
/// canDelete as they are for the person receiving it; "deletedIds" for "deleted".
/// </summary>
public class GroupCommentEventDto
{
    public Guid GroupId { get; set; }

    /// <summary>"added", "edited" or "deleted".</summary>
    public string Action { get; set; } = string.Empty;

    public GroupCommentDto? Comment { get; set; }

    /// <summary>The comment that was deleted and the replies that went with it.</summary>
    public List<Guid> DeletedIds { get; set; } = new();

    /// <summary>Comments and replies in the discussion after the change.</summary>
    public int Total { get; set; }
}
