using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Tasks;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
#nullable disable


namespace Miqat.Application.Services
{
    public class TaskService : ITaskService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly TaskMapper _mapper;

        public TaskService(IUnitOfWork unitOfWork, TaskMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<IEnumerable<TaskDto>> GetAllTasks()
        {
            var tasks = await _unitOfWork.Repository<TaskItem>().GetAllAsync();
            return _mapper.MapToDtos(tasks);
        }

        public async Task<IEnumerable<TaskDto>> GetTasksByUserId(Guid userId)
        {
            var spec = new TasksByUserIdSpec(userId);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return _mapper.MapToDtos(tasks);
        }

        public async Task<IEnumerable<TaskDto>> GetTasksByUserIdPaged(
            Guid userId, int pageIndex, int pageSize)
        {
            var spec = new TasksByUserIdWithPagingSpec(userId, pageIndex, pageSize);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return _mapper.MapToDtos(tasks);
        }

        public async Task<IEnumerable<TaskDto>> GetTasksDueSoon(
            Guid userId, int withinDays = 3)
        {
            var spec = new TasksDueSoonSpec(userId, withinDays);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return _mapper.MapToDtos(tasks);
        }

        public async Task<IEnumerable<TaskDto>> GetTasksByGroup(Guid groupId)
        {
            var spec = new TasksByGroupSpec(groupId);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return _mapper.MapToDtos(tasks);
        }

        public async Task<TaskDto?> GetTaskById(Guid id)
        {
            var spec = new TaskByIdWithDetailsSpec(id);
            var task = await _unitOfWork.Repository<TaskItem>()
                .GetEntityWithSpec(spec);
            return task != null ? _mapper.MapToDto(task) : null;
        }

        public async Task<TaskDto> CreateAsync(TaskDto dto)
        {
            Enum.TryParse<Priority>(dto.Priority, out var priority);
            Enum.TryParse<RecurrencePattern>(dto.Recurrence, out var recurrence);

            var entity = new TaskItem(
                dto.Title,
                dto.Description,
                dto.UserId,
                priority,
                dto.DueDate,
                dto.AssignedToUserId,
                dto.GroupId,
                dto.Tags,
                recurrence,
                dto.RecurrenceEndDate
            );

            await _unitOfWork.Repository<TaskItem>().AddAsync(entity);
            await _unitOfWork.CompleteAsync();
            return _mapper.MapToDto(entity);
        }

        public async Task<bool> UpdateAsync(Guid id, TaskDto dto)
        {
            var spec = new TaskByIdWithDetailsSpec(id);
            var entity = await _unitOfWork.Repository<TaskItem>()
                .GetEntityWithSpec(spec);
            if (entity == null) return false;

            entity.Title = dto.Title;
            entity.Description = dto.Description;
            entity.DueDate = dto.DueDate;
            entity.Tags = dto.Tags;

            if (Enum.TryParse<Domain.Enumerations.TaskStatus>(
                dto.Status, out var status))
                entity.Status = status;

            if (Enum.TryParse<Priority>(dto.Priority, out var priority))
                entity.Priority = priority;

            if (Enum.TryParse<RecurrencePattern>(dto.Recurrence, out var recurrence))
                entity.Recurrence = recurrence;

            entity.SetUpdated();
            _unitOfWork.Repository<TaskItem>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(id);
            if (entity == null) return false;
            entity.SoftDelete();
            _unitOfWork.Repository<TaskItem>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }
    }
}
