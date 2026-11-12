using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Tasks
{
    /// <summary>Tasks (owned or assigned) that are due between the two instants.</summary>
    public class TasksDueSoonSpec : BaseSpecification<TaskItem>
    {
        public TasksDueSoonSpec(Guid userId, DateTime from, DateTime until)
            : base(t =>
                (t.UserId == userId || t.AssignedToUserId == userId) &&
                !t.IsDeleted &&
                t.DueDate.HasValue &&
                t.DueDate.Value <= until &&
                t.DueDate.Value >= from)
        {
            AddInclude(t => t.User);
            AddInclude(t => t.AssignedToUser!);
            AddInclude(t => t.Group!);
            AddOrderBy(t => t.DueDate!);
        }
    }
}
