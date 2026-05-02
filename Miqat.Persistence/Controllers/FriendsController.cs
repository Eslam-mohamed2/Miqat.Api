using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
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

        [HttpPost("send-request/{receiverId}")]
        public async Task<IActionResult> SendFriendRequest(Guid receiverId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var friendship = await _friendService.SendFriendRequestAsync(currentUserId, receiverId);
                return Ok(new { message = "Friend request sent successfully.", friendship });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpPut("accept/{friendshipId}")]
        public async Task<IActionResult> AcceptFriendRequest(Guid friendshipId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var result = await _friendService.AcceptFriendRequestAsync(friendshipId, currentUserId);
                if (!result)
                    return NotFound(new { message = "Friendship request not found." });

                return Ok(new { message = "Friend request accepted successfully." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpPut("reject/{friendshipId}")]
        public async Task<IActionResult> RejectFriendRequest(Guid friendshipId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var result = await _friendService.RejectFriendRequestAsync(friendshipId, currentUserId);
                if (!result)
                    return NotFound(new { message = "Friendship request not found." });

                return Ok(new { message = "Friend request rejected successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpPut("block/{userToBlockId}")]
        public async Task<IActionResult> BlockUser(Guid userToBlockId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                await _friendService.BlockUserAsync(currentUserId, userToBlockId);
                return Ok(new { message = "User blocked successfully." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpPut("unblock/{blockedUserId}")]
        public async Task<IActionResult> UnblockUser(Guid blockedUserId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var result = await _friendService.UnblockUserAsync(currentUserId, blockedUserId);
                if (!result)
                    return NotFound(new { message = "No blocked user found." });

                return Ok(new { message = "User unblocked successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetFriends()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var friends = await _friendService.GetFriendsAsync(currentUserId);
                return Ok(new { friends });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingRequests()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var pendingRequests = await _friendService.GetPendingRequestsAsync(currentUserId);
                return Ok(new { pendingRequests });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet("sent")]
        public async Task<IActionResult> GetSentRequests()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var sentRequests = await _friendService.GetSentRequestsAsync(currentUserId);
                return Ok(new { sentRequests });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet("{friendshipId}")]
        public async Task<IActionResult> GetFriendship(Guid friendshipId)
        {
            try
            {
                var friendship = await _friendService.GetFriendshipAsync(friendshipId);
                if (friendship == null)
                    return NotFound(new { message = "Friendship not found." });

                return Ok(friendship);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpDelete("{friendshipId}")]
        public async Task<IActionResult> Unfriend(Guid friendshipId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                await _friendService.DeleteAsync(friendshipId, currentUserId);
                return Ok(new { message = "Unfriended successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet("status/{userId}")]
        public async Task<IActionResult> GetFriendshipStatus(Guid userId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var status = await _friendService.GetStatusAsync(currentUserId, userId);
                return Ok(new { status });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet("suggestions")]
        public async Task<IActionResult> GetSuggestions()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var suggestions = await _friendService.GetSuggestionsAsync(currentUserId);
                return Ok(suggestions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }
    }
}
