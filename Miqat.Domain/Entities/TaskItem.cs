using Miqat.Domain.Enumerations;
namespace Miqat.Domain.Entities
{
    public class TaskItem : Common.BaseEntity
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Enumerations.TaskStatus Status { get; set; } = Enumerations.TaskStatus.Pending;
        public Priority Priority { get; set; } = Priority.Medium;
        /// <summary>When work is planned to begin. With DueDate it gives the calendar a real time range.</summary>
        public DateTime? StartDate { get; set; }

        public DateTime? DueDate { get; set; }

        /// <summary>Set when the task becomes Completed, cleared when it is reopened. See <see cref="ChangeStatus"/>.</summary>
        public DateTime? CompletedAt { get; private set; }
        public string? Tags { get; set; }
        public RecurrencePattern Recurrence { get; set; } = RecurrencePattern.None;
        public DateTime? RecurrenceEndDate { get; set; }

        // Owner
        public Guid UserId { get; set; }
        public virtual User User { get; set; } = null!;

        // Assigned to (different from owner)
        public Guid? AssignedToUserId { get; set; }
        public virtual User? AssignedToUser { get; set; }

        // Optional group context
        public Guid? GroupId { get; set; }
        public virtual Group? Group { get; set; }

        /// <summary>
        /// The due date the "due soon" reminder was last sent for. The reminder
        /// job skips the task while this equals <see cref="DueDate"/>, and
        /// reminds again when the due date moves.
        /// </summary>
        public DateTime? DueSoonRemindedFor { get; set; }

        public virtual ICollection<Comment> Comments { get; private set; } = new List<Comment>();
        public virtual ICollection<Subtask> Subtasks { get; private set; } = new List<Subtask>();

        /// <summary>The tasks this one waits for.</summary>
        public virtual ICollection<TaskDependency> Prerequisites { get; private set; } = new List<TaskDependency>();

        public bool IsOpen =>
            Status != Enumerations.TaskStatus.Completed && Status != Enumerations.TaskStatus.Cancelled;

        public TaskItem(string title, string? description, Guid userId,
                        Priority priority = Priority.Medium,
                        DateTime? dueDate = null,
                        Guid? assignedToUserId = null,
                        Guid? groupId = null,
                        string? tags = null,
                        RecurrencePattern recurrence = RecurrencePattern.None,
                        DateTime? recurrenceEndDate = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title cannot be empty.", nameof(title));

            Title = title;
            Description = description;
            UserId = userId;
            Priority = priority;
            DueDate = dueDate;
            AssignedToUserId = assignedToUserId;
            GroupId = groupId;
            Tags = tags;
            Status = Enumerations.TaskStatus.Pending;
            Recurrence = recurrence;
            RecurrenceEndDate = recurrenceEndDate;
        }

        /// <summary>
        /// Changes the status and keeps <see cref="CompletedAt"/> in step with it,
        /// so "when was this finished" never has to be guessed from UpdatedAt.
        /// </summary>
        public void ChangeStatus(Enumerations.TaskStatus status)
        {
            if (status == Enumerations.TaskStatus.Completed)
                CompletedAt ??= DateTime.UtcNow;
            else
                CompletedAt = null;

            Status = status;
        }
    }
}

