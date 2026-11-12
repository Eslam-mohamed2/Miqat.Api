using Miqat.Application.Modules;
using Miqat.Application.Services;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// Voice and video calls between two friends. The sound and picture go
    /// straight from browser to browser (WebRTC); this only rings the friend,
    /// passes the set-up messages between the two, and records the call in
    /// their chat. Used by the live connection, which knows who is calling.
    /// </summary>
    public interface ICallService
    {
        /// <summary>What a browser needs before it can call: the helper servers and how long to ring.</summary>
        CallSetupDto GetSetup(Guid userId);

        /// <summary>
        /// Starts ringing the friend of a chat. 409 when the friend is offline
        /// or busy (they get a missed call) or the caller is already on a call.
        /// </summary>
        Task<CallInvite> StartAsync(Guid callerId, string connectionId, Guid conversationId, bool video);

        /// <summary>The friend picks up, in the tab with this connection.</summary>
        CallSession Answer(Guid callId, Guid userId, string connectionId);

        /// <summary>Where a set-up message of an answered call goes: the other tab. Null for anyone else.</summary>
        string? PeerConnectionOf(Guid callId, string connectionId);

        /// <summary>Hang up, decline or give up. Null when the call was already over or is not the user's.</summary>
        Task<CallEnded?> EndAsync(Guid callId, Guid userId);

        /// <summary>A tab closed: the call it was part of ends with it.</summary>
        Task<CallEnded?> EndForConnectionAsync(string connectionId);
    }
}
