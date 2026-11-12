using Miqat.Domain.Common;
using System;

namespace Miqat.Domain.Entities
{
    /// <summary>
    /// A comment on a task — the discussion half of the mention system.
    /// EntityType.Comment and MentionedInTask notifications have referred to
    /// comments since the beginning; this is the entity that finally backs them.
    /// </summary>
    public class Comment : BaseEntity
    {
        public string Content { get; set; } = string.Empty;

        public Guid TaskId { get; set; }
        public virtual TaskItem Task { get; set; } = null!;

        public Guid AuthorId { get; set; }
        public virtual User Author { get; set; } = null!;

        public Comment(string content, Guid taskId, Guid authorId)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Comment content cannot be empty.", nameof(content));

            Content = content;
            TaskId = taskId;
            AuthorId = authorId;
        }

        private Comment() { }
    }
}
