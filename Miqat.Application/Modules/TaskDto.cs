namespace Miqat.Application.Modules;

public class TaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Pending | In_progress | Completed | Cancelled | On_hold</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Low | Medium | High | Critical</summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>Optional planned start. Together with DueDate it is the task's time range.</summary>
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }

    public string? Tags { get; set; }
    public string? Recurrence { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }

    // Owner
    public Guid UserId { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerProfilePictureUrl { get; set; }

    // Assignee
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToUserName { get; set; }
    public string? AssignedToUserProfilePictureUrl { get; set; }

    // Project
    public Guid? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? GroupColor { get; set; }

    // ── Read-only: sent by the API, ignored when a task is saved ─────────────

    /// <summary>Number of comments in the task's discussion.</summary>
    public int CommentCount { get; set; }

    /// <summary>Lines on the checklist, and how many of them are ticked.</summary>
    public int SubtaskCount { get; set; }
    public int CompletedSubtaskCount { get; set; }

    /// <summary>True while a task this one waits for is not finished.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>True when the due date has passed and the task is still open.</summary>
    public bool IsOverdue { get; set; }

    /// <summary>When the task was marked Completed; null while it is open.</summary>
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
