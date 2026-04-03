using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Groups;

public class GroupWithOwnerSpec : BaseSpecification<Group>
{
    public GroupWithOwnerSpec()
    {
        AddInclude(g => g.Owner);
    }
}