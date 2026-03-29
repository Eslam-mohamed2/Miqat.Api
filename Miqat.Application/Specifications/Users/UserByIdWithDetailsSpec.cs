using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Users
{
    public class UserByIdWithDetailsSpec : BaseSpecification<User>
    {
        public UserByIdWithDetailsSpec(Guid userId)
            : base(u => u.Id == userId && !u.IsDeleted)
        {
            AddInclude(u => u.GroupMembers);
            AddInclude(u => u.Notifications);
            AddInclude("GroupMembers.Group");
        }
    }
}
