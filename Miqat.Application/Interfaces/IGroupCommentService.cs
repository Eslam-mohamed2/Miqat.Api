using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// A project's discussion: comments its owner and members write to each
    /// other. Everything here answers 404 for a project that does not exist and
    /// 403 for someone who is not on it, as the project itself does.
    /// </summary>
    public interface IGroupCommentService
    {
        /// <summary>
        /// The newest page, or with <paramref name="beforeCommentId"/> the page
        /// before that comment. A page holds up to <paramref name="limit"/>
        /// comments of their own (1-100, 30 when left out) with all their replies.
        /// </summary>
        Task<GroupCommentPageDto> GetPageAsync(Guid groupId, Guid? beforeCommentId, int? limit);

        Task<GroupCommentDto> AddAsync(Guid groupId, CreateGroupCommentDto dto);

        /// <summary>Author only. Null when the comment does not exist.</summary>
        Task<GroupCommentDto?> UpdateAsync(Guid commentId, string content);

        /// <summary>
        /// The author or the project's owner. The replies to the comment are
        /// deleted with it. False when the comment does not exist.
        /// </summary>
        Task<bool> DeleteAsync(Guid commentId);

        /// <summary>Who can be @mentioned in the discussion: everyone on the project but the caller.</summary>
        Task<IEnumerable<MentionableUserDto>> GetMentionableAsync(Guid groupId);
    }
}
