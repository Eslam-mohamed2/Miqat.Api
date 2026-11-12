using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Scheduling;
using Miqat.Application.Specifications.Tasks;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Services
{
    public class TaskService : ITaskService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly TaskDtoFactory _dtos;
        private readonly IAccessPolicy _access;
        private readonly ICurrentUserService _currentUser;
        private readonly INotificationSender _notifications;
        private readonly IReminderService _reminders;
        private readonly TaskBroadcaster _broadcaster;

        public TaskService(
            IUnitOfWork unitOfWork,
            TaskDtoFactory dtos,
            IAccessPolicy access,
            ICurrentUserService currentUser,
            INotificationSender notifications,
            IReminderService reminders,
            TaskBroadcaster broadcaster)
        {
            _unitOfWork = unitOfWork;
            _dtos = dtos;
            _access = access;
            _currentUser = currentUser;
            _notifications = notifications;
            _reminders = reminders;
            _broadcaster = broadcaster;
        }

        public async Task<IEnumerable<TaskDto>> GetAllTasks()
        {
            // Was GetAllAsync(): that returned soft-deleted tasks too and loaded
            // no navigations, so every owner / assignee / project name was null.
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(new AllTasksSpec());
            return await _dtos.ToDtosAsync(tasks);
        }

        public async Task<IEnumerable<TaskDto>> GetTasksByUserId(Guid userId, TaskFilter? filter = null)
        {
            var spec = filter == null || filter.IsEmpty
                ? new TasksByUserIdSpec(userId)
                : new TasksByUserIdSpec(userId, filter);

            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return await _dtos.ToDtosAsync(tasks);
        }

        public async Task<PagedResult<TaskDto>> GetTasksByUserIdPaged(
            Guid userId, int pageIndex, int pageSize)
        {
            // Clamped so a caller cannot request the whole table through the
            // paged endpoint, and a page never comes back empty by accident.
            (pageIndex, pageSize) = Paging.Normalize(pageIndex, pageSize, defaultPageSize: 10);

            var spec = new TasksByUserIdWithPagingSpec(userId, pageIndex, pageSize);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);

            // Counted with the unpaged spec — counting the paged one would just
            // return the page length and make TotalCount useless.
            var total = await _unitOfWork.Repository<TaskItem>()
                .CountAsync(new TasksByUserIdSpec(userId));

            return PagedResult<TaskDto>.Create(
                await _dtos.ToDtosAsync(tasks), total, pageIndex, pageSize);
        }

        public async Task<IEnumerable<TaskDto>> GetTasksDueSoon(
            Guid userId, int withinDays = 3)
        {
            withinDays = Math.Clamp(withinDays, 0, 365);
            var now = DateTime.UtcNow;

            var spec = new TasksDueSoonSpec(userId, now, now.AddDays(withinDays));
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return await _dtos.ToDtosAsync(tasks);
        }

        public async Task<IEnumerable<TaskDto>> GetTasksByGroup(Guid groupId)
        {
            await _access.RequireAsync(
                _access.CanViewGroupAsync(groupId), "You are not a member of that project.");

            var spec = new TasksByGroupSpec(groupId);
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(spec);
            return await _dtos.ToDtosAsync(tasks);
        }

        public async Task<TaskDto?> GetTaskById(Guid id)
        {
            var task = await LoadAsync(id);
            if (task == null) return null;

            await _access.RequireAsync(
                _access.CanViewTaskAsync(id), "You do not have access to this task.");

            return await _dtos.ToDtoAsync(task);
        }

        public async Task<TaskDto> CreateAsync(TaskDto dto)
        {
            // Taken from the token, never from the payload — otherwise a caller
            // could create work owned by someone else.
            var actorId = _currentUser.RequireUserId();
            dto.UserId = actorId;

            // Create-only rule (it used to sit in the validator, where it also
            // blocked edits of overdue tasks). One day of slack, so "today" is
            // accepted in every time zone and for date-only pickers, which send
            // midnight.
            if (dto.DueDate.HasValue && dto.DueDate.Value < DateTime.UtcNow.Date.AddDays(-1))
                throw new ApiException("Due date cannot be in the past.", 400);

            EnsureDatesInOrder(dto.StartDate, dto.DueDate);

            // You can only file a task into a project you belong to.
            if (dto.GroupId.HasValue)
            {
                await _access.RequireAsync(
                    _access.CanViewGroupAsync(dto.GroupId.Value),
                    "You are not a member of that project.");
            }

            if (dto.AssignedToUserId.HasValue)
                await EnsureAssignableUserAsync(dto.AssignedToUserId.Value);

            if (!EnumText.TryParseDefined<Priority>(dto.Priority, out var priority))
                priority = Priority.Medium;
            if (!EnumText.TryParseDefined<RecurrencePattern>(dto.Recurrence, out var recurrence))
                recurrence = RecurrencePattern.None;

            var entity = new TaskItem(
                dto.Title,
                dto.Description,
                actorId,
                priority,
                dto.DueDate,
                dto.AssignedToUserId,
                dto.GroupId,
                dto.Tags,
                recurrence,
                dto.RecurrenceEndDate
            )
            {
                StartDate = dto.StartDate
            };

            // The constructor always starts a task at Pending, and nothing here
            // read dto.Status — so creating a task as "In progress" or "Completed"
            // silently produced a Pending one instead.
            if (EnumText.TryParseTaskStatus(dto.Status, out var status))
                entity.ChangeStatus(status);

            await _unitOfWork.Repository<TaskItem>().AddAsync(entity);
            await _unitOfWork.CompleteAsync();

            if (entity.AssignedToUserId.HasValue)
            {
                await NotifyAsync(
                    new[] { entity.AssignedToUserId.Value }, actorId, entity,
                    NotificationType.TaskAssigned, "Task assigned to you",
                    actor => $"{actor} assigned you \"{entity.Title}\".");
            }

            await _broadcaster.ChangedAsync(entity, "created");

            // Reloaded so the response carries owner / assignee / project details;
            // a freshly added entity has none of its navigations loaded.
            var created = await LoadAsync(entity.Id);
            return await _dtos.ToDtoAsync(created ?? entity);
        }

        public async Task<bool> UpdateAsync(Guid id, TaskDto dto)
        {
            // Loaded before the permission check so an id that does not exist
            // answers 404 rather than 403.
            var entity = await LoadAsync(id);
            if (entity == null) return false;

            await _access.RequireAsync(
                _access.CanEditTaskAsync(id), "You do not have permission to edit this task.");

            EnsureDatesInOrder(dto.StartDate, dto.DueDate);

            // Moving a task into a project requires membership of that project.
            if (dto.GroupId.HasValue && dto.GroupId != entity.GroupId)
            {
                await _access.RequireAsync(
                    _access.CanViewGroupAsync(dto.GroupId.Value),
                    "You are not a member of that project.");
            }

            if (dto.AssignedToUserId.HasValue && dto.AssignedToUserId != entity.AssignedToUserId)
                await EnsureAssignableUserAsync(dto.AssignedToUserId.Value);

            var actorId = _currentUser.RequireUserId();
            var previousAssignee = entity.AssignedToUserId;
            var previousStatus = entity.Status;

            entity.Title = dto.Title;
            entity.Description = dto.Description;
            entity.StartDate = dto.StartDate;
            entity.DueDate = dto.DueDate;
            entity.Tags = dto.Tags;
            entity.RecurrenceEndDate = dto.RecurrenceEndDate;

            // PUT here is a whole-document update, so a null legitimately means
            // "unassign" / "remove from project".
            entity.AssignedToUserId = dto.AssignedToUserId;
            entity.GroupId = dto.GroupId;

            // Parsed with the same helper TaskValidator uses, so a value that
            // passes validation cannot then fail to apply here.
            if (EnumText.TryParseTaskStatus(dto.Status, out var status))
                entity.ChangeStatus(status);

            if (EnumText.TryParseDefined<Priority>(dto.Priority, out var priority))
                entity.Priority = priority;

            if (EnumText.TryParseDefined<RecurrencePattern>(dto.Recurrence, out var recurrence))
                entity.Recurrence = recurrence;

            // The entity is tracked, so SaveChanges writes exactly what changed.
            // Repository.Update() would also mark the loaded owner, assignee and
            // project rows as modified and rewrite them on every task edit.
            var justCompleted = entity.Status == TaskStatus.Completed && previousStatus != TaskStatus.Completed;

            // Saved together with the task: reminders follow its new due date
            // (or stop when it is finished), and a repeating task that was just
            // finished gets its next one.
            await _reminders.RescheduleAsync(entity);
            var next = justCompleted ? await StageNextOccurrenceAsync(entity) : null;

            entity.SetUpdated(actorId.ToString());
            await _unitOfWork.CompleteAsync();

            if (next != null) await _broadcaster.ChangedAsync(next, "created");

            if (entity.AssignedToUserId.HasValue && entity.AssignedToUserId != previousAssignee)
            {
                await NotifyAsync(
                    new[] { entity.AssignedToUserId.Value }, actorId, entity,
                    NotificationType.TaskAssigned, "Task assigned to you",
                    actor => $"{actor} assigned you \"{entity.Title}\".");
            }

            if (justCompleted)
            {
                var interested = new List<Guid> { entity.UserId };
                if (entity.AssignedToUserId.HasValue) interested.Add(entity.AssignedToUserId.Value);

                await NotifyAsync(
                    interested, actorId, entity,
                    NotificationType.TaskCompleted, "Task completed",
                    actor => $"{actor} completed \"{entity.Title}\".");
            }

            await _broadcaster.ChangedAsync(entity, "updated");
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(id);
            if (entity == null || entity.IsDeleted) return false;

            await _access.RequireAsync(
                _access.CanDeleteTaskAsync(id),
                "Only the task's creator or the project owner can delete it.");

            entity.SoftDelete(_currentUser.UserId?.ToString());
            await _reminders.RemoveAsync(CalendarItemKind.Task, id);
            await _unitOfWork.CompleteAsync();

            await _broadcaster.ChangedAsync(entity, "deleted");
            return true;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private Task<TaskItem?> LoadAsync(Guid id) =>
            _unitOfWork.Repository<TaskItem>().GetEntityWithSpec(new TaskByIdWithDetailsSpec(id));

        private static void EnsureDatesInOrder(DateTime? startDate, DateTime? dueDate)
        {
            if (startDate.HasValue && dueDate.HasValue && startDate.Value > dueDate.Value)
                throw new ApiException("Start date cannot be after the due date.", 400);
        }

        /// <summary>
        /// A task can only be handed to an existing, active account. Without this
        /// an unknown id reached the database and came back as a 500.
        /// </summary>
        private async Task EnsureAssignableUserAsync(Guid userId)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null || user.IsDeleted || !user.IsActive)
                throw new ApiException("The user this task is assigned to was not found.", 400);
        }

        /// <summary>
        /// Tells each recipient (never the person who did the thing). The task
        /// change is already saved, so this cannot fail it.
        /// </summary>
        private Task NotifyAsync(
            IEnumerable<Guid> recipients, Guid actorId, TaskItem task,
            NotificationType type, string title, Func<string, string> message) =>
            _notifications.SendAsync(
                new NotificationContent(type, title, message, actorId, task.Id, "TaskItem"), recipients);

        /// <summary>
        /// A repeating task that is finished carries on as a new task on its
        /// next date, with the same details, a fresh checklist and the same
        /// reminders. The finished one stays as it is: a record of that date.
        /// Nothing is saved here; null when the series is over.
        /// </summary>
        private async Task<TaskItem?> StageNextOccurrenceAsync(TaskItem finished)
        {
            if (finished.Recurrence == RecurrencePattern.None || !finished.DueDate.HasValue) return null;

            var due = finished.DueDate.Value;
            var now = DateTime.UtcNow;
            var owner = await _unitOfWork.Repository<User>().GetByIdAsync(finished.UserId);

            // The next date still ahead: finishing a task three weeks late must
            // not hand back three dates that have already passed.
            var nextDue = Recurrence.NextStart(
                due, RecurrenceRule.Of(finished), TimeZones.Resolve(owner?.TimeZone), due > now ? due : now);
            if (nextDue == null) return null;

            // Finishing, reopening and finishing again must not create it twice.
            if (await _unitOfWork.Repository<TaskItem>().AnyAsync(
                    t => t.UserId == finished.UserId && t.Title == finished.Title &&
                         t.DueDate == nextDue && !t.IsDeleted))
                return null;

            var next = new TaskItem(
                finished.Title, finished.Description, finished.UserId, finished.Priority, nextDue,
                finished.AssignedToUserId, finished.GroupId, finished.Tags,
                finished.Recurrence, finished.RecurrenceEndDate)
            {
                StartDate = finished.StartDate + (nextDue.Value - due)
            };
            await _unitOfWork.Repository<TaskItem>().AddAsync(next);

            var checklist = await _unitOfWork.Repository<Subtask>().FindAsync(s => s.TaskId == finished.Id);
            foreach (var line in checklist)
                await _unitOfWork.Repository<Subtask>().AddAsync(new Subtask(next.Id, line.Title, line.Position));

            var reminders = await _unitOfWork.Repository<Reminder>()
                .FindAsync(r => r.ItemKind == CalendarItemKind.Task && r.ItemId == finished.Id);
            foreach (var reminder in reminders)
            {
                var copy = new Reminder(reminder.UserId, CalendarItemKind.Task, next.Id, reminder.MinutesBefore);
                copy.ScheduleFor(nextDue);
                await _unitOfWork.Repository<Reminder>().AddAsync(copy);
            }

            return next;
        }
    }
}
