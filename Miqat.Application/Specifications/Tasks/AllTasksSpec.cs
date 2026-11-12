using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Tasks
{
    public class AllTasksSpec : BaseSpecification<TaskItem>
    {
        public AllTasksSpec() : base(t => !t.IsDeleted)
        {
            AddInclude(t => t.User);
            AddInclude(t => t.AssignedToUser!);
            AddInclude(t => t.Group!);
            AddOrderByDescending(t => t.CreatedAt);
        }
    }
}
