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

        public NotificationService(IUnitOfWork unitOfWork, NotificationMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
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
            var notifications = await _unitOfWork.Repository<Notification>()
                .FindAsync(n => n.RecipientUserId == userId && !n.IsDeleted);
            return _mapper.MapToDtos(notifications);
        }

        public async Task<bool> MarkAsReadAsync(Guid notificationId)
        {
            var entity = await _unitOfWork.Repository<Notification>()
                .GetByIdAsync(notificationId);
            if (entity == null) return false;

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
            entity.SoftDelete();
            _unitOfWork.Repository<Notification>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }
    }
}
