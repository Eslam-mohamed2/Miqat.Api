using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Miqat.Application.Interfaces;

namespace Miqat.API.Hubs
{
    /// <inheritdoc />
    public class SignalRRealtimeNotifier : IRealtimeNotifier
    {
        private readonly IHubContext<MiqatHub> _hub;
        private readonly ILogger<SignalRRealtimeNotifier> _logger;

        public SignalRRealtimeNotifier(
            IHubContext<MiqatHub> hub,
            ILogger<SignalRRealtimeNotifier> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        public async Task NotifyUserAsync(Guid userId, string eventName, object payload)
        {
            try
            {
                await _hub.Clients.Group(MiqatHub.UserGroup(userId.ToString()))
                    .SendAsync(eventName, payload);
            }
            catch (Exception ex)
            {
                // A realtime push is best-effort by contract; the data is already
                // saved and will arrive on the next fetch regardless.
                _logger.LogWarning(ex, "Realtime push '{Event}' to {UserId} failed", eventName, userId);
            }
        }

        public async Task NotifyUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload)
        {
            foreach (var userId in userIds.Distinct())
                await NotifyUserAsync(userId, eventName, payload);
        }
    }
}
