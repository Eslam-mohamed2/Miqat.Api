using Miqat.Domain.Enumerations;

namespace Miqat.Application.Modules;

// ── Reminders (tasks and events) ─────────────────────────────────────────────

public class ReminderDto
{
    public Guid Id { get; set; }
    public CalendarItemKind ItemKind { get; set; }
    public Guid ItemId { get; set; }
    public string? ItemTitle { get; set; }
    public int MinutesBefore { get; set; }

    /// <summary>When it fires next; null when nothing is ahead (the item is finished or in the past).</summary>
    public DateTime? RemindAt { get; set; }
    public DateTime? LastSentAt { get; set; }
}

public class SaveReminderDto
{
    /// <summary>0 reminds at the start (or due) time itself.</summary>
    public int MinutesBefore { get; set; }
}

public class SnoozeReminderDto
{
    public int Minutes { get; set; } = 10;
}

// ── Attachments (tasks and events) ───────────────────────────────────────────

public class AttachmentDto
{
    public Guid Id { get; set; }
    public CalendarItemKind ItemKind { get; set; }
    public Guid ItemId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string? UploadedByName { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Relative address to download the file from; it needs the usual Authorization header.</summary>
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>What a download hands to the controller: the stored details and the open file.</summary>
public sealed record AttachmentFile(string FileName, string ContentType, Stream Content);

// ── Subtasks (checklist of a task) ───────────────────────────────────────────

public class SubtaskDto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int Position { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Create needs a title. On update, values that are left out keep what is stored.</summary>
public class SaveSubtaskDto
{
    public string? Title { get; set; }
    public bool? IsDone { get; set; }

    /// <summary>0 is the top of the checklist. The other lines move to make room.</summary>
    public int? Position { get; set; }
}

// ── Dependencies between tasks ───────────────────────────────────────────────

/// <summary>A task as it appears in a list of links: enough to show and to open it.</summary>
public class TaskLinkDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public bool IsDone { get; set; }
}

public class TaskDependenciesDto
{
    /// <summary>Tasks that have to be finished before this one.</summary>
    public List<TaskLinkDto> BlockedBy { get; set; } = new();

    /// <summary>Tasks that wait for this one.</summary>
    public List<TaskLinkDto> Blocks { get; set; } = new();

    /// <summary>True while at least one of <see cref="BlockedBy"/> is not finished.</summary>
    public bool IsBlocked { get; set; }
}

public class AddDependencyDto
{
    public Guid DependsOnTaskId { get; set; }
}
