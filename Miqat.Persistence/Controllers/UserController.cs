using Microsoft.AspNetCore.Authorization;
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
        private readonly IUserOverviewService _overview;
        private readonly IBlobStorageService _blobStorageService;

        public UserController(
            IUserService userService,
            IUserOverviewService overview,
            IBlobStorageService blobStorageService)
        {
            _userService = userService;
            _overview = overview;
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

        /// <summary>
        /// Everything the dashboard and the top bar count, in one call: tasks by
        /// status, overdue / due today / due this week, projects, friends, pending
        /// friend requests, unread notifications and mentions.
        /// </summary>
        [HttpGet("me/summary")]
        public async Task<IActionResult> GetMySummary()
        {
            return Ok(await _overview.GetSummaryAsync(GetCurrentUserId()));
        }

        /// <summary>Notification settings (Settings > Notifications).</summary>
        [HttpGet("me/preferences")]
        public async Task<IActionResult> GetMyPreferences()
        {
            var preferences = await _userService.GetPreferencesAsync(GetCurrentUserId());
            if (preferences == null) return NotFound(new { message = "User not found." });
            return Ok(preferences);
        }

        /// <summary>Saves the settings that are sent; omitted ones keep their value.</summary>
        [HttpPut("me/preferences")]
        public async Task<IActionResult> UpdateMyPreferences([FromBody] UserPreferencesDto dto)
        {
            var preferences = await _userService.UpdatePreferencesAsync(GetCurrentUserId(), dto);
            if (preferences == null) return NotFound(new { message = "User not found." });
            return Ok(preferences);
        }

        /// <summary>
        /// The people the caller can pick as an assignee or project member: their
        /// friends and everyone on their projects. (GET /api/User is admin-only.)
        /// </summary>
        [HttpGet("contacts")]
        public async Task<IActionResult> GetContacts()
        {
            return Ok(await _overview.GetContactsAsync(GetCurrentUserId()));
        }

        /// <summary>
        /// A person as seen by the caller: their public details, the friendship
        /// between the two, friends in common and the projects they share.
        /// </summary>
        [HttpGet("{id:guid}/profile")]
        public async Task<IActionResult> GetProfile(Guid id)
        {
            var profile = await _overview.GetProfileAsync(GetCurrentUserId(), id, User.IsInRole("Admin"));
            if (profile == null) return NotFound(new { message = "User not found." });
            return Ok(profile);
        }

        /// <summary>
        /// Someone's profile. Phone number and date of birth are only returned to
        /// the user themselves and to admins.
        /// </summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var canSeePrivateFields = id == GetCurrentUserId() || User.IsInRole("Admin");
            var user = await _userService.GetUserById(id, canSeePrivateFields);
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

        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UserDto dto)
        {
            var result = await _userService.UpdateAsync(id, dto);
            if (!result) return NotFound(new { message = "User not found." });
            return NoContent();
        }

        /// <summary>
        /// Admin: activate / deactivate an account, or change its role.
        /// Body: { "isActive": true|false, "role": "Admin"|"User" } — either field may be omitted.
        /// </summary>
        [HttpPut("{id:guid}/access")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetAccess(Guid id, [FromBody] UserAccessDto dto)
        {
            if (dto.IsActive == null && string.IsNullOrWhiteSpace(dto.Role))
                return BadRequest(new { message = "Send isActive, role, or both." });

            var result = await _userService.SetAccessAsync(id, dto, GetCurrentUserId());
            if (!result) return NotFound(new { message = "User not found." });
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _userService.DeleteAsync(id, GetCurrentUserId());
            if (!result) return NotFound(new { message = "User not found." });
            return NoContent();
        }

        [HttpPost("upload-profile-image")]
        [RequestSizeLimit(6 * 1024 * 1024)] // 5MB image + multipart overhead
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
                var imageUrl = await _blobStorageService.UploadImageAsync(file);

                // Only the picture is written. This used to read the whole user
                // back as a DTO and push it through the admin update.
                var saved = await _userService.SetProfilePictureAsync(GetCurrentUserId(), imageUrl);
                if (!saved) return NotFound(new { message = "User not found." });

                return Ok(new { profileImageUrl = imageUrl });
            }
            catch (ArgumentException ex)
            {
                // ex.Message would append "(Parameter 'file')"
                return BadRequest(new { message = ex.Message.Split(" (Parameter")[0] });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
            }
        }

        /// <summary>Removes the caller's profile picture.</summary>
        [HttpDelete("profile-image")]
        public async Task<IActionResult> RemoveProfileImage()
        {
            var saved = await _userService.SetProfilePictureAsync(GetCurrentUserId(), null);
            if (!saved) return NotFound(new { message = "User not found." });
            return NoContent();
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers([FromQuery] string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "Search query is required" });

            if (query.Length > 100)
                return BadRequest(new { message = "Search query cannot exceed 100 characters." });

            var users = await _userService.SearchAsync(query, GetCurrentUserId());
            return Ok(users);
        }
    }
}
