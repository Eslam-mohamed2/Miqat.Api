using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface ITaskService
    {
        Task<IEnumerable<TaskDto>> GetAllTasks();
        Task<IEnumerable<TaskDto>> GetTasksByUserId(Guid userId);
        Task<IEnumerable<TaskDto>> GetTasksByUserIdPaged(Guid userId, int pageIndex, int pageSize);
        Task<IEnumerable<TaskDto>> GetTasksDueSoon(Guid userId, int withinDays = 3);
        Task<IEnumerable<TaskDto>> GetTasksByGroup(Guid groupId);
        Task<TaskDto?> GetTaskById(Guid id);
        Task<TaskDto> CreateAsync(TaskDto dto);
        Task<bool> UpdateAsync(Guid id, TaskDto dto);
        Task<bool> DeleteAsync(Guid id);
    }
}
