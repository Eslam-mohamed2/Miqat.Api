using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Tasks
{
    public class TasksByGroupSpec : BaseSpecification<TaskItem>
    {
        public TasksByGroupSpec(Guid groupId)
            : base(t => t.GroupId == groupId && !t.IsDeleted)
        {
            AddInclude(t => t.User);
            AddInclude(t => t.AssignedToUser!);
            AddOrderByDescending(t => t.CreatedAt);
        }
    }
}
