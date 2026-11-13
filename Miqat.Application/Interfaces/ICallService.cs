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
        Task<CallSetupDto> GetSetupAsync(Guid userId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Voice and video calls inside a group chat, for up to
    /// <see cref="GroupCall.MaxPeople"/> people who are connected browser to
    /// browser, every pair directly. This keeps who is in the call, rings the
    /// others, says where a set-up message goes, and records the call in the
    /// chat when the last person leaves. Everyone in the chat is told live what
    /// happens ("groupCallStarted", "groupCallIncoming", "groupCallJoined",
    /// "groupCallLeft", "groupCallEnded").
    /// </summary>
    public interface IGroupCallService
    {
        /// <summary>
        /// Starts the call of a group chat and rings its other members, or
        /// joins the call that is already going on there. 409 when the person
        /// is on another call or the call is full.
        /// </summary>
        Task<GroupCallDto> StartAsync(Guid userId, string connectionId, Guid conversationId, bool video);

        /// <summary>Joins a call that is going on, in the tab with this connection. 409 when it is over or full, or the person is on another call.</summary>
        Task<GroupCallDto> JoinAsync(Guid userId, string connectionId, Guid callId);

        /// <summary>Leaves a call; it goes on for the others, and ends when nobody is left.</summary>
        Task LeaveAsync(Guid userId, Guid callId);

        /// <summary>A tab closed: whoever was in a group call with it leaves that call.</summary>
        Task LeaveForConnectionAsync(string connectionId);

        /// <summary>A person left a group chat or was removed from it: they leave its call too.</summary>
        Task LeaveForMemberAsync(Guid conversationId, Guid userId);

        /// <summary>"Not now": the ringing stops in every tab of this person ("groupCallDismissed"). The call goes on.</summary>
        Task DismissAsync(Guid userId, Guid callId);

        /// <summary>
        /// Where a set-up message from one person in a call to another goes:
        /// the tab the other is in the call with. Null unless both are in the
        /// call and the message comes from the sender's own tab of the call.
        /// </summary>
        string? SignalTargetOf(Guid callId, Guid fromUserId, string fromConnectionId, Guid toUserId);

        /// <summary>The calls going on in the group chats of a person.</summary>
        Task<List<GroupCallDto>> GetActiveAsync(Guid userId);
    }
}
