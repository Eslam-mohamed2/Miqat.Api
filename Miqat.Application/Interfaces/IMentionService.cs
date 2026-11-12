using Miqat.Application.Modules;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Interfaces
{
    public interface IMentionService
    {
        Task<IEnumerable<MentionDto>> GetMentionsAsync(Guid userId);
        Task<IEnumerable<MentionDto>> GetUnreadMentionsAsync(Guid userId);
        Task<int> GetUnreadMentionsCountAsync(Guid userId);
        Task<bool> MarkMentionAsReadAsync(Guid mentionId, Guid userId);

        /// <summary>Marks every unread mention of the user as read. Returns how many changed.</summary>
        Task<int> MarkAllMentionsAsReadAsync(Guid userId);

        Task<IEnumerable<Guid>> ParseMentionsFromTextAsync(string text);
        Task<IEnumerable<MentionDto>> CreateMentionsAsync(Guid mentionedByUserId, IEnumerable<Guid> mentionedUserIds, EntityType entityType, Guid entityId);
    }
}
