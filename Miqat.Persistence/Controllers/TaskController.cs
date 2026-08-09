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

        [HttpGet]
        public async Task<IActionResult> GetMyTasks()
        {
            var tasks = await _taskService.GetTasksByUserId(GetCurrentUserId());
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

        [HttpGet("group/{groupId}")]
        public async Task<IActionResult> GetTasksByGroup(Guid groupId)
        {
            var tasks = await _taskService.GetTasksByGroup(groupId);
            return Ok(tasks);
        }

        [HttpGet("{taskId}/comments")]
        public async Task<IActionResult> GetComments(Guid taskId)
        {
            var comments = await _commentService.GetForTaskAsync(taskId);
            return Ok(comments);
        }

        [HttpPost("{taskId}/comments")]
        public async Task<IActionResult> AddComment(Guid taskId, [FromBody] CreateCommentDto dto)
        {
            var comment = await _commentService.AddAsync(taskId, dto.Content, dto.MentionedUserIds);
            return CreatedAtAction(nameof(GetComments), new { taskId }, comment);
        }

        /// <summary>Feeds the @-picker in the comment composer.</summary>
        [HttpGet("{taskId}/mentionable")]
        public async Task<IActionResult> GetMentionable(Guid taskId)
        {
            var people = await _commentService.GetMentionableAsync(taskId);
            return Ok(people);
        }

        [HttpDelete("comments/{commentId}")]
        public async Task<IActionResult> DeleteComment(Guid commentId)
        {
            var result = await _commentService.DeleteAsync(commentId);
            if (!result) return NotFound(new { message = "Comment not found." });
            return NoContent();
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var task = await _taskService.GetTaskById(id);
            if (task == null) return NotFound(new { message = "Task not found." });
            return Ok(task);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaskDto dto)
        {
            dto.UserId = GetCurrentUserId();
            var created = await _taskService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById),
                new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] TaskDto dto)
        {
            var result = await _taskService.UpdateAsync(id, dto);
            if (!result) return NotFound(new { message = "Task not found." });
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _taskService.DeleteAsync(id);
            if (!result) return NotFound(new { message = "Task not found." });
            return NoContent();
        }

        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllTasks()
        {
            var tasks = await _taskService.GetAllTasks();
            return Ok(tasks);
        }
    }
}
