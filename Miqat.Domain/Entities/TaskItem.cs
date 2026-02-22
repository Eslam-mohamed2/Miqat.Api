using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Miqat.Domain.Entities
{
    public class TaskItem : Common.BaseEntity
    {
        public string Title { get;  set; } = string.Empty;
        public string? Description { get;  set; }
        public Enumerations.TaskStatus Status { get;  set; }
        public Guid UserId { get; set; } 
        public virtual User User { get; set; } = null!;

        public TaskItem(string title, string? description, Guid userId) 
        {
        if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title cannot be empty.", nameof(title));
            this.Title = title;
            this.Description = description;
            this.UserId = userId;
            this.Status = Enumerations.TaskStatus.Pending;
        }
    }
}
