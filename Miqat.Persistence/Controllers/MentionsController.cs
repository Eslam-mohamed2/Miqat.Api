using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Domain.Enumerations;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MentionsController : ControllerBase
    {
        private readonly IMentionService _mentionService;

        public MentionsController(IMentionService mentionService)
        {
            _mentionService = mentionService;
        }

        private Guid GetCurrentUserId() =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet]
        public async Task<IActionResult> GetMentions()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var mentions = await _mentionService.GetMentionsAsync(currentUserId);
                return Ok(new { mentions });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet("unread")]
        public async Task<IActionResult> GetUnreadMentions()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var unreadMentions = await _mentionService.GetUnreadMentionsAsync(currentUserId);
                return Ok(new { unreadMentions });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadMentionsCount()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var count = await _mentionService.GetUnreadMentionsCountAsync(currentUserId);
                return Ok(new { unreadCount = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpPut("{mentionId}/read")]
        public async Task<IActionResult> MarkMentionAsRead(Guid mentionId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                var result = await _mentionService.MarkMentionAsReadAsync(mentionId, currentUserId);
                if (!result)
                    return NotFound(new { message = "Mention not found or not authorized." });

                return Ok(new { message = "Mention marked as read." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpPost("parse")]
        public async Task<IActionResult> ParseMentions([FromBody] ParseMentionsRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Text))
                    return BadRequest(new { message = "Text is required." });

                var mentionedUserIds = await _mentionService.ParseMentionsFromTextAsync(request.Text);
                return Ok(new { mentionedUserIds });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateMentions([FromBody] CreateMentionsRequest request)
        {
            try
            {
                var currentUserId = GetCurrentUserId();

                if (request.MentionedUserIds == null || !request.MentionedUserIds.Any())
                    return BadRequest(new { message = "At least one mentioned user ID is required." });

                if (request.EntityId == Guid.Empty)
                    return BadRequest(new { message = "EntityId is required." });

                var createdMentions = await _mentionService.CreateMentionsAsync(
                    currentUserId,
                    request.MentionedUserIds,
                    request.EntityType,
                    request.EntityId
                );

                return Ok(new { message = "Mentions created successfully.", mentions = createdMentions });
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
    }

    public class ParseMentionsRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class CreateMentionsRequest
    {
        public IEnumerable<Guid> MentionedUserIds { get; set; } = new List<Guid>();
        public EntityType EntityType { get; set; }
        public Guid EntityId { get; set; }
    }
}
