using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
#nullable disable


namespace Miqat.Application.Services
{
    public class TaskService : ITaskService
    {
        readonly IUnitOfWork _unitOfWork;
        readonly TaskMapper _mapper;

        public TaskService(IUnitOfWork unitOfWork, TaskMapper taskMapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = taskMapper;
        }
        public async Task<IEnumerable<TaskDto>> GetAllTasks()
        {
            var tasks = await _unitOfWork.Repository<TaskItem>().GetAllAsync();
            return _mapper.MapToDtos(tasks);
        }

        public async Task<TaskDto> GetTaskById(Guid id)
        {
            var taskEnitiy = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(id);
            return taskEnitiy != null ? _mapper.MapToDto(taskEnitiy) : null;
        }
        public async Task<TaskDto> CreateAsync(TaskDto dto)
        {
            var taskEnitiy = new TaskItem(dto.Title, dto.Description, dto.UserId);
            await _unitOfWork.Repository<TaskItem>().AddAsync(taskEnitiy);
            await _unitOfWork.CompleteAsync(); 
            return _mapper.MapToDto(taskEnitiy);
        }
        public async Task<bool> UpdateAsync(Guid id, TaskDto taskDto)
        {
            var taskEntity = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(id);
            if (taskEntity == null) return false;
            taskEntity.Title = taskDto.Title;
            taskEntity.Description = taskDto.Description;
            if (Enum.TryParse<Miqat.Domain.Enumerations.TaskStatus>(taskDto.Status, out var statusEnum))
                taskEntity.Status = statusEnum;
            _unitOfWork.Repository<TaskItem>().Update(taskEntity);
            return await _unitOfWork.CompleteAsync() > 0;
        }
        
        public async Task<bool> DeleteAsync(Guid id)
        {
            var taskEnitiy = _unitOfWork.Repository<TaskItem>().GetByIdAsync(id);
            if (taskEnitiy == null) return false;
            _unitOfWork.Repository<TaskItem>().Delete(taskEnitiy.Result);
            return await _unitOfWork.CompleteAsync() > 0;
        }
    }
}
