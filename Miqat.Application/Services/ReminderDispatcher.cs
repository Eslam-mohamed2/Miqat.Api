using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Scheduling;
using Miqat.Application.Specifications.Events;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Services
{
    /// <summary>
    /// Sends the reminders whose time has come. Run on a timer by the host.
    /// <para>
    /// Two kinds: the reminders people set themselves ("10 minutes before"),
    /// and the automatic "due soon" notice every open task gets once, a day
    /// before it is due. Both ask the database only for what is due now, so a
    /// pass costs the same however many tasks and events exist.
    /// </para>
    /// </summary>
    public class ReminderDispatcher
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationSender _notifications;

        public ReminderDispatcher(IUnitOfWork unitOfWork, INotificationSender notifications)
        {
            _unitOfWork = unitOfWork;
            _notifications = notifications;
        }

        /// <summary>Returns how many notifications were sent.</summary>
        public async Task<int> RunAsync(DateTime now, TimeSpan dueSoonWindow)
        {
            var staged = await StageRemindersAsync(now);
            staged.AddRange(await StageDueSoonAsync(now, dueSoonWindow));

            // One save for the pass: the notifications, and the bookkeeping that
            // stops them from being sent a second time.
            await _unitOfWork.CompleteAsync();

            foreach (var batch in staged) await _notifications.DeliverAsync(batch);
            return staged.Sum(batch => batch.Items.Count);
        }

        // ── Reminders people set ──────────────────────────────────────────────

        private async Task<List<StagedNotifications>> StageRemindersAsync(DateTime now)
        {
            var staged = new List<StagedNotifications>();

            var due = (await _unitOfWork.Repository<Reminder>()
                .FindAsync(r => r.RemindAt != null && r.RemindAt <= now)).ToList();
            if (due.Count == 0) return staged;

            var tasks = await LoadAsync(due, CalendarItemKind.Task,
                ids => _unitOfWork.Repository<TaskItem>().SelectAsync(t => ids.Contains(t.Id), t => t), t => t.Id);
            var events = await LoadAsync(due, CalendarItemKind.Event,
                async ids => (await _unitOfWork.Repository<CalendarEvent>()
                    .ListAsync(new EventsWithDetailsSpec(ids))).ToList(), e => e.Id);

            foreach (var reminder in due)
            {
                var item = reminder.ItemKind == CalendarItemKind.Task
                    ? Describe(tasks.GetValueOrDefault(reminder.ItemId), reminder)
                    : Describe(events.GetValueOrDefault(reminder.ItemId), reminder, now);

                if (item == null)
                {
                    // The task or event is gone: so is its reminder.
                    _unitOfWork.Repository<Reminder>().Delete(reminder);
                    continue;
                }

                if (item.About.HasValue)
                {
                    staged.Add(await _notifications.StageAsync(
                        new NotificationContent(
                            NotificationType.Reminder, "Reminder", _ => item.Message(item.About.Value - now),
                            LinkedEntityId: reminder.ItemId, LinkedEntityType: item.LinkedType),
                        new[] { reminder.UserId }));
                }

                reminder.MarkSent(now, NextToAnnounce(reminder, item, now));
            }

            return staged;
        }

        /// <summary>
        /// The date the reminder announces next. Normally the first one whose
        /// reminder time has not passed yet, so a reminder never fires twice in
        /// a row. A reminder that was snoozed before its real time keeps the
        /// date it was waiting for.
        /// </summary>
        private static DateTime? NextToAnnounce(Reminder reminder, RemindedItem item, DateTime now)
        {
            var earliest = now.AddMinutes(reminder.MinutesBefore);
            if (reminder.ItemTime > earliest) return reminder.ItemTime;

            return item.Next(item.About > earliest ? item.About.Value : earliest);
        }

        /// <summary>What a reminder on a task says; null when the task no longer exists.</summary>
        private static RemindedItem? Describe(TaskItem? task, Reminder reminder)
        {
            if (task == null || task.IsDeleted) return null;

            var about = reminder.IsSnoozed ? ItemTimes.Current(task) : reminder.ItemTime;
            if (!task.IsOpen) about = null;

            return new RemindedItem(
                about, "TaskItem",
                left => left > TimeSpan.Zero
                    ? $"\"{task.Title}\" is due {ClockTime.Until(left)}."
                    : $"\"{task.Title}\" is due now.",
                after => ItemTimes.Next(task, after));
        }

        private static RemindedItem? Describe(CalendarEvent? calendarEvent, Reminder reminder, DateTime now)
        {
            if (calendarEvent == null) return null;

            var about = reminder.IsSnoozed ? ItemTimes.Current(calendarEvent, now) : reminder.ItemTime;

            return new RemindedItem(
                about, "Event",
                left => left > TimeSpan.Zero
                    ? $"\"{calendarEvent.Title}\" starts {ClockTime.Until(left)}."
                    : $"\"{calendarEvent.Title}\" has started.",
                after => ItemTimes.Next(calendarEvent, after));
        }

        /// <summary>Loads the items of one kind the due reminders point at, in one query.</summary>
        private static async Task<Dictionary<Guid, T>> LoadAsync<T>(
            IEnumerable<Reminder> due, CalendarItemKind kind, Func<List<Guid>, Task<List<T>>> load, Func<T, Guid> idOf)
        {
            var ids = due.Where(r => r.ItemKind == kind).Select(r => r.ItemId).Distinct().ToList();
            return ids.Count == 0 ? new Dictionary<Guid, T>() : (await load(ids)).ToDictionary(idOf);
        }

        /// <param name="About">The start or due time to announce; null when there is nothing to say (finished, or over).</param>
        private sealed record RemindedItem(
            DateTime? About, string LinkedType, Func<TimeSpan, string> Message, Func<DateTime, DateTime?> Next);

        // ── The automatic "due soon" notice ───────────────────────────────────

        private async Task<List<StagedNotifications>> StageDueSoonAsync(DateTime now, TimeSpan window)
        {
            var until = now.Add(window);

            // DueSoonRemindedFor is what keeps this to the tasks that still need
            // a notice: usually none at all.
            var tasks = await _unitOfWork.Repository<TaskItem>().FindAsync(
                t => !t.IsDeleted &&
                     t.DueDate != null && t.DueDate > now && t.DueDate <= until &&
                     t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled &&
                     (t.DueSoonRemindedFor == null || t.DueSoonRemindedFor != t.DueDate));

            var staged = new List<StagedNotifications>();

            foreach (var task in tasks)
            {
                var recipients = new HashSet<Guid> { task.UserId };
                if (task.AssignedToUserId.HasValue) recipients.Add(task.AssignedToUserId.Value);

                var left = task.DueDate!.Value - now;
                staged.Add(await _notifications.StageAsync(
                    new NotificationContent(
                        NotificationType.TaskDueSoon, "Task due soon",
                        _ => $"\"{task.Title}\" is due {ClockTime.Until(left)}.",
                        LinkedEntityId: task.Id, LinkedEntityType: "TaskItem"),
                    recipients));

                task.DueSoonRemindedFor = task.DueDate;
            }

            return staged;
        }
    }
}
