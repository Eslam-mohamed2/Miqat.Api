using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Notifications;
using Miqat.Domain.Entities;

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
            var notifications = await _unitOfWork.Repository<Notification>()
                .ListAsync(new UnreadNotificationsSpec(userId));
            return await ToDtosAsync(notifications);
        }

        public async Task<int> GetUnreadCountAsync(Guid userId)
        {
            // A COUNT in the database: the badge polls this, so it must not load
            // the rows just to count them.
            var counts = await _unitOfWork.Repository<Notification>()
                .CountGroupedAsync(
                    n => n.RecipientUserId == userId && !n.IsRead && !n.IsDeleted,
                    n => n.RecipientUserId);

            return counts.TryGetValue(userId, out var count) ? count : 0;
        }

        public async Task<IEnumerable<NotificationDto>> GetAllNotifications(Guid userId)
        {
            // Was a bare FindAsync, which loaded neither the triggering user (so
            // TriggeredByUserName came back null for every row) nor any ordering
            // (so the feed arrived in arbitrary order rather than newest-first).
            var notifications = await _unitOfWork.Repository<Notification>()
                .ListAsync(new AllNotificationsSpec(userId));
            return await ToDtosAsync(notifications);
        }

        public async Task<PagedResult<NotificationDto>> GetNotificationsPaged(
            Guid userId, int pageIndex, int pageSize)
        {
            (pageIndex, pageSize) = Paging.Normalize(pageIndex, pageSize);

            var page = await _unitOfWork.Repository<Notification>()
                .ListAsync(new AllNotificationsPagedSpec(userId, pageIndex, pageSize));
            var total = await _unitOfWork.Repository<Notification>()
                .CountAsync(new AllNotificationsSpec(userId));

            return PagedResult<NotificationDto>.Create(
                await ToDtosAsync(page), total, pageIndex, pageSize);
        }

        public async Task<bool> MarkAsReadAsync(Guid notificationId)
        {
            var entity = await FindLiveAsync(notificationId);
            if (entity == null) return false;
            RequireOwnership(entity);

            if (!entity.IsRead)
            {
                entity.IsRead = true;
                entity.SetUpdated();
                await _unitOfWork.CompleteAsync();
            }

            return true;
        }

        public async Task<bool> MarkAllAsReadAsync(Guid userId)
        {
            var notifications = await _unitOfWork.Repository<Notification>()
                .FindAsync(n => n.RecipientUserId == userId && !n.IsRead && !n.IsDeleted);

            foreach (var n in notifications)
            {
                n.IsRead = true;
                n.SetUpdated();
            }

            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await FindLiveAsync(id);
            if (entity == null) return false;
            RequireOwnership(entity);

            entity.SoftDelete();
            await _unitOfWork.CompleteAsync();
            return true;
        }

        /// <summary>A notification that was already deleted counts as gone.</summary>
        private async Task<Notification?> FindLiveAsync(Guid id)
        {
            var entity = await _unitOfWork.Repository<Notification>().GetByIdAsync(id);
            return entity == null || entity.IsDeleted ? null : entity;
        }

        /// <summary>
        /// Maps the rows and fills in what each one points at (the title of the
        /// task or event, the name of the project), so the feed can say
        /// "...in Website redesign" without the client looking every id up.
        /// One id-and-name query per kind that occurs, whatever the length.
        /// </summary>
        private async Task<List<NotificationDto>> ToDtosAsync(IEnumerable<Notification> notifications)
        {
            var dtos = _mapper.MapToDtos(notifications).ToList();

            var taskTitles = await NamesAsync(dtos, IsTaskLink, ids =>
                _unitOfWork.Repository<TaskItem>().SelectAsync(
                    t => ids.Contains(t.Id), t => new KeyValuePair<Guid, string>(t.Id, t.Title)));
            var groupNames = await NamesAsync(dtos, IsProjectLink, ids =>
                _unitOfWork.Repository<Group>().SelectAsync(
                    g => ids.Contains(g.Id), g => new KeyValuePair<Guid, string>(g.Id, g.Name)));
            var eventTitles = await NamesAsync(dtos, IsEventLink, ids =>
                _unitOfWork.Repository<CalendarEvent>().SelectAsync(
                    e => ids.Contains(e.Id), e => new KeyValuePair<Guid, string>(e.Id, e.Title)));

            foreach (var dto in dtos)
            {
                if (!dto.LinkedEntityId.HasValue) continue;

                var names = IsTaskLink(dto.LinkedEntityType) ? taskTitles
                    : IsProjectLink(dto.LinkedEntityType) ? groupNames
                    : IsEventLink(dto.LinkedEntityType) ? eventTitles
                    : null;

                if (names != null && names.TryGetValue(dto.LinkedEntityId.Value, out var name))
                    dto.LinkedEntityName = name;
            }

            return dtos;
        }

        /// <summary>The names behind the linked ids of one kind; no query at all when the list has none of that kind.</summary>
        private static async Task<Dictionary<Guid, string>> NamesAsync(
            List<NotificationDto> dtos, Func<string?, bool> isKind,
            Func<List<Guid>, Task<List<KeyValuePair<Guid, string>>>> load)
        {
            var ids = dtos
                .Where(d => d.LinkedEntityId.HasValue && isKind(d.LinkedEntityType))
                .Select(d => d.LinkedEntityId!.Value)
                .Distinct()
                .ToList();

            return ids.Count == 0
                ? new Dictionary<Guid, string>()
                : new Dictionary<Guid, string>(await load(ids));
        }

        // The type is free text written by whoever created the notification:
        // "TaskItem" from the task and comment modules, "Task" / "Project" from
        // the generic mention endpoint, "Group" from project invitations, "Event"
        // from the calendar.
        private static bool IsTaskLink(string? type) =>
            string.Equals(type, "TaskItem", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "Task", StringComparison.OrdinalIgnoreCase);

        private static bool IsEventLink(string? type) =>
            string.Equals(type, "Event", StringComparison.OrdinalIgnoreCase);

        private static bool IsProjectLink(string? type) =>
            string.Equals(type, "Group", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "Project", StringComparison.OrdinalIgnoreCase);
    }
}
