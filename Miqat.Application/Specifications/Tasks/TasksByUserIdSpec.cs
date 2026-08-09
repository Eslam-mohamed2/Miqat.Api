using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Tasks
{
    public class TasksByUserIdSpec : BaseSpecification<TaskItem>
    {
        public TasksByUserIdSpec(Guid userId)
                        // "My tasks" must mean created by me OR assigned to me. Matching the
            // creator only meant work a teammate handed you never appeared on
            // your own task list — it was only visible inside the project view.
            : base(t => (t.UserId == userId || t.AssignedToUserId == userId) && !t.IsDeleted)
        {
            AddInclude(t => t.User);
            AddInclude(t => t.AssignedToUser!);
            AddInclude(t => t.Group!);
            AddOrderByDescending(t => t.CreatedAt);
        }
    }
}
