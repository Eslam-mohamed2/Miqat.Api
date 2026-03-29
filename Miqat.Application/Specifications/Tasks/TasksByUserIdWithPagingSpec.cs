using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Tasks
{
    public class TasksByUserIdWithPagingSpec : BaseSpecification<TaskItem>
    {
        public TasksByUserIdWithPagingSpec(Guid userId, int pageIndex, int pageSize)
            : base(t => t.UserId == userId && !t.IsDeleted)
        {
            AddInclude(t => t.AssignedToUser!);
            AddInclude(t => t.Group!);
            AddOrderByDescending(t => t.CreatedAt);
            ApplyPaging(pageIndex, pageSize);
        }
    }
}
