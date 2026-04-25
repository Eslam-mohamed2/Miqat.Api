using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Groups;

public class GroupIdsByUserIdSpec : BaseSpecification<GroupMember>
{
    public GroupIdsByUserIdSpec(Guid userId)
        : base(gm => gm.UserId == userId)
    {
        // only need GroupId and User include is unnecessary here
    }
}
