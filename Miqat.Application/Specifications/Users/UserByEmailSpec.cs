using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Users
{
    public class UserByEmailSpec : BaseSpecification<User>
    {
        public UserByEmailSpec(string email)
            : base(u => u.Email == email && !u.IsDeleted)
        {
            AddInclude(u => u.Tasks);
        }
    }
}
