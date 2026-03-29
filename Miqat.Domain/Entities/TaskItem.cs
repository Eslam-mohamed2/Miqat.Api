using Miqat.Domain.Enumerations;
namespace Miqat.Domain.Entities
{
    public class TaskItem : Common.BaseEntity
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Enumerations.TaskStatus Status { get; set; } = Enumerations.TaskStatus.Pending;
        public Priority Priority { get; set; } = Priority.Medium;
        public DateTime? DueDate { get; set; }
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
    }
}

