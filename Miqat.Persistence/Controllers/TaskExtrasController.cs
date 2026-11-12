using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// The parts of a task beyond its own fields: its checklist and the tasks
    /// it waits for. Listed with the other task endpoints.
    /// </summary>
    [Route("api/Task")]
    [Tags("Task")]
    public class TaskExtrasController : BaseApiController
    {
        private readonly ISubtaskService _subtasks;
        private readonly ITaskDependencyService _dependencies;

        public TaskExtrasController(ISubtaskService subtasks, ITaskDependencyService dependencies)
        {
            _subtasks = subtasks;
            _dependencies = dependencies;
        }

        // ── Checklist ─────────────────────────────────────────────────────────

        [HttpGet("{taskId:guid}/subtasks")]
        public async Task<IActionResult> GetSubtasks(Guid taskId)
            => Ok(await _subtasks.GetForTaskAsync(taskId));

        /// <summary>Adds a checklist line - at the end, or at "position".</summary>
        [HttpPost("{taskId:guid}/subtasks")]
        public async Task<IActionResult> AddSubtask(Guid taskId, [FromBody] SaveSubtaskDto dto)
        {
            var subtask = await _subtasks.AddAsync(taskId, dto);
            return CreatedAtAction(nameof(GetSubtasks), new { taskId }, subtask);
        }

        /// <summary>Renames, ticks or moves a checklist line. Values left out stay as they are.</summary>
        [HttpPut("subtasks/{subtaskId:guid}")]
        public async Task<IActionResult> UpdateSubtask(Guid subtaskId, [FromBody] SaveSubtaskDto dto)
            => OkOrNotFound(await _subtasks.UpdateAsync(subtaskId, dto), "Checklist line");

        [HttpDelete("subtasks/{subtaskId:guid}")]
        public async Task<IActionResult> DeleteSubtask(Guid subtaskId)
            => NoContentOrNotFound(await _subtasks.DeleteAsync(subtaskId), "Checklist line");

        // ── Dependencies ──────────────────────────────────────────────────────

        /// <summary>What this task waits for ("blockedBy") and what waits for it ("blocks").</summary>
        [HttpGet("{taskId:guid}/dependencies")]
        public async Task<IActionResult> GetDependencies(Guid taskId)
            => Ok(await _dependencies.GetAsync(taskId));

        /// <summary>"This task cannot start until that one is finished."</summary>
        [HttpPost("{taskId:guid}/dependencies")]
        public async Task<IActionResult> AddDependency(Guid taskId, [FromBody] AddDependencyDto dto)
            => Ok(await _dependencies.AddAsync(taskId, dto.DependsOnTaskId));

        [HttpDelete("{taskId:guid}/dependencies/{dependsOnTaskId:guid}")]
        public async Task<IActionResult> RemoveDependency(Guid taskId, Guid dependsOnTaskId)
            => NoContentOrNotFound(await _dependencies.RemoveAsync(taskId, dependsOnTaskId), "Dependency");
    }
}
