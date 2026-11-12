using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;

namespace Miqat.Application.Specifications.Notifications
{
    /// <summary>One page of a user's feed, newest first, actor included.</summary>
    public class AllNotificationsPagedSpec : BaseSpecification<Notification>
    {
        public AllNotificationsPagedSpec(Guid userId, int pageIndex, int pageSize)
            : base(n => n.RecipientUserId == userId && !n.IsDeleted)
        {
            AddInclude(n => n.TriggeredByUser!);
            AddOrderByDescending(n => n.CreatedAt);
            ApplyPaging(pageIndex, pageSize);
        }
    }
}
