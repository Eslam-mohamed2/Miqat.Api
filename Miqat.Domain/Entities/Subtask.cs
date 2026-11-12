using Miqat.Domain.Common;

namespace Miqat.Domain.Entities;

/// <summary>One line of the checklist of a task.</summary>
public class Subtask : BaseEntity
{
    public Guid TaskId { get; private set; }
    public virtual TaskItem Task { get; private set; } = null!;

    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    /// <summary>Order within the checklist, lowest first.</summary>
    public int Position { get; set; }

    public Subtask(Guid taskId, string title, int position)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));

        TaskId = taskId;
        Title = title.Trim();
        Position = position;
    }

    private Subtask() { }

    public void SetDone(bool done)
    {
        if (done == IsDone) return;

        IsDone = done;
        CompletedAt = done ? DateTime.UtcNow : null;
    }
}
