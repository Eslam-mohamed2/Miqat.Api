using System.Text.RegularExpressions;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    public class MentionService : IMentionService
    {
        private const int MaxMentionsPerRequest = 50;

        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationSender _notifications;

        public MentionService(IUnitOfWork unitOfWork, INotificationSender notifications)
        {
            _unitOfWork = unitOfWork;
            _notifications = notifications;
        }

        public async Task<IEnumerable<MentionDto>> GetMentionsAsync(Guid userId)
        {
            var mentions = await _unitOfWork.Repository<Mention>()
                .FindAsync(m => m.MentionedUserId == userId && !m.IsDeleted);

            return await MapToDtosAsync(mentions);
        }

        public async Task<IEnumerable<MentionDto>> GetUnreadMentionsAsync(Guid userId)
        {
            var mentions = await _unitOfWork.Repository<Mention>()
                .FindAsync(m => m.MentionedUserId == userId && !m.IsRead && !m.IsDeleted);

            return await MapToDtosAsync(mentions);
        }

        public async Task<int> GetUnreadMentionsCountAsync(Guid userId)
        {
            var counts = await _unitOfWork.Repository<Mention>()
                .CountGroupedAsync(
                    m => m.MentionedUserId == userId && !m.IsRead && !m.IsDeleted,
                    m => m.MentionedUserId);

            return counts.TryGetValue(userId, out var count) ? count : 0;
        }

        public async Task<bool> MarkMentionAsReadAsync(Guid mentionId, Guid userId)
        {
            var mention = await _unitOfWork.Repository<Mention>().GetByIdAsync(mentionId);
            if (mention == null || mention.IsDeleted || mention.MentionedUserId != userId)
                return false;

            if (!mention.IsRead)
            {
                mention.MarkAsRead();
                await _unitOfWork.CompleteAsync();
            }

            return true;
        }

        public async Task<int> MarkAllMentionsAsReadAsync(Guid userId)
        {
            var unread = (await _unitOfWork.Repository<Mention>()
                    .FindAsync(m => m.MentionedUserId == userId && !m.IsRead && !m.IsDeleted))
                .ToList();

            foreach (var mention in unread)
                mention.MarkAsRead();

            if (unread.Count > 0)
                await _unitOfWork.CompleteAsync();

            return unread.Count;
        }

        public async Task<IEnumerable<Guid>> ParseMentionsFromTextAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<Guid>();

            // @name - word characters only
            var names = Regex.Matches(text, @"@(\w+)")
                .Select(m => m.Groups[1].Value.ToLower())
                .Distinct()
                .Take(MaxMentionsPerRequest)
                .ToList();

            var mentionedUserIds = new HashSet<Guid>();

            foreach (var name in names)
            {
                // Match on the name, or on the part of the email before the @.
                var users = await _unitOfWork.Repository<User>()
                    .FindAsync(u => (u.FullName.ToLower().Contains(name) ||
                                     u.Email.ToLower().StartsWith(name + "@")) &&
                                    !u.IsDeleted && u.IsActive);

                var match = users.OrderBy(u => u.FullName).FirstOrDefault();
                if (match != null)
                    mentionedUserIds.Add(match.Id);
            }

            return mentionedUserIds;
        }

        public async Task<IEnumerable<MentionDto>> CreateMentionsAsync(
            Guid mentionedByUserId, IEnumerable<Guid> mentionedUserIds, EntityType entityType, Guid entityId)
        {
            if (!Enum.IsDefined(entityType))
                throw new ApiException("EntityType must be Project, Task or Comment.", 400);

            var mentionedByUser = await _unitOfWork.Repository<User>().GetByIdAsync(mentionedByUserId);
            if (mentionedByUser == null || mentionedByUser.IsDeleted)
                throw new ApiException("User not found.", 404);

            // No self mentions, no duplicates.
            var targetIds = mentionedUserIds
                .Where(id => id != Guid.Empty && id != mentionedByUserId)
                .Distinct()
                .Take(MaxMentionsPerRequest)
                .ToList();

            if (targetIds.Count == 0)
                return new List<MentionDto>();

            var existingUsers = await _unitOfWork.Repository<User>()
                .FindAsync(u => targetIds.Contains(u.Id) && !u.IsDeleted);
            var existingUserIds = existingUsers.Select(u => u.Id).ToHashSet();

            var alreadyMentioned = await _unitOfWork.Repository<Mention>()
                .FindAsync(m => m.MentionedByUserId == mentionedByUserId &&
                                targetIds.Contains(m.MentionedUserId) &&
                                m.EntityType == entityType &&
                                m.EntityId == entityId &&
                                !m.IsDeleted);
            var alreadyMentionedIds = alreadyMentioned.Select(m => m.MentionedUserId).ToHashSet();

            var created = new List<Mention>();

            foreach (var mentionedUserId in targetIds)
            {
                if (!existingUserIds.Contains(mentionedUserId) || alreadyMentionedIds.Contains(mentionedUserId))
                    continue;

                var mention = new Mention(mentionedByUserId, mentionedUserId, entityType, entityId);
                await _unitOfWork.Repository<Mention>().AddAsync(mention);
                created.Add(mention);
            }

            var staged = await _notifications.StageAsync(
                new NotificationContent(
                    NotificationType.MentionedInTask, "You were mentioned",
                    actor => $"{actor} mentioned you in a {entityType.ToString().ToLower()}.",
                    mentionedByUserId, entityId, entityType.ToString()),
                created.Select(m => m.MentionedUserId));

            // One save: either every mention and its notification is stored, or none.
            await _unitOfWork.CompleteAsync();
            await _notifications.DeliverAsync(staged);

            return await MapToDtosAsync(created);
        }

        /// <summary>
        /// Maps a list of mentions, resolving in a fixed number of queries:
        /// who wrote each one, and the name of the thing it is in - the task's
        /// title, the project's name, or (for a comment) the task the comment
        /// belongs to. The list used to carry only an id, leaving every client
        /// to look the name up itself.
        /// </summary>
        private async Task<List<MentionDto>> MapToDtosAsync(IEnumerable<Mention> mentions)
        {
            var list = mentions.OrderByDescending(m => m.CreatedAt).ToList();
            if (list.Count == 0) return new List<MentionDto>();

            var authorIds = list.Select(m => m.MentionedByUserId).Distinct().ToList();
            var authors = (await _unitOfWork.Repository<User>().FindAsync(u => authorIds.Contains(u.Id)))
                .ToDictionary(u => u.Id);

            List<Guid> IdsOf(EntityType type) =>
                list.Where(m => m.EntityType == type).Select(m => m.EntityId).Distinct().ToList();

            var taskIds = IdsOf(EntityType.Task);
            var projectIds = IdsOf(EntityType.Project);
            var commentIds = IdsOf(EntityType.Comment);

            // A comment mention is named after the task the comment sits on.
            var commentTaskIds = new Dictionary<Guid, Guid>();
            if (commentIds.Count > 0)
            {
                commentTaskIds = (await _unitOfWork.Repository<Comment>().FindAsync(c => commentIds.Contains(c.Id)))
                    .ToDictionary(c => c.Id, c => c.TaskId);
                taskIds = taskIds.Concat(commentTaskIds.Values).Distinct().ToList();
            }

            var taskTitles = taskIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await _unitOfWork.Repository<TaskItem>().FindAsync(t => taskIds.Contains(t.Id)))
                    .ToDictionary(t => t.Id, t => t.Title);

            var projectNames = projectIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await _unitOfWork.Repository<Domain.Entities.Group>().FindAsync(g => projectIds.Contains(g.Id)))
                    .ToDictionary(g => g.Id, g => g.Name);

            string? NameOf(Mention m) => m.EntityType switch
            {
                EntityType.Task => taskTitles.GetValueOrDefault(m.EntityId),
                EntityType.Project => projectNames.GetValueOrDefault(m.EntityId),
                EntityType.Comment => commentTaskIds.TryGetValue(m.EntityId, out var taskId)
                    ? taskTitles.GetValueOrDefault(taskId)
                    : null,
                _ => null
            };

            return list.Select(m =>
            {
                var author = authors.GetValueOrDefault(m.MentionedByUserId);
                return new MentionDto
                {
                    Id = m.Id,
                    MentionedByUserId = m.MentionedByUserId,
                    MentionedByUserName = author?.FullName,
                    MentionedByUserProfilePictureUrl = author?.ProfilePictureUrl,
                    MentionedUserId = m.MentionedUserId,
                    EntityType = m.EntityType.ToString(),
                    EntityId = m.EntityId,
                    EntityName = NameOf(m),
                    IsRead = m.IsRead,
                    CreatedAt = m.CreatedAt
                };
            }).ToList();
        }
    }
}
