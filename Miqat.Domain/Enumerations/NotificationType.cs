using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Enumerations
{
    public enum NotificationType
    {
        TaskAssigned,
        TaskCompleted,
        TaskDueSoon,
        GroupInvite,
        MentionedInTask,
        FriendRequestSent,
        FriendRequestAccepted,
        UserBlocked,

        // Calendar. Stored as text, so adding members needs no migration.
        EventInvitation,
        EventResponse,
        EventChanged,
        EventCancelled,

        /// <summary>A reminder the user set on a task or an event.</summary>
        Reminder
    }
}
