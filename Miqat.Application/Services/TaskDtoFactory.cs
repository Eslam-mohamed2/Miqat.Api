using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Services
{
    /// <summary>
    /// Builds TaskDtos including the values that are counted from other tables:
    /// comments, checklist progress and whether the task is waiting for another
    /// one. All of them come from a single query for the whole list, however
    /// long it is. Every place that returns tasks goes through here so a task
    /// looks the same wherever it is fetched from.
    /// </summary>
    public class TaskDtoFactory
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly TaskMapper _mapper;

        public TaskDtoFactory(IUnitOfWork unitOfWork, TaskMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<List<TaskDto>> ToDtosAsync(IEnumerable<TaskItem> tasks)
        {
            var dtos = _mapper.MapToDtos(tasks).ToList();
            if (dtos.Count == 0) return dtos;

            var ids = dtos.Select(d => d.Id).ToList();
            var counts = (await _unitOfWork.Repository<TaskItem>().SelectAsync(
                    t => ids.Contains(t.Id),
                    t => new TaskCounts(
                        t.Id,
                        t.Comments.Count(c => !c.IsDeleted),
                        t.Subtasks.Count(),
                        t.Subtasks.Count(s => s.IsDone),
                        t.Prerequisites.Any(p =>
                            !p.DependsOnTask.IsDeleted &&
                            p.DependsOnTask.Status != TaskStatus.Completed &&
                            p.DependsOnTask.Status != TaskStatus.Cancelled))))
                .ToDictionary(c => c.TaskId);

            foreach (var dto in dtos)
            {
                if (!counts.TryGetValue(dto.Id, out var count)) continue;

                dto.CommentCount = count.Comments;
                dto.SubtaskCount = count.Subtasks;
                dto.CompletedSubtaskCount = count.SubtasksDone;
                dto.IsBlocked = count.IsBlocked;
            }

            return dtos;
        }

        public async Task<TaskDto> ToDtoAsync(TaskItem task)
            => (await ToDtosAsync(new[] { task })).First();

        private sealed record TaskCounts(Guid TaskId, int Comments, int Subtasks, int SubtasksDone, bool IsBlocked);
    }
}
