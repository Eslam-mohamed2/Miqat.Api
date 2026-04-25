using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Groups;

public class GroupMembersByGroupIdWithUsersSpec : BaseSpecification<GroupMember>
{
    public GroupMembersByGroupIdWithUsersSpec(Guid groupId, int pageIndex, int pageSize)
        : base(gm => gm.GroupId == groupId)
    {
        AddInclude(gm => gm.User);
        AddOrderByDescending(gm => gm.JoinedAt);
        ApplyPaging(pageIndex, pageSize);
    }
}
