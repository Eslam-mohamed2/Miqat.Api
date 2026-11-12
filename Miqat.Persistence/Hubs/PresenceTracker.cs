using System.Collections.Concurrent;
using Miqat.Application.Interfaces;

namespace Miqat.API.Hubs
{
    /// <inheritdoc />
    /// <remarks>Counts open connections per user: one person can have several tabs and devices.</remarks>
    public class PresenceTracker : IPresenceTracker
    {
        private readonly ConcurrentDictionary<Guid, int> _connections = new();

        public bool IsOnline(Guid userId) => _connections.TryGetValue(userId, out var count) && count > 0;

        public bool Connected(Guid userId) => _connections.AddOrUpdate(userId, 1, (_, count) => count + 1) == 1;

        public bool Disconnected(Guid userId)
        {
            while (_connections.TryGetValue(userId, out var count))
            {
                if (count <= 1)
                {
                    // Removed only if nobody connected in between; otherwise look again.
                    if (_connections.TryRemove(new KeyValuePair<Guid, int>(userId, count))) return true;
                }
                else if (_connections.TryUpdate(userId, count - 1, count))
                {
                    return false;
                }
            }
            return false;
        }
    }
}
