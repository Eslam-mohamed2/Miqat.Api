using Miqat.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Entities
{
    public class Group : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Color { get; set; }

        public Guid OwnerId { get; set; }
        public virtual User Owner { get; set; } = null!;

        public virtual ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
        public virtual ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

        public Group(string name, string? description, Guid ownerId, string? color = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Group name cannot be empty.", nameof(name));

            Name = name;
            Description = description;
            OwnerId = ownerId;
            Color = color;
        }

        private Group() { }
    }
}
