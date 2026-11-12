using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using Miqat.Domain.Specifications;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Specifications.Tasks
{
    public class TasksByUserIdSpec : BaseSpecification<TaskItem>
    {
        // "My tasks" must mean created by me OR assigned to me. Matching the
        // creator only meant work a teammate handed you never appeared on
        // your own task list — it was only visible inside the project view.
        public TasksByUserIdSpec(Guid userId)
            : base(t => (t.UserId == userId || t.AssignedToUserId == userId) && !t.IsDeleted)
        {
            AddDetails();
        }

        /// <summary>The same list narrowed by the optional filter, in the database.</summary>
        public TasksByUserIdSpec(Guid userId, TaskFilter filter)
            : this(userId, filter.Status, filter.Priority, filter.GroupId, filter.DueFrom, filter.DueTo)
        {
        }

        private TasksByUserIdSpec(
            Guid userId, TaskStatus? status, Priority? priority, Guid? groupId,
            DateTime? dueFrom, DateTime? dueTo)
            : base(t => (t.UserId == userId || t.AssignedToUserId == userId) && !t.IsDeleted &&
                        (status == null || t.Status == status) &&
                        (priority == null || t.Priority == priority) &&
                        (groupId == null || t.GroupId == groupId) &&
                        (dueFrom == null || (t.DueDate != null && t.DueDate >= dueFrom)) &&
                        (dueTo == null || (t.DueDate != null && t.DueDate <= dueTo)))
        {
            AddDetails();
        }

        private void AddDetails()
        {
            AddInclude(t => t.User);
            AddInclude(t => t.AssignedToUser!);
            AddInclude(t => t.Group!);
            AddOrderByDescending(t => t.CreatedAt);
        }
    }
}
