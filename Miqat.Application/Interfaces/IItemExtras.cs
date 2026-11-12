using Microsoft.AspNetCore.Http;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Interfaces
{
    /// <summary>"Remind me N minutes before" on a task or an event. Each person has their own reminders.</summary>
    public interface IReminderService
    {
        /// <summary>The reminders of the caller that still have something ahead, soonest first.</summary>
        Task<List<ReminderDto>> GetUpcomingAsync();

        Task<List<ReminderDto>> GetForItemAsync(CalendarItemKind kind, Guid itemId);
        Task<ReminderDto> AddAsync(CalendarItemKind kind, Guid itemId, int minutesBefore);
        Task<bool> DeleteAsync(Guid reminderId);

        /// <summary>Makes a reminder fire again in a number of minutes.</summary>
        Task<ReminderDto?> SnoozeAsync(Guid reminderId, int minutes);

        // ── For the task and event modules. None of these save: the reminders
        //    are stored together with the change they belong to. ───────────────

        /// <summary>Adds reminders for one person on an event: the given ones, or their default from the calendar settings.</summary>
        Task StageForEventAsync(CalendarEvent calendarEvent, Guid userId, IEnumerable<int>? minutesBefore = null);

        /// <summary>The event moved (or its repeat changed): point every reminder on it at the new time.</summary>
        Task RescheduleAsync(CalendarEvent calendarEvent);

        /// <summary>The due date or status of the task changed.</summary>
        Task RescheduleAsync(TaskItem task);

        /// <summary>Drops the reminders on an item: everyone's, or those of one person.</summary>
        Task RemoveAsync(CalendarItemKind kind, Guid itemId, Guid? userId = null);
    }

    /// <summary>Files attached to a task or an event.</summary>
    public interface IAttachmentService
    {
        Task<List<AttachmentDto>> GetForItemAsync(CalendarItemKind kind, Guid itemId);
        Task<AttachmentDto> AddAsync(CalendarItemKind kind, Guid itemId, IFormFile file);

        /// <summary>The stored file, for someone who may see the item it is attached to. Null when it does not exist.</summary>
        Task<AttachmentFile?> OpenAsync(Guid attachmentId);

        Task<bool> DeleteAsync(Guid attachmentId);
    }
}
