using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// Voice and video calls between friends. A call is made on the hub
    /// (/hubs/miqat): the methods "StartCall", "AnswerCall", "SendCallSignal"
    /// and "EndCall", and the events "callIncoming", "callAccepted",
    /// "callSignal" and "callEnded". The sound and picture go directly
    /// between the two browsers.
    /// <para>
    /// A group chat has a call for up to 8 people: the methods
    /// "StartGroupCall", "JoinGroupCall", "LeaveGroupCall", "DismissGroupCall"
    /// and "SendGroupCallSignal", and the events "groupCallStarted",
    /// "groupCallIncoming", "groupCallJoined", "groupCallLeft",
    /// "groupCallEnded", "groupCallDismissed" and "groupCallSignal".
    /// </para>
    /// </summary>
    public class CallController : BaseApiController
    {
        private readonly ICallService _calls;
        private readonly IGroupCallService _groupCalls;
        private readonly ICurrentUserService _currentUser;

        public CallController(ICallService calls, IGroupCallService groupCalls, ICurrentUserService currentUser)
        {
            _calls = calls;
            _groupCalls = groupCalls;
            _currentUser = currentUser;
        }

        /// <summary>
        /// The group calls going on right now in my group chats, and who is in
        /// each: what the app shows as "call in progress" with a way to join.
        /// </summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActive() =>
            Ok(await _groupCalls.GetActiveAsync(_currentUser.RequireUserId()));

        /// <summary>
        /// What the browser needs before a call: the servers that help the two
        /// devices reach each other (for RTCPeerConnection) and how long to ring.
        /// </summary>
        [HttpGet("setup")]
        public async Task<IActionResult> GetSetup(CancellationToken cancellationToken) =>
            Ok(await _calls.GetSetupAsync(_currentUser.RequireUserId(), cancellationToken));
    }
}
