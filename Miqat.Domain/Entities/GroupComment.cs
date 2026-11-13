using Miqat.Domain.Common;
using System;

namespace Miqat.Domain.Entities
{
    /// <summary>
    /// A comment in a project's discussion: what its members write to each
    /// other about the project as a whole, next to the comments on its tasks.
    /// <para>
    /// A comment is either a comment of its own or a reply to one
    /// (<see cref="ParentId"/>). Replies are one level deep: a reply never has
    /// replies of its own.
    /// </para>
    /// </summary>
    public class GroupComment : BaseEntity
    {
        public const int MaxContentLength = 2000;

        public string Content { get; set; } = string.Empty;

        public Guid GroupId { get; set; }
        public virtual Group Group { get; set; } = null!;

        public Guid AuthorId { get; set; }
        public virtual User Author { get; set; } = null!;

        /// <summary>The comment this one answers; null for a comment of its own.</summary>
        public Guid? ParentId { get; private set; }
        public virtual GroupComment? Parent { get; set; }

        public GroupComment(string content, Guid groupId, Guid authorId, Guid? parentId = null)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Comment content cannot be empty.", nameof(content));

            Content = content;
            GroupId = groupId;
            AuthorId = authorId;
            ParentId = parentId;
        }

        private GroupComment() { }
    }
}
