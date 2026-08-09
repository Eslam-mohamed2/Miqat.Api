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
        private readonly IAccessPolicy _access;
        private readonly ICurrentUserService _currentUser;
        private readonly IRealtimeNotifier _realtime;

        public TaskService(
            IUnitOfWork unitOfWork,
            TaskMapper mapper,
            IAccessPolicy access,
            ICurrentUserService currentUser,
            IRealtimeNotifier realtime)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _access = access;
            _currentUser = currentUser;
            _realtime = realtime;
        }

        /// <summary>
        /// Everyone whose board shows this task: its creator, its assignee, and
        /// the members of the project it belongs to. The actor is included on
        /// purpose — their other tabs should update too.
        /// </summary>
        private async Task BroadcastTaskChangedAsync(TaskItem task, string action)
        {
            var recipients = new HashSet<Guid> { task.UserId };
            if (task.AssignedToUserId.HasValue) recipients.Add(task.AssignedToUserId.Value);

            if (task.GroupId.HasValue)
            {
                var groupId = task.GroupId.Value;
                var members = await _unitOfWork.Repository<GroupMember>()
                    .FindAsync(gm => gm.GroupId == groupId);
                foreach (var member in members) recipients.Add(member.UserId);

                var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
                if (group != null) recipients.Add(group.OwnerId);
            }

            await _realtime.NotifyUsersAsync(recipients, "taskChanged", new
            {
                taskId = task.Id,
                groupId = task.GroupId,
                action,
                title = task.Title
            });
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

        public async Task<PagedResult<TaskDto>> GetTasksByUserIdPaged(
            Guid userId, int pageIndex, int pageSize)
        {
            // Clamped so a caller cannot request the whole table through the
            // paged endpoint, and a page never comes back empty by accident.
            pageIndex = Math.Max(0, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var spec = new TasksByUserIdWithPagingSpec(userId, pageIndex, pageSize);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);

            // Counted with the unpaged spec — counting the paged one would just
            // return the page length and make TotalCount useless.
            var total = await _unitOfWork.Repository<TaskItem>()
                .CountAsync(new TasksByUserIdSpec(userId));

            return PagedResult<TaskDto>.Create(
                _mapper.MapToDtos(tasks).ToList(), total, pageIndex, pageSize);
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
            await _access.RequireAsync(
                _access.CanViewGroupAsync(groupId), "You are not a member of that project.");

            var spec = new TasksByGroupSpec(groupId);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return _mapper.MapToDtos(tasks);
        }

        public async Task<TaskDto?> GetTaskById(Guid id)
        {
            var spec = new TaskByIdWithDetailsSpec(id);
            var task = await _unitOfWork.Repository<TaskItem>()
                .GetEntityWithSpec(spec);
            if (task == null) return null;

            await _access.RequireAsync(
                _access.CanViewTaskAsync(id), "You do not have access to this task.");

            return _mapper.MapToDto(task);
        }

        public async Task<TaskDto> CreateAsync(TaskDto dto)
        {
            // Taken from the token, never from the payload — otherwise a caller
            // could create work owned by someone else.
            dto.UserId = _currentUser.RequireUserId();

            // You can only file a task into a project you belong to.
            if (dto.GroupId.HasValue)
            {
                await _access.RequireAsync(
                    _access.CanViewGroupAsync(dto.GroupId.Value),
                    "You are not a member of that project.");
            }

            Enum.TryParse<Priority>(dto.Priority, ignoreCase: true, out var priority);
            Enum.TryParse<RecurrencePattern>(dto.Recurrence, ignoreCase: true, out var recurrence);

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

            // The constructor always starts a task at Pending, and nothing here
            // read dto.Status — so creating a task as "In progress" or "Completed"
            // silently produced a Pending one instead.
            if (Enum.TryParse<Domain.Enumerations.TaskStatus>(
                    dto.Status, ignoreCase: true, out var status))
                entity.Status = status;

            await _unitOfWork.Repository<TaskItem>().AddAsync(entity);
            await _unitOfWork.CompleteAsync();

            await BroadcastTaskChangedAsync(entity, "created");
            return _mapper.MapToDto(entity);
        }

        public async Task<bool> UpdateAsync(Guid id, TaskDto dto)
        {
            await _access.RequireAsync(
                _access.CanEditTaskAsync(id), "You do not have permission to edit this task.");

            // Moving a task into a project requires membership of that project.
            if (dto.GroupId.HasValue)
            {
                await _access.RequireAsync(
                    _access.CanViewGroupAsync(dto.GroupId.Value),
                    "You are not a member of that project.");
            }

            var spec = new TaskByIdWithDetailsSpec(id);
            var entity = await _unitOfWork.Repository<TaskItem>()
                .GetEntityWithSpec(spec);
            if (entity == null) return false;

            entity.Title = dto.Title;
            entity.Description = dto.Description;
            entity.DueDate = dto.DueDate;
            entity.Tags = dto.Tags;
            entity.RecurrenceEndDate = dto.RecurrenceEndDate;

            // Neither of these was applied, so a task's assignee was fixed at
            // creation and could never be reassigned, and a task could never be
            // moved between projects — both silently, since the call still 204'd.
            // PUT here is a whole-document update, so a null legitimately means
            // "unassign" / "remove from project".
            entity.AssignedToUserId = dto.AssignedToUserId;
            entity.GroupId = dto.GroupId;

            // ignoreCase matches how TaskValidator accepts these, so a value that
            // passes validation cannot then fail to parse here. Without it the parse
            // failed quietly and left the old value in place, so the caller got a 204
            // for an update that never happened.
            if (Enum.TryParse<Domain.Enumerations.TaskStatus>(
                dto.Status, ignoreCase: true, out var status))
                entity.Status = status;

            if (Enum.TryParse<Priority>(dto.Priority, ignoreCase: true, out var priority))
                entity.Priority = priority;

            if (Enum.TryParse<RecurrencePattern>(dto.Recurrence, ignoreCase: true, out var recurrence))
                entity.Recurrence = recurrence;

            entity.SetUpdated();
            _unitOfWork.Repository<TaskItem>().Update(entity);
            var saved = await _unitOfWork.CompleteAsync() > 0;

            if (saved) await BroadcastTaskChangedAsync(entity, "updated");
            return saved;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            await _access.RequireAsync(
                _access.CanDeleteTaskAsync(id),
                "Only the task's creator or the project owner can delete it.");

            var entity = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(id);
            if (entity == null) return false;
            entity.SoftDelete();
            _unitOfWork.Repository<TaskItem>().Update(entity);
            var saved = await _unitOfWork.CompleteAsync() > 0;

            if (saved) await BroadcastTaskChangedAsync(entity, "deleted");
            return saved;
        }
    }
}
