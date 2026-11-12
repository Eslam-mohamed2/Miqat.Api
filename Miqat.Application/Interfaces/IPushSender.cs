using Miqat.Domain.Entities;

namespace Miqat.Application.Interfaces
{
    /// <summary>Sends a notification to a browser that is not showing the app (Web Push).</summary>
    public interface IPushSender
    {
        /// <summary>False until the server has its push keys (Push:PublicKey / Push:PrivateKey).</summary>
        bool IsConfigured { get; }

        /// <summary>The key a browser needs to subscribe; null when not configured.</summary>
        string? PublicKey { get; }

        /// <summary>
        /// True when the browser may be registered: its push service is one of
        /// the known ones. The server posts to this address, so it must not be
        /// something a caller can point at an internal machine.
        /// </summary>
        bool IsAllowedEndpoint(string endpoint);

        /// <summary>
        /// Sends one message to one device. Returns false when the push service
        /// answers that the subscription no longer exists - the device should
        /// then be forgotten.
        /// </summary>
        Task<bool> SendAsync(Device device, string payloadJson);
    }
}
