using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Groups
{
    public class GroupByIdWithMembersSpec : BaseSpecification<Group>
    {
        public GroupByIdWithMembersSpec(Guid groupId)
            : base(g => g.Id == groupId && !g.IsDeleted)
        {
            AddInclude(g => g.Members);
            AddInclude(g => g.Tasks);
            AddInclude(g => g.Owner);
            AddInclude("Members.User");
        }
    }
}
