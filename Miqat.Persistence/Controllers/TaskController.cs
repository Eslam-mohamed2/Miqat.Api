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

        public TaskController(ITaskService taskService)
        {
            _taskService = taskService;
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
