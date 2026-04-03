using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface INotificationService
    {
        Task<IEnumerable<NotificationDto>> GetUnreadNotifications(Guid userId);
        Task<IEnumerable<NotificationDto>> GetAllNotifications(Guid userId);
        Task<bool> MarkAsReadAsync(Guid notificationId);
        Task<bool> MarkAllAsReadAsync(Guid userId);
        Task<bool> DeleteAsync(Guid id);
    }
}
