using System.Linq.Expressions;
using Miqat.Application.Common;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using Miqat.Domain.Specifications;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Specifications.Tasks
{
    /// <summary>
    /// The tasks of a user (created by or assigned to them) that belong on the
    /// calendar between two moments: tasks with a due date in the range, plus
    /// open repeating tasks whose later dates may fall in it. Narrowed in the
    /// database, so the calendar no longer loads every task to show one month.
    /// </summary>
    public class TasksOnCalendarSpec : BaseSpecification<TaskItem>
    {
        public TasksOnCalendarSpec(Guid userId, DateRange range)
            : base(Touching(userId, range.From, range.To))
        {
            AddInclude(t => t.Group!);
        }

        private static Expression<Func<TaskItem, bool>> Touching(Guid userId, DateTime from, DateTime to)
        {
            // "Repeat until" covers that whole day wherever the user is.
            var seriesEndFloor = from.AddDays(-1);

            return t => (t.UserId == userId || t.AssignedToUserId == userId) &&
                        !t.IsDeleted &&
                        t.DueDate != null &&
                        (t.StartDate ?? t.DueDate) < to &&
                        (t.DueDate >= from ||
                         (t.Recurrence != RecurrencePattern.None &&
                          t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled &&
                          (t.RecurrenceEndDate == null || t.RecurrenceEndDate >= seriesEndFloor)));
        }
    }
}
