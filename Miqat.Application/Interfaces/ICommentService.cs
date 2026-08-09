using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface ICommentService
    {
        /// <summary>Oldest first, so the thread reads top to bottom.</summary>
        Task<IEnumerable<CommentDto>> GetForTaskAsync(Guid taskId);

        Task<CommentDto> AddAsync(Guid taskId, string content, IEnumerable<Guid>? mentionedUserIds = null);

        /// <summary>
        /// People who can be @mentioned on this task: everyone already involved,
        /// plus the caller's friends (who can be pulled in for help).
        /// </summary>
        Task<IEnumerable<MentionableUserDto>> GetMentionableAsync(Guid taskId);

        /// <summary>Author-only (or admin). Returns false when the row is gone.</summary>
        Task<bool> DeleteAsync(Guid commentId);
    }
}
