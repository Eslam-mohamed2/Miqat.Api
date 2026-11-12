using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
    // Business errors are thrown as ApiException by the service and turned into
    // the right status code + { message } by GlobalExceptionMiddleware.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FriendsController : ControllerBase
    {
        private readonly IFriendService _friendService;

        public FriendsController(IFriendService friendService)
        {
            _friendService = friendService;
        }

        private Guid GetCurrentUserId() =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpPost("send-request/{receiverId:guid}")]
        public async Task<IActionResult> SendFriendRequest(Guid receiverId)
        {
            var friendship = await _friendService.SendFriendRequestAsync(GetCurrentUserId(), receiverId);
            return Ok(new { message = "Friend request sent successfully.", friendship });
        }

        [HttpPut("accept/{friendshipId:guid}")]
        public async Task<IActionResult> AcceptFriendRequest(Guid friendshipId)
        {
            var result = await _friendService.AcceptFriendRequestAsync(friendshipId, GetCurrentUserId());
            if (!result)
                return NotFound(new { message = "Friendship request not found." });

            return Ok(new { message = "Friend request accepted successfully." });
        }

        [HttpPut("reject/{friendshipId:guid}")]
        public async Task<IActionResult> RejectFriendRequest(Guid friendshipId)
        {
            var result = await _friendService.RejectFriendRequestAsync(friendshipId, GetCurrentUserId());
            if (!result)
                return NotFound(new { message = "Friendship request not found." });

            return Ok(new { message = "Friend request rejected successfully." });
        }

        [HttpPut("block/{userToBlockId:guid}")]
        public async Task<IActionResult> BlockUser(Guid userToBlockId)
        {
            await _friendService.BlockUserAsync(GetCurrentUserId(), userToBlockId);
            return Ok(new { message = "User blocked successfully." });
        }

        [HttpPut("unblock/{blockedUserId:guid}")]
        public async Task<IActionResult> UnblockUser(Guid blockedUserId)
        {
            var result = await _friendService.UnblockUserAsync(GetCurrentUserId(), blockedUserId);
            if (!result)
                return NotFound(new { message = "No blocked user found." });

            return Ok(new { message = "User unblocked successfully." });
        }

        /// <summary>The people the caller has blocked, so the client can offer "unblock".</summary>
        [HttpGet("blocked")]
        public async Task<IActionResult> GetBlockedUsers()
        {
            var blockedUsers = await _friendService.GetBlockedUsersAsync(GetCurrentUserId());
            return Ok(new { blockedUsers });
        }

        [HttpGet]
        public async Task<IActionResult> GetFriends()
        {
            var friends = await _friendService.GetFriendsAsync(GetCurrentUserId());
            return Ok(new { friends });
        }

        /// <summary>Same data as GET /api/Friends, returned as a plain array.</summary>
        [HttpGet("my-friends")]
        public async Task<IActionResult> GetMyFriends()
        {
            var friends = await _friendService.GetFriendsAsync(GetCurrentUserId());
            return Ok(friends);
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingRequests()
        {
            var pendingRequests = await _friendService.GetPendingRequestsAsync(GetCurrentUserId());
            return Ok(new { pendingRequests });
        }

        [HttpGet("sent")]
        public async Task<IActionResult> GetSentRequests()
        {
            var sentRequests = await _friendService.GetSentRequestsAsync(GetCurrentUserId());
            return Ok(new { sentRequests });
        }

        [HttpGet("suggestions")]
        public async Task<IActionResult> GetSuggestions()
        {
            var suggestions = await _friendService.GetSuggestionsAsync(GetCurrentUserId());
            return Ok(suggestions);
        }

        /// <summary>Returns { status, id }. status: None | Sent | Pending | Friends | Blocked.</summary>
        [HttpGet("status/{userId:guid}")]
        public async Task<IActionResult> GetFriendshipStatus(Guid userId)
        {
            var result = await _friendService.GetStatusAsync(GetCurrentUserId(), userId);
            return Ok(result);
        }

        [HttpGet("{friendshipId:guid}")]
        public async Task<IActionResult> GetFriendship(Guid friendshipId)
        {
            var friendship = await _friendService.GetFriendshipAsync(friendshipId, GetCurrentUserId());
            if (friendship == null)
                return NotFound(new { message = "Friendship not found." });

            return Ok(friendship);
        }

        /// <summary>Unfriend, or cancel / withdraw a pending request.</summary>
        [HttpDelete("{friendshipId:guid}")]
        public async Task<IActionResult> Unfriend(Guid friendshipId)
        {
            await _friendService.DeleteAsync(friendshipId, GetCurrentUserId());
            return Ok(new { message = "Unfriended successfully" });
        }
    }
}
