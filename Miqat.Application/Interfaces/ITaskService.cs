using Miqat.Application.Common;
using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    public interface ITaskService
    {
        Task<IEnumerable<TaskDto>> GetAllTasks();

        /// <summary>Tasks the user created or is assigned to, optionally filtered.</summary>
        Task<IEnumerable<TaskDto>> GetTasksByUserId(Guid userId, TaskFilter? filter = null);

        Task<PagedResult<TaskDto>> GetTasksByUserIdPaged(Guid userId, int pageIndex, int pageSize);
        Task<IEnumerable<TaskDto>> GetTasksDueSoon(Guid userId, int withinDays = 3);
        Task<IEnumerable<TaskDto>> GetTasksByGroup(Guid groupId);
        Task<TaskDto?> GetTaskById(Guid id);
        Task<TaskDto> CreateAsync(TaskDto dto);
        Task<bool> UpdateAsync(Guid id, TaskDto dto);
        Task<bool> DeleteAsync(Guid id);
    }
}
