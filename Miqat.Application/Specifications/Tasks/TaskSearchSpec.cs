using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Tasks
{
    /// <summary>
    /// Tasks the user can see (owned, assigned, or inside one of their projects)
    /// whose title, description or tags contain the lower-cased term.
    /// </summary>
    public class TaskSearchSpec : BaseSpecification<TaskItem>
    {
        public TaskSearchSpec(Guid userId, List<Guid> visibleGroupIds, string lowerTerm, int take)
            : base(t => !t.IsDeleted &&
                        (t.UserId == userId ||
                         t.AssignedToUserId == userId ||
                         (t.GroupId.HasValue && visibleGroupIds.Contains(t.GroupId.Value))) &&
                        (t.Title.ToLower().Contains(lowerTerm) ||
                         (t.Description != null && t.Description.ToLower().Contains(lowerTerm)) ||
                         (t.Tags != null && t.Tags.ToLower().Contains(lowerTerm))))
        {
            AddInclude(t => t.User);
            AddInclude(t => t.AssignedToUser!);
            AddInclude(t => t.Group!);
            AddOrderByDescending(t => t.CreatedAt);
            ApplyPaging(0, take);
        }
    }
}
