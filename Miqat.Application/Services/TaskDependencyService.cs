using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class TaskDependencyService : ITaskDependencyService
    {
        private const int MaxPrerequisites = 25;

        /// <summary>How many tasks the circle check follows before giving up; far above any real chain.</summary>
        private const int MaxChainLength = 1000;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAccessPolicy _access;
        private readonly TaskBroadcaster _broadcaster;

        public TaskDependencyService(IUnitOfWork unitOfWork, IAccessPolicy access, TaskBroadcaster broadcaster)
        {
            _unitOfWork = unitOfWork;
            _access = access;
            _broadcaster = broadcaster;
        }

        private IGenericRepository<TaskDependency> Links => _unitOfWork.Repository<TaskDependency>();

        public async Task<TaskDependenciesDto> GetAsync(Guid taskId)
        {
            await _access.RequireItemAsync(CalendarItemKind.Task, taskId);

            // Two queries: what this task waits for, and what waits for it.
            var blockedBy = await Links.SelectAsync(
                link => link.TaskId == taskId && !link.DependsOnTask.IsDeleted,
                link => new LinkedTask(
                    link.DependsOnTask.Id, link.DependsOnTask.Title, link.DependsOnTask.Status, link.DependsOnTask.DueDate));

            var blocks = await Links.SelectAsync(
                link => link.DependsOnTaskId == taskId && !link.Task.IsDeleted,
                link => new LinkedTask(link.Task.Id, link.Task.Title, link.Task.Status, link.Task.DueDate));

            return new TaskDependenciesDto
            {
                BlockedBy = blockedBy.Select(ToDto).ToList(),
                Blocks = blocks.Select(ToDto).ToList(),
                IsBlocked = blockedBy.Any(task => !IsFinished(task.Status))
            };
        }

        public async Task<TaskDependenciesDto> AddAsync(Guid taskId, Guid dependsOnTaskId)
        {
            if (taskId == dependsOnTaskId)
                throw new ApiException("A task cannot wait for itself.", 400);

            await _access.RequireItemAsync(CalendarItemKind.Task, taskId, toChange: true);
            await _access.RequireItemAsync(CalendarItemKind.Task, dependsOnTaskId);

            var prerequisites = await Links.SelectAsync(link => link.TaskId == taskId, link => link.DependsOnTaskId);
            if (!prerequisites.Contains(dependsOnTaskId))
            {
                if (prerequisites.Count >= MaxPrerequisites)
                    throw new ApiException($"A task can wait for up to {MaxPrerequisites} other tasks.", 400);

                if (await WaitsForAsync(dependsOnTaskId, taskId))
                    throw new ApiException("These two tasks would be waiting for each other.", 400);

                await Links.AddAsync(new TaskDependency(taskId, dependsOnTaskId));
                await SaveAsync(taskId);
            }

            return await GetAsync(taskId);
        }

        public async Task<bool> RemoveAsync(Guid taskId, Guid dependsOnTaskId)
        {
            await _access.RequireItemAsync(CalendarItemKind.Task, taskId, toChange: true);

            var link = (await Links.FindAsync(
                l => l.TaskId == taskId && l.DependsOnTaskId == dependsOnTaskId)).FirstOrDefault();
            if (link == null) return false;

            Links.Delete(link);
            await SaveAsync(taskId);
            return true;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// True when <paramref name="taskId"/> already waits - directly or
        /// through other tasks - for <paramref name="targetId"/>. Follows the
        /// links one level per query, reading ids only.
        /// </summary>
        private async Task<bool> WaitsForAsync(Guid taskId, Guid targetId)
        {
            var seen = new HashSet<Guid> { taskId };
            var level = new List<Guid> { taskId };

            while (level.Count > 0 && seen.Count < MaxChainLength)
            {
                var current = level;
                var next = await Links.SelectAsync(link => current.Contains(link.TaskId), link => link.DependsOnTaskId);

                if (next.Contains(targetId)) return true;
                level = next.Where(seen.Add).ToList();
            }

            return false;
        }

        /// <summary>Saves, then refreshes the boards that show the task.</summary>
        private async Task SaveAsync(Guid taskId)
        {
            await _unitOfWork.CompleteAsync();
            await _broadcaster.UpdatedAsync(taskId);
        }

        private static bool IsFinished(TaskStatus status) =>
            status is TaskStatus.Completed or TaskStatus.Cancelled;

        private static TaskLinkDto ToDto(LinkedTask task) => new()
        {
            Id = task.Id,
            Title = task.Title,
            Status = task.Status.ToString(),
            DueDate = task.DueDate,
            IsDone = IsFinished(task.Status)
        };

        private sealed record LinkedTask(Guid Id, string Title, TaskStatus Status, DateTime? DueDate);
    }
}
