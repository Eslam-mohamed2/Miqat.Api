using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Users
{
    /// <summary>Case-insensitive lookup of a non-deleted user by email (pass it trimmed and lower-cased).</summary>
    public class UserByEmailSpec : BaseSpecification<User>
    {
        public UserByEmailSpec(string normalizedEmail)
            : base(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted)
        {
        }
    }
}
