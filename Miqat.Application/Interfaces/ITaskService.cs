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
        Task<TaskDto> GetTaskById(Guid id);
        Task<TaskDto> CreateAsync(TaskDto task);
        Task<bool> UpdateAsync(Guid id, TaskDto task);
        Task<bool> DeleteAsync(Guid id);
    }
}
