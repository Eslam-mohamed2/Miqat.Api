using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IBlobStorageService _blobStorageService;

        public UserController(IUserService userService, IBlobStorageService blobStorageService)
        {
            _userService = userService;
            _blobStorageService = blobStorageService;
        }

        private Guid GetCurrentUserId() =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var user = await _userService.GetUserById(GetCurrentUserId());
            if (user == null) return NotFound(new { message = "User not found." });
            return Ok(user);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var user = await _userService.GetUserById(id);
            if (user == null) return NotFound(new { message = "User not found." });
            return Ok(user);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userService.GetAllUsers();
            return Ok(users);
        }

        // Takes UpdateProfileDto, not UserDto. With UserDto the UserValidator
        // demanded an Email the profile form never sends, so every save returned
        // 400 "Email is required" — and had one been sent, the update would have
        // changed the user's login address as a side effect.
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileDto dto)
        {
            var result = await _userService.UpdateProfileAsync(GetCurrentUserId(), dto);
            if (!result) return NotFound(new { message = "User not found." });
            return NoContent();
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UserDto dto)
        {
            var result = await _userService.UpdateAsync(id, dto);
            if (!result) return NotFound(new { message = "User not found." });
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _userService.DeleteAsync(id);
            if (!result) return NotFound(new { message = "User not found." });
            return NoContent();
        }

        [HttpPost("upload-profile-image")]
        public async Task<IActionResult> UploadProfileImage(IFormFile file)
        {
            // Storage being unconfigured is a deployment state, not a server fault,
            // so it answers 503 with something the UI can show instead of a bare 500.
            if (!_blobStorageService.IsConfigured)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Profile image upload is not available: image storage is not configured."
                });
            }

            try
            {
                // Upload to Azure Blob Storage
                var imageUrl = await _blobStorageService.UploadImageAsync(file);

                // Get current user and update profile picture URL
                var userId = GetCurrentUserId();
                var user = await _userService.GetUserById(userId);
                if (user == null) return NotFound(new { message = "User not found." });

                // Update user profile picture
                user.ProfilePictureUrl = imageUrl;
                var updateResult = await _userService.UpdateAsync(userId, user);
                if (!updateResult) return BadRequest(new { message = "Failed to update user profile picture." });

                return Ok(new { profileImageUrl = imageUrl });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers([FromQuery] string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return BadRequest(new { message = "Search query is required" });

                var currentUserId = GetCurrentUserId();
                var users = await _userService.SearchAsync(query, currentUserId);
                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }
    }
}
