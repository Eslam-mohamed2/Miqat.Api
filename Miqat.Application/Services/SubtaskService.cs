using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// A checklist belongs to its task: whoever may work on the task may read
    /// and change its checklist, and nobody else can.
    /// </remarks>
    public class SubtaskService : ISubtaskService
    {
        private const int MaxPerTask = 100;
        private const int MaxTitleLength = 200; // Subtasks.Title column size

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAccessPolicy _access;
        private readonly TaskBroadcaster _broadcaster;

        public SubtaskService(IUnitOfWork unitOfWork, IAccessPolicy access, TaskBroadcaster broadcaster)
        {
            _unitOfWork = unitOfWork;
            _access = access;
            _broadcaster = broadcaster;
        }

        private IGenericRepository<Subtask> Subtasks => _unitOfWork.Repository<Subtask>();

        public async Task<List<SubtaskDto>> GetForTaskAsync(Guid taskId)
        {
            await _access.RequireItemAsync(CalendarItemKind.Task, taskId);
            return (await LoadChecklistAsync(taskId)).Select(ToDto).ToList();
        }

        public async Task<SubtaskDto> AddAsync(Guid taskId, SaveSubtaskDto dto)
        {
            await _access.RequireItemAsync(CalendarItemKind.Task, taskId, toChange: true);

            var checklist = await LoadChecklistAsync(taskId);
            if (checklist.Count >= MaxPerTask)
                throw new ApiException($"A task can have up to {MaxPerTask} checklist lines.", 400);

            var subtask = new Subtask(taskId, RequireTitle(dto.Title), checklist.Count);
            if (dto.IsDone == true) subtask.SetDone(true);

            await Subtasks.AddAsync(subtask);
            Place(checklist, subtask, dto.Position ?? checklist.Count);

            await SaveAsync(taskId);
            return ToDto(subtask);
        }

        public async Task<SubtaskDto?> UpdateAsync(Guid subtaskId, SaveSubtaskDto dto)
        {
            var subtask = await Subtasks.GetByIdAsync(subtaskId);
            if (subtask == null) return null;

            await _access.RequireItemAsync(CalendarItemKind.Task, subtask.TaskId, toChange: true);

            if (dto.Title != null) subtask.Title = RequireTitle(dto.Title);
            if (dto.IsDone.HasValue) subtask.SetDone(dto.IsDone.Value);

            if (dto.Position.HasValue)
            {
                var checklist = await LoadChecklistAsync(subtask.TaskId);
                checklist.Remove(subtask);
                Place(checklist, subtask, dto.Position.Value);
            }

            subtask.SetUpdated();
            await SaveAsync(subtask.TaskId);
            return ToDto(subtask);
        }

        public async Task<bool> DeleteAsync(Guid subtaskId)
        {
            var subtask = await Subtasks.GetByIdAsync(subtaskId);
            if (subtask == null) return false;

            await _access.RequireItemAsync(CalendarItemKind.Task, subtask.TaskId, toChange: true);

            var checklist = await LoadChecklistAsync(subtask.TaskId);
            checklist.Remove(subtask);
            Renumber(checklist);

            Subtasks.Delete(subtask);
            await SaveAsync(subtask.TaskId);
            return true;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private async Task<List<Subtask>> LoadChecklistAsync(Guid taskId) =>
            (await Subtasks.FindAsync(s => s.TaskId == taskId))
                .OrderBy(s => s.Position).ThenBy(s => s.CreatedAt)
                .ToList();

        /// <summary>Puts a line at a position of the checklist (clamped to its ends) and renumbers the rest around it.</summary>
        private static void Place(List<Subtask> checklist, Subtask subtask, int position)
        {
            checklist.Insert(Math.Clamp(position, 0, checklist.Count), subtask);
            Renumber(checklist);
        }

        private static void Renumber(List<Subtask> checklist)
        {
            for (var i = 0; i < checklist.Count; i++) checklist[i].Position = i;
        }

        /// <summary>Saves, then refreshes the boards that show the task.</summary>
        private async Task SaveAsync(Guid taskId)
        {
            await _unitOfWork.CompleteAsync();
            await _broadcaster.UpdatedAsync(taskId);
        }

        private static string RequireTitle(string? title)
        {
            var text = title?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new ApiException("A checklist line needs a title.", 400);
            if (text.Length > MaxTitleLength)
                throw new ApiException($"A checklist line cannot exceed {MaxTitleLength} characters.", 400);

            return text;
        }

        private static SubtaskDto ToDto(Subtask subtask) => new()
        {
            Id = subtask.Id,
            TaskId = subtask.TaskId,
            Title = subtask.Title,
            IsDone = subtask.IsDone,
            Position = subtask.Position,
            CompletedAt = subtask.CompletedAt,
            CreatedAt = subtask.CreatedAt
        };
    }
}
