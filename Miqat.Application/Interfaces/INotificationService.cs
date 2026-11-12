using Miqat.Application.Common;
using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    public interface INotificationService
    {
        Task<IEnumerable<NotificationDto>> GetUnreadNotifications(Guid userId);
        Task<int> GetUnreadCountAsync(Guid userId);
        Task<IEnumerable<NotificationDto>> GetAllNotifications(Guid userId);
        Task<PagedResult<NotificationDto>> GetNotificationsPaged(Guid userId, int pageIndex, int pageSize);
        Task<bool> MarkAsReadAsync(Guid notificationId);
        Task<bool> MarkAllAsReadAsync(Guid userId);
        Task<bool> DeleteAsync(Guid id);
    }
}
