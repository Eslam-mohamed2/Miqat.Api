using Microsoft.AspNetCore.Authorization;
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
            var mentions = await _mentionService.GetMentionsAsync(GetCurrentUserId());
            return Ok(new { mentions });
        }

        [HttpGet("unread")]
        public async Task<IActionResult> GetUnreadMentions()
        {
            var unreadMentions = await _mentionService.GetUnreadMentionsAsync(GetCurrentUserId());
            return Ok(new { unreadMentions });
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadMentionsCount()
        {
            var count = await _mentionService.GetUnreadMentionsCountAsync(GetCurrentUserId());
            return Ok(new { unreadCount = count });
        }

        [HttpPut("{mentionId:guid}/read")]
        public async Task<IActionResult> MarkMentionAsRead(Guid mentionId)
        {
            var result = await _mentionService.MarkMentionAsReadAsync(mentionId, GetCurrentUserId());
            if (!result)
                return NotFound(new { message = "Mention not found or not authorized." });

            return Ok(new { message = "Mention marked as read." });
        }

        /// <summary>Marks every unread mention as read in one call.</summary>
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllMentionsAsRead()
        {
            var updated = await _mentionService.MarkAllMentionsAsReadAsync(GetCurrentUserId());
            return Ok(new { message = "All mentions marked as read.", updated });
        }

        [HttpPost("parse")]
        public async Task<IActionResult> ParseMentions([FromBody] ParseMentionsRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return BadRequest(new { message = "Text is required." });

            if (request.Text.Length > ParseMentionsRequest.MaxTextLength)
                return BadRequest(new { message = $"Text cannot exceed {ParseMentionsRequest.MaxTextLength} characters." });

            var mentionedUserIds = await _mentionService.ParseMentionsFromTextAsync(request.Text);
            return Ok(new { mentionedUserIds });
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateMentions([FromBody] CreateMentionsRequest request)
        {
            if (request.MentionedUserIds == null || !request.MentionedUserIds.Any())
                return BadRequest(new { message = "At least one mentioned user ID is required." });

            if (request.EntityId == Guid.Empty)
                return BadRequest(new { message = "EntityId is required." });

            if (!Enum.IsDefined(request.EntityType))
                return BadRequest(new { message = "EntityType must be Project, Task or Comment." });

            var createdMentions = await _mentionService.CreateMentionsAsync(
                GetCurrentUserId(),
                request.MentionedUserIds,
                request.EntityType,
                request.EntityId
            );

            return Ok(new { message = "Mentions created successfully.", mentions = createdMentions });
        }
    }

    public class ParseMentionsRequest
    {
        public const int MaxTextLength = 5000;

        public string Text { get; set; } = string.Empty;
    }

    public class CreateMentionsRequest
    {
        public IEnumerable<Guid> MentionedUserIds { get; set; } = new List<Guid>();

        /// <summary>Project, Task or Comment (name or number).</summary>
        public EntityType EntityType { get; set; }

        public Guid EntityId { get; set; }
    }
}
