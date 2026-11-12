using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// A hosted relay (TURN) service for calls. Two devices on different
    /// networks often cannot reach each other directly; the relay passes the
    /// sound and picture between them. The service hands out the servers and
    /// short-lived passwords the browsers use.
    /// </summary>
    public interface IRelayAccess
    {
        /// <summary>
        /// The relay servers to give a browser. Empty when no relay is set up
        /// or the service cannot be reached: a call is then tried without one.
        /// </summary>
        Task<IReadOnlyList<IceServerDto>> GetServersAsync(CancellationToken cancellationToken = default);
    }
}
