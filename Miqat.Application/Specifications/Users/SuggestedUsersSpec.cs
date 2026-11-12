using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Users
{
    /// <summary>Active, verified users outside the excluded set, newest first.</summary>
    public class SuggestedUsersSpec : BaseSpecification<User>
    {
        public SuggestedUsersSpec(List<Guid> excludedIds, int take)
            : base(u => !excludedIds.Contains(u.Id) &&
                        !u.IsDeleted && u.IsActive && u.IsVerified)
        {
            AddOrderByDescending(u => u.CreatedAt);
            ApplyPaging(0, take);
        }
    }
}
