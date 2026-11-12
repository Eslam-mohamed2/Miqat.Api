using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Users
{
    /// <summary>Active, verified users whose name or email contains the (lower-cased) term.</summary>
    public class UserSearchSpec : BaseSpecification<User>
    {
        public UserSearchSpec(string lowerTerm, Guid excludeUserId, int take)
            : base(u => u.Id != excludeUserId &&
                        !u.IsDeleted && u.IsActive && u.IsVerified &&
                        (u.FullName.ToLower().Contains(lowerTerm) ||
                         u.Email.ToLower().Contains(lowerTerm)))
        {
            AddOrderBy(u => u.FullName);
            ApplyPaging(0, take);
        }
    }
}
