using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// A soft-deleted project, task or event is treated as missing: nobody is granted
    /// anything on it, admins included.
    /// </remarks>
    public class AccessPolicy : IAccessPolicy
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;

        public AccessPolicy(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
        }

        public async Task<bool> CanViewGroupAsync(Guid groupId)
        {
            var userId = _currentUser.RequireUserId();

            var group = await FindGroupAsync(groupId);
            if (group == null) return false;
            if (_currentUser.IsAdmin) return true;
            if (group.OwnerId == userId) return true;

            return await IsMemberAsync(groupId, userId);
        }

        public async Task<bool> CanManageGroupAsync(Guid groupId)
        {
            var userId = _currentUser.RequireUserId();

            var group = await FindGroupAsync(groupId);
            if (group == null) return false;

            return _currentUser.IsAdmin || group.OwnerId == userId;
        }

        public async Task<bool> CanViewTaskAsync(Guid taskId)
        {
            var userId = _currentUser.RequireUserId();

            var task = await FindTaskAsync(taskId);
            if (task == null) return false;
            if (_currentUser.IsAdmin) return true;

            if (task.UserId == userId) return true;
            if (task.AssignedToUserId == userId) return true;

            // Work inside a project is visible to that project.
            return task.GroupId.HasValue && await CanViewGroupAsync(task.GroupId.Value);
        }

        public Task<bool> CanEditTaskAsync(Guid taskId) => CanViewTaskAsync(taskId);

        public async Task<bool> CanDeleteTaskAsync(Guid taskId)
        {
            var userId = _currentUser.RequireUserId();

            var task = await FindTaskAsync(taskId);
            if (task == null) return false;
            if (_currentUser.IsAdmin) return true;

            // Deleting is narrower than editing: the creator, or whoever owns the
            // project it lives in. A fellow member can change a task but not
            // destroy someone else's work.
            if (task.UserId == userId) return true;
            return task.GroupId.HasValue && await CanManageGroupAsync(task.GroupId.Value);
        }

        public async Task<bool> CanViewEventAsync(Guid eventId)
        {
            var userId = _currentUser.RequireUserId();

            var calendarEvent = await FindEventAsync(eventId);
            if (calendarEvent == null) return false;
            if (_currentUser.IsAdmin || calendarEvent.OwnerId == userId) return true;

            if (await _unitOfWork.Repository<EventAttendee>()
                    .AnyAsync(a => a.EventId == eventId && a.UserId == userId))
                return true;

            return calendarEvent.GroupId.HasValue && await CanViewGroupAsync(calendarEvent.GroupId.Value);
        }

        public async Task<bool> CanManageEventAsync(Guid eventId)
        {
            var userId = _currentUser.RequireUserId();

            var calendarEvent = await FindEventAsync(eventId);
            return calendarEvent != null && (_currentUser.IsAdmin || calendarEvent.OwnerId == userId);
        }

        public async Task RequireItemAsync(CalendarItemKind kind, Guid itemId, bool toChange = false)
        {
            var isTask = kind == CalendarItemKind.Task;
            var name = isTask ? "task" : "event";

            var exists = isTask ? await FindTaskAsync(itemId) != null : await FindEventAsync(itemId) != null;
            if (!exists) throw new ApiException($"That {name} was not found.", 404);

            // Everyone who can see a task may work on it; an event is changed by its owner only.
            var allowed = isTask
                ? await CanViewTaskAsync(itemId)
                : await (toChange ? CanManageEventAsync(itemId) : CanViewEventAsync(itemId));

            if (!allowed)
                throw new ApiException(
                    toChange ? $"You cannot change this {name}." : $"You do not have access to this {name}.", 403);
        }

        public async Task RequireAsync(Task<bool> check, string message)
        {
            if (!await check) throw new ApiException(message, 403);
        }

        private async Task<Group?> FindGroupAsync(Guid groupId)
        {
            var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
            return group == null || group.IsDeleted ? null : group;
        }

        private async Task<TaskItem?> FindTaskAsync(Guid taskId)
        {
            var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
            return task == null || task.IsDeleted ? null : task;
        }

        private async Task<CalendarEvent?> FindEventAsync(Guid eventId)
        {
            var calendarEvent = await _unitOfWork.Repository<CalendarEvent>().GetByIdAsync(eventId);
            return calendarEvent == null || calendarEvent.IsDeleted ? null : calendarEvent;
        }

        // An existence check in the database; the membership row is not loaded.
        private Task<bool> IsMemberAsync(Guid groupId, Guid userId) =>
            _unitOfWork.Repository<GroupMember>()
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
    }
}
