using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Groups;

public class GroupMembersByGroupIdSpec : BaseSpecification<GroupMember>
{
    public GroupMembersByGroupIdSpec(Guid groupId)
        : base(gm => gm.GroupId == groupId)
    {
    }
}
