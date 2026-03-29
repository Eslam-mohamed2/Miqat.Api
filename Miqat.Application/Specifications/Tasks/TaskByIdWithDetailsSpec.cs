using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Tasks
{
    public class TaskByIdWithDetailsSpec : BaseSpecification<TaskItem>
    {
        public TaskByIdWithDetailsSpec(Guid taskId)
            : base(t => t.Id == taskId && !t.IsDeleted)
        {
            AddInclude(t => t.User);
            AddInclude(t => t.AssignedToUser!);
            AddInclude(t => t.Group!);
        }
    }
}
