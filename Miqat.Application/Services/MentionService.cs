using System.Text.RegularExpressions;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    public class MentionService : IMentionService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;

        public MentionService(IUnitOfWork unitOfWork, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
        }

        public async Task<IEnumerable<MentionDto>> GetMentionsAsync(Guid userId)
        {
            var mentions = await _unitOfWork.Repository<Mention>()
                .FindAsync(m => m.MentionedUserId == userId && !m.IsDeleted);

            var dtos = new List<MentionDto>();
            foreach (var mention in mentions)
            {
                var mentionedByUser = await _unitOfWork.Repository<User>().GetByIdAsync(mention.MentionedByUserId);
                dtos.Add(MapToDto(mention, mentionedByUser));
            }

            return dtos.OrderByDescending(d => d.CreatedAt);
        }

        public async Task<IEnumerable<MentionDto>> GetUnreadMentionsAsync(Guid userId)
        {
            var mentions = await _unitOfWork.Repository<Mention>()
                .FindAsync(m => m.MentionedUserId == userId && !m.IsRead && !m.IsDeleted);

            var dtos = new List<MentionDto>();
            foreach (var mention in mentions)
            {
                var mentionedByUser = await _unitOfWork.Repository<User>().GetByIdAsync(mention.MentionedByUserId);
                dtos.Add(MapToDto(mention, mentionedByUser));
            }

            return dtos.OrderByDescending(d => d.CreatedAt);
        }

        public async Task<int> GetUnreadMentionsCountAsync(Guid userId)
        {
            var mentions = await _unitOfWork.Repository<Mention>()
                .FindAsync(m => m.MentionedUserId == userId && !m.IsRead && !m.IsDeleted);

            return mentions.Count();
        }

        public async Task<bool> MarkMentionAsReadAsync(Guid mentionId, Guid userId)
        {
            var mention = await _unitOfWork.Repository<Mention>().GetByIdAsync(mentionId);
            if (mention == null || mention.MentionedUserId != userId)
                return false;

            mention.MarkAsRead();
            _unitOfWork.Repository<Mention>().Update(mention);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<IEnumerable<Guid>> ParseMentionsFromTextAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<Guid>();

            // Pattern to match @username (word characters only)
            var pattern = @"@(\w+)";
            var matches = Regex.Matches(text, pattern);

            var mentionedUserIds = new HashSet<Guid>();

            foreach (Match match in matches)
            {
                var username = match.Groups[1].Value;

                // Find user by username (case-insensitive search on FullName or Email)
                var user = await _unitOfWork.Repository<User>()
                    .FindAsync(u => (u.Email.ToLower() == username.ToLower() ||
                                    u.FullName.ToLower().Contains(username.ToLower())) &&
                                   !u.IsDeleted);

                if (user.Any())
                {
                    mentionedUserIds.Add(user.First().Id);
                }
            }

            return mentionedUserIds;
        }

        public async Task<IEnumerable<MentionDto>> CreateMentionsAsync(Guid mentionedByUserId, IEnumerable<Guid> mentionedUserIds, EntityType entityType, Guid entityId)
        {
            // Verify mentionedByUser exists
            var mentionedByUser = await _unitOfWork.Repository<User>().GetByIdAsync(mentionedByUserId);
            if (mentionedByUser == null)
                throw new InvalidOperationException($"User with ID {mentionedByUserId} not found.");

            var createdMentions = new List<MentionDto>();

            foreach (var mentionedUserId in mentionedUserIds)
            {
                // Don't allow self-mentions
                if (mentionedUserId == mentionedByUserId)
                    continue;

                // Verify mentioned user exists
                var mentionedUser = await _unitOfWork.Repository<User>().GetByIdAsync(mentionedUserId);
                if (mentionedUser == null)
                    continue;

                // Check if mention already exists
                var existingMention = await _unitOfWork.Repository<Mention>()
                    .FindAsync(m => m.MentionedByUserId == mentionedByUserId &&
                                   m.MentionedUserId == mentionedUserId &&
                                   m.EntityType == entityType &&
                                   m.EntityId == entityId &&
                                   !m.IsDeleted);

                if (existingMention.Any())
                    continue;

                // Create mention
                var mention = new Mention(mentionedByUserId, mentionedUserId, entityType, entityId);
                await _unitOfWork.Repository<Mention>().AddAsync(mention);
                await _unitOfWork.CompleteAsync();

                // Create notification
                var notification = new Notification(
                    title: "You were mentioned",
                    message: $"{mentionedByUser.FullName} mentioned you in a {entityType.ToString().ToLower()}.",
                    type: NotificationType.MentionedInTask,
                    recipientUserId: mentionedUserId,
                    triggeredByUserId: mentionedByUserId,
                    linkedEntityId: entityId,
                    linkedEntityType: entityType.ToString()
                );
                await _unitOfWork.Repository<Notification>().AddAsync(notification);
                await _unitOfWork.CompleteAsync();

                createdMentions.Add(MapToDto(mention, mentionedByUser));
            }

            return createdMentions;
        }

        private MentionDto MapToDto(Mention mention, User? mentionedByUser)
        {
            return new MentionDto
            {
                Id = mention.Id,
                MentionedByUserId = mention.MentionedByUserId,
                MentionedByUserName = mentionedByUser?.FullName,
                MentionedByUserProfilePictureUrl = mentionedByUser?.ProfilePictureUrl,
                MentionedUserId = mention.MentionedUserId,
                EntityType = mention.EntityType.ToString(),
                EntityId = mention.EntityId,
                IsRead = mention.IsRead,
                CreatedAt = mention.CreatedAt
            };
        }
    }
}
