using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    /// <summary>The checklist of a task.</summary>
    public interface ISubtaskService
    {
        Task<List<SubtaskDto>> GetForTaskAsync(Guid taskId);
        Task<SubtaskDto> AddAsync(Guid taskId, SaveSubtaskDto dto);
        Task<SubtaskDto?> UpdateAsync(Guid subtaskId, SaveSubtaskDto dto);
        Task<bool> DeleteAsync(Guid subtaskId);
    }

    /// <summary>"This task waits for that one."</summary>
    public interface ITaskDependencyService
    {
        Task<TaskDependenciesDto> GetAsync(Guid taskId);

        /// <summary>Rejects a link that would make tasks wait for each other in a circle.</summary>
        Task<TaskDependenciesDto> AddAsync(Guid taskId, Guid dependsOnTaskId);

        Task<bool> RemoveAsync(Guid taskId, Guid dependsOnTaskId);
    }
}
