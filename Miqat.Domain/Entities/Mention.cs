using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities
{
    public class Mention : BaseEntity
    {
        public Guid MentionedByUserId { get; set; }
        public virtual User MentionedByUser { get; set; } = null!;

        public Guid MentionedUserId { get; set; }
        public virtual User MentionedUser { get; set; } = null!;

        public EntityType EntityType { get; set; }

        public Guid EntityId { get; set; }

        public bool IsRead { get; set; } = false;

        public Mention(Guid mentionedByUserId, Guid mentionedUserId, EntityType entityType, Guid entityId)
        {
            if (mentionedByUserId == Guid.Empty)
                throw new ArgumentException("MentionedByUserId cannot be empty.", nameof(mentionedByUserId));
            if (mentionedUserId == Guid.Empty)
                throw new ArgumentException("MentionedUserId cannot be empty.", nameof(mentionedUserId));
            if (entityId == Guid.Empty)
                throw new ArgumentException("EntityId cannot be empty.", nameof(entityId));
            if (mentionedByUserId == mentionedUserId)
                throw new ArgumentException("A user cannot mention themselves.", nameof(mentionedUserId));

            MentionedByUserId = mentionedByUserId;
            MentionedUserId = mentionedUserId;
            EntityType = entityType;
            EntityId = entityId;
            IsRead = false;
        }

        private Mention() { }

        public void MarkAsRead()
        {
            IsRead = true;
            SetUpdated();
        }
    }
}
