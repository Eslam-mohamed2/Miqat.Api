using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
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
            if (_currentUser.IsAdmin) return true;
            var userId = _currentUser.RequireUserId();

            var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
            if (group == null) return false;
            if (group.OwnerId == userId) return true;

            return await IsMemberAsync(groupId, userId);
        }

        public async Task<bool> CanManageGroupAsync(Guid groupId)
        {
            if (_currentUser.IsAdmin) return true;
            var userId = _currentUser.RequireUserId();

            var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
            return group != null && group.OwnerId == userId;
        }

        public async Task<bool> CanViewTaskAsync(Guid taskId)
        {
            if (_currentUser.IsAdmin) return true;
            var userId = _currentUser.RequireUserId();

            var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
            if (task == null) return false;

            if (task.UserId == userId) return true;
            if (task.AssignedToUserId == userId) return true;

            // Work inside a project is visible to that project.
            return task.GroupId.HasValue && await CanViewGroupAsync(task.GroupId.Value);
        }

        public Task<bool> CanEditTaskAsync(Guid taskId) => CanViewTaskAsync(taskId);

        public async Task<bool> CanDeleteTaskAsync(Guid taskId)
        {
            if (_currentUser.IsAdmin) return true;
            var userId = _currentUser.RequireUserId();

            var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
            if (task == null) return false;

            // Deleting is narrower than editing: the creator, or whoever owns the
            // project it lives in. A fellow member can change a task but not
            // destroy someone else's work.
            if (task.UserId == userId) return true;
            return task.GroupId.HasValue && await CanManageGroupAsync(task.GroupId.Value);
        }

        public async Task RequireAsync(Task<bool> check, string message)
        {
            if (!await check) throw new ApiException(message, 403);
        }

        private async Task<bool> IsMemberAsync(Guid groupId, Guid userId)
        {
            var membership = await _unitOfWork.Repository<GroupMember>()
                .FindAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
            return membership.Any();
        }
    }
}
