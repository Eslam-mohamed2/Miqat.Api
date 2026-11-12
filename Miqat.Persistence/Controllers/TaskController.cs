using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Enumerations;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TaskController : ControllerBase
    {
        private readonly ITaskService _taskService;
        private readonly ICommentService _commentService;

        public TaskController(ITaskService taskService, ICommentService commentService)
        {
            _taskService = taskService;
            _commentService = commentService;
        }

        private Guid GetCurrentUserId() =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>
        /// Tasks the caller created or is assigned to. All filters are optional:
        /// status, priority, groupId (one project), dueFrom / dueTo (a date range,
        /// for example the month a calendar is showing).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyTasks(
            [FromQuery] string? status = null,
            [FromQuery] string? priority = null,
            [FromQuery] Guid? groupId = null,
            [FromQuery] DateTime? dueFrom = null,
            [FromQuery] DateTime? dueTo = null)
        {
            var filter = new TaskFilter { GroupId = groupId };

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!EnumText.TryParseTaskStatus(status, out var parsedStatus))
                    return BadRequest(new { message = "Status must be one of: Pending, In_progress, Completed, Cancelled, On_hold." });
                filter.Status = parsedStatus;
            }

            if (!string.IsNullOrWhiteSpace(priority))
            {
                if (!EnumText.TryParseDefined<Priority>(priority, out var parsedPriority))
                    return BadRequest(new { message = "Priority must be Low, Medium, High or Critical." });
                filter.Priority = parsedPriority;
            }

            // Query-string dates arrive without a time zone; the database column
            // only accepts UTC, so they are read as UTC like dates in a body are.
            filter.DueFrom = UtcDateTimeConverter.Normalize(dueFrom);
            filter.DueTo = UtcDateTimeConverter.Normalize(dueTo);

            if (filter.DueFrom.HasValue && filter.DueTo.HasValue && filter.DueFrom > filter.DueTo)
                return BadRequest(new { message = "dueFrom cannot be after dueTo." });

            var tasks = await _taskService.GetTasksByUserId(GetCurrentUserId(), filter);
            return Ok(tasks);
        }

        [HttpGet("paged")]
        public async Task<IActionResult> GetMyTasksPaged(
            [FromQuery] int pageIndex = 0,
            [FromQuery] int pageSize = 10)
        {
            var tasks = await _taskService.GetTasksByUserIdPaged(
                GetCurrentUserId(), pageIndex, pageSize);
            return Ok(tasks);
        }

        [HttpGet("due-soon")]
        public async Task<IActionResult> GetTasksDueSoon(
            [FromQuery] int withinDays = 3)
        {
            var tasks = await _taskService
                .GetTasksDueSoon(GetCurrentUserId(), withinDays);
            return Ok(tasks);
        }

        [HttpGet("group/{groupId:guid}")]
        public async Task<IActionResult> GetTasksByGroup(Guid groupId)
        {
            var tasks = await _taskService.GetTasksByGroup(groupId);
            return Ok(tasks);
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllTasks()
        {
            var tasks = await _taskService.GetAllTasks();
            return Ok(tasks);
        }

        // ── Comments ──────────────────────────────────────────────────────────

        [HttpGet("{taskId:guid}/comments")]
        public async Task<IActionResult> GetComments(Guid taskId)
        {
            var comments = await _commentService.GetForTaskAsync(taskId);
            return Ok(comments);
        }

        [HttpPost("{taskId:guid}/comments")]
        public async Task<IActionResult> AddComment(Guid taskId, [FromBody] CreateCommentDto dto)
        {
            var comment = await _commentService.AddAsync(taskId, dto.Content, dto.MentionedUserIds);
            return CreatedAtAction(nameof(GetComments), new { taskId }, comment);
        }

        /// <summary>Feeds the @-picker in the comment composer.</summary>
        [HttpGet("{taskId:guid}/mentionable")]
        public async Task<IActionResult> GetMentionable(Guid taskId)
        {
            var people = await _commentService.GetMentionableAsync(taskId);
            return Ok(people);
        }

        /// <summary>Edit a comment. Author only.</summary>
        [HttpPut("comments/{commentId:guid}")]
        public async Task<IActionResult> UpdateComment(Guid commentId, [FromBody] UpdateCommentDto dto)
        {
            var comment = await _commentService.UpdateAsync(commentId, dto.Content);
            if (comment == null) return NotFound(new { message = "Comment not found." });
            return Ok(comment);
        }

        [HttpDelete("comments/{commentId:guid}")]
        public async Task<IActionResult> DeleteComment(Guid commentId)
        {
            var result = await _commentService.DeleteAsync(commentId);
            if (!result) return NotFound(new { message = "Comment not found." });
            return NoContent();
        }

        // ── Single task ───────────────────────────────────────────────────────

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var task = await _taskService.GetTaskById(id);
            if (task == null) return NotFound(new { message = "Task not found." });
            return Ok(task);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaskDto dto)
        {
            var created = await _taskService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById),
                new { id = created.Id }, created);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] TaskDto dto)
        {
            var result = await _taskService.UpdateAsync(id, dto);
            if (!result) return NotFound(new { message = "Task not found." });
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _taskService.DeleteAsync(id);
            if (!result) return NotFound(new { message = "Task not found." });
            return NoContent();
        }
    }
}
