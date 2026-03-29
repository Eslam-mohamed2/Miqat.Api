using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Specifications.Notifications
{
    public class UnreadNotificationsSpec : BaseSpecification<Notification>
    {
        public UnreadNotificationsSpec(Guid userId)
            : base(n =>
                n.RecipientUserId == userId &&
                !n.IsRead &&
                !n.IsDeleted)
        {
            AddInclude(n => n.TriggeredByUser!);
            AddOrderByDescending(n => n.CreatedAt);
        }
    }
}
