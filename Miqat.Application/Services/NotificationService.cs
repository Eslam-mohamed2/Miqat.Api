using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Notifications;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly NotificationMapper _mapper;
        private readonly ICurrentUserService _currentUser;

        public NotificationService(
            IUnitOfWork unitOfWork,
            NotificationMapper mapper,
            ICurrentUserService currentUser)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _currentUser = currentUser;
        }

        /// <summary>
        /// A notification is private to its recipient. Both mark-as-read and
        /// delete took a bare id and acted on it, so anyone could mark or delete
        /// another user's notifications by id.
        /// </summary>
        private void RequireOwnership(Notification entity)
        {
            if (_currentUser.IsAdmin) return;
            if (entity.RecipientUserId != _currentUser.RequireUserId())
                throw new ApiException("That notification is not yours.", 403);
        }

        public async Task<IEnumerable<NotificationDto>> GetUnreadNotifications(Guid userId)
        {
            var spec = new UnreadNotificationsSpec(userId);
            var notifications = await _unitOfWork.Repository<Notification>()
                .ListAsync(spec);
            return _mapper.MapToDtos(notifications);
        }

        public async Task<IEnumerable<NotificationDto>> GetAllNotifications(Guid userId)
        {
            // Was a bare FindAsync, which loaded neither the triggering user (so
            // TriggeredByUserName came back null for every row) nor any ordering
            // (so the feed arrived in arbitrary order rather than newest-first).
            var spec = new AllNotificationsSpec(userId);
            var notifications = await _unitOfWork.Repository<Notification>()
                .ListAsync(spec);
            return _mapper.MapToDtos(notifications);
        }

        public async Task<PagedResult<NotificationDto>> GetNotificationsPaged(
            Guid userId, int pageIndex, int pageSize)
        {
            pageIndex = Math.Max(0, pageIndex);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var page = await _unitOfWork.Repository<Notification>()
                .ListAsync(new AllNotificationsPagedSpec(userId, pageIndex, pageSize));
            var total = await _unitOfWork.Repository<Notification>()
                .CountAsync(new AllNotificationsSpec(userId));

            return PagedResult<NotificationDto>.Create(
                _mapper.MapToDtos(page).ToList(), total, pageIndex, pageSize);
        }

        public async Task<bool> MarkAsReadAsync(Guid notificationId)
        {
            var entity = await _unitOfWork.Repository<Notification>()
                .GetByIdAsync(notificationId);
            if (entity == null) return false;
            RequireOwnership(entity);

            entity.IsRead = true;
            entity.SetUpdated();
            _unitOfWork.Repository<Notification>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> MarkAllAsReadAsync(Guid userId)
        {
            var notifications = await _unitOfWork.Repository<Notification>()
                .FindAsync(n => n.RecipientUserId == userId && !n.IsRead);

            foreach (var n in notifications)
            {
                n.IsRead = true;
                n.SetUpdated();
                _unitOfWork.Repository<Notification>().Update(n);
            }

            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _unitOfWork.Repository<Notification>()
                .GetByIdAsync(id);
            if (entity == null) return false;
            RequireOwnership(entity);

            entity.SoftDelete();
            _unitOfWork.Repository<Notification>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }
    }
}
