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
    /// </summary>
    public class CallController : BaseApiController
    {
        private readonly ICallService _calls;
        private readonly ICurrentUserService _currentUser;

        public CallController(ICallService calls, ICurrentUserService currentUser)
        {
            _calls = calls;
            _currentUser = currentUser;
        }

        /// <summary>
        /// What the browser needs before a call: the servers that help the two
        /// devices reach each other (for RTCPeerConnection) and how long to ring.
        /// </summary>
        [HttpGet("setup")]
        public IActionResult GetSetup() => Ok(_calls.GetSetup(_currentUser.RequireUserId()));
    }
}
