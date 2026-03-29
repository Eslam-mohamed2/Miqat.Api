using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Tasks
{
    public class TasksDueSoonSpec : BaseSpecification<TaskItem>
    {
        public TasksDueSoonSpec(Guid userId, int withinDays = 3)
            : base(t =>
                t.UserId == userId &&
                !t.IsDeleted &&
                t.DueDate.HasValue &&
                t.DueDate.Value <= DateTime.UtcNow.AddDays(withinDays) &&
                t.DueDate.Value >= DateTime.UtcNow)
        {
            AddInclude(t => t.Group!);
            AddOrderBy(t => t.DueDate!);
        }
    }
}
