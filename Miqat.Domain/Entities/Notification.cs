using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Entities
{
    public class Notification : BaseEntity
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; } = false;
        public NotificationType Type { get; set; }

        // Who receives it
        public Guid RecipientUserId { get; set; }
        public virtual User RecipientUser { get; set; } = null!;

        // Who triggered it (null = system generated)
        public Guid? TriggeredByUserId { get; set; }
        public virtual User? TriggeredByUser { get; set; }

        // The entity that caused it
        public Guid? LinkedEntityId { get; set; }
        public string? LinkedEntityType { get; set; } // "TaskItem" | "Group"

        public Notification(string title, string message, NotificationType type,
                            Guid recipientUserId, Guid? triggeredByUserId = null,
                            Guid? linkedEntityId = null, string? linkedEntityType = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title cannot be empty.", nameof(title));

            Title = title;
            Message = message;
            Type = type;
            RecipientUserId = recipientUserId;
            TriggeredByUserId = triggeredByUserId;
            LinkedEntityId = linkedEntityId;
            LinkedEntityType = linkedEntityType;
        }

        private Notification() { }
    }
}
