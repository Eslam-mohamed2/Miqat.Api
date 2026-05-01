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

        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UserDto dto)
        {
            var result = await _userService.UpdateAsync(GetCurrentUserId(), dto);
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
    }
}
