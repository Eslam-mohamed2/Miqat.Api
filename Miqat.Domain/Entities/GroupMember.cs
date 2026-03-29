using Miqat.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Entities
{
    public class GroupMember : BaseEntity
    {
        public Guid GroupId { get; set; }
        public virtual Group Group { get; set; } = null!;

        public Guid UserId { get; set; }
        public virtual User User { get; set; } = null!;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public GroupMember(Guid groupId, Guid userId)
        {
            GroupId = groupId;
            UserId = userId;
            JoinedAt = DateTime.UtcNow;
        }

        private GroupMember() { }

    }
}
