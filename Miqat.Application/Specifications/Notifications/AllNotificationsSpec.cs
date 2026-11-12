using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;
using System;

namespace Miqat.Application.Specifications.Notifications
{
    /// <summary>
    /// Every notification for a user, newest first.
    ///
    /// The include matters: NotificationDto.TriggeredByUserName is mapped from the
    /// TriggeredByUser navigation property, so without it the actor's name comes
    /// back null and the feed cannot show who did the thing.
    /// </summary>
    public class AllNotificationsSpec : BaseSpecification<Notification>
    {
        public AllNotificationsSpec(Guid userId)
            : base(n =>
                n.RecipientUserId == userId &&
                !n.IsDeleted)
        {
            AddInclude(n => n.TriggeredByUser!);
            AddOrderByDescending(n => n.CreatedAt);
        }
    }
}
