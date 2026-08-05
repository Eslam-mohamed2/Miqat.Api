using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// Pushes an event to a user's live connections (every open tab), so the UI
    /// updates the moment something happens instead of on the next refresh.
    ///
    /// Implementations must never throw: a dropped socket cannot be allowed to
    /// fail the write that triggered the push.
    /// </summary>
    public interface IRealtimeNotifier
    {
        Task NotifyUserAsync(Guid userId, string eventName, object payload);
        Task NotifyUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload);
    }
}
