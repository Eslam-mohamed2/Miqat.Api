using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Scheduling;
using Miqat.Application.Specifications.Events;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class ReminderService : IReminderService
    {
        /// <summary>Four weeks: the earliest a reminder may fire before its item.</summary>
        public const int MaxMinutesBefore = 4 * 7 * 24 * 60;

        private const int MaxPerItem = 5;
        private const int MaxSnoozeMinutes = 7 * 24 * 60;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IAccessPolicy _access;
        private readonly ICalendarService _calendars;

        public ReminderService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IAccessPolicy access,
            ICalendarService calendars)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _access = access;
            _calendars = calendars;
        }

        private IGenericRepository<Reminder> Reminders => _unitOfWork.Repository<Reminder>();

        // ── What the caller sees and changes ──────────────────────────────────

        public async Task<List<ReminderDto>> GetUpcomingAsync()
        {
            var userId = _currentUser.RequireUserId();
            var reminders = await Reminders.FindAsync(r => r.UserId == userId && r.RemindAt != null);

            return await ToDtosAsync(reminders.OrderBy(r => r.RemindAt));
        }

        public async Task<List<ReminderDto>> GetForItemAsync(CalendarItemKind kind, Guid itemId)
        {
            await _access.RequireItemAsync(kind, itemId);

            var reminders = await FindMineAsync(kind, itemId);
            return await ToDtosAsync(reminders.OrderBy(r => r.MinutesBefore));
        }

        public async Task<ReminderDto> AddAsync(CalendarItemKind kind, Guid itemId, int minutesBefore)
        {
            // Seeing the item is enough: a reminder is personal, it changes nothing for anyone else.
            await _access.RequireItemAsync(kind, itemId);

            if (minutesBefore is < 0 or > MaxMinutesBefore)
                throw new ApiException("A reminder can be set from 0 minutes up to 4 weeks before.", 400);

            var mine = await FindMineAsync(kind, itemId);
            var reminder = mine.FirstOrDefault(r => r.MinutesBefore == minutesBefore);

            if (reminder == null)
            {
                if (mine.Count >= MaxPerItem)
                    throw new ApiException($"You can set up to {MaxPerItem} reminders on one item.", 400);

                reminder = new Reminder(_currentUser.RequireUserId(), kind, itemId, minutesBefore);
                reminder.ScheduleFor(await NextTimeAsync(kind, itemId));

                await Reminders.AddAsync(reminder);
                await _unitOfWork.CompleteAsync();
            }

            return (await ToDtosAsync(new[] { reminder })).First();
        }

        public async Task<bool> DeleteAsync(Guid reminderId)
        {
            var reminder = await FindMineAsync(reminderId);
            if (reminder == null) return false;

            Reminders.Delete(reminder);
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<ReminderDto?> SnoozeAsync(Guid reminderId, int minutes)
        {
            if (minutes is < 1 or > MaxSnoozeMinutes)
                throw new ApiException("A reminder can be snoozed from 1 minute up to 7 days.", 400);

            var reminder = await FindMineAsync(reminderId);
            if (reminder == null) return null;

            reminder.SnoozeUntil(DateTime.UtcNow.AddMinutes(minutes));
            await _unitOfWork.CompleteAsync();

            return (await ToDtosAsync(new[] { reminder })).First();
        }

        // ── For the task and event modules (nothing here saves) ───────────────

        public async Task StageForEventAsync(CalendarEvent calendarEvent, Guid userId, IEnumerable<int>? minutesBefore = null)
        {
            var wanted = minutesBefore?.ToList()
                ?? ((await _calendars.GetSettingsOfAsync(userId)).DefaultReminderMinutes is int byDefault
                    ? new List<int> { byDefault }
                    : new List<int>());

            var existing = (await Reminders.FindAsync(
                    r => r.UserId == userId && r.ItemKind == CalendarItemKind.Event && r.ItemId == calendarEvent.Id))
                .Select(r => r.MinutesBefore)
                .ToHashSet();

            var nextStart = ItemTimes.Next(calendarEvent, DateTime.UtcNow);

            foreach (var minutes in wanted.Where(m => m is >= 0 and <= MaxMinutesBefore).Distinct().Take(MaxPerItem))
            {
                if (!existing.Add(minutes)) continue;

                var reminder = new Reminder(userId, CalendarItemKind.Event, calendarEvent.Id, minutes);
                reminder.ScheduleFor(nextStart);
                await Reminders.AddAsync(reminder);
            }
        }

        public Task RescheduleAsync(CalendarEvent calendarEvent) =>
            RescheduleAsync(CalendarItemKind.Event, calendarEvent.Id, ItemTimes.Next(calendarEvent, DateTime.UtcNow));

        public Task RescheduleAsync(TaskItem task) =>
            RescheduleAsync(CalendarItemKind.Task, task.Id, ItemTimes.Next(task, DateTime.UtcNow));

        public async Task RemoveAsync(CalendarItemKind kind, Guid itemId, Guid? userId = null)
        {
            var reminders = await Reminders.FindAsync(
                r => r.ItemKind == kind && r.ItemId == itemId && (userId == null || r.UserId == userId));

            Reminders.DeleteRange(reminders);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private async Task RescheduleAsync(CalendarItemKind kind, Guid itemId, DateTime? nextTime)
        {
            var reminders = await Reminders.FindAsync(r => r.ItemKind == kind && r.ItemId == itemId);
            foreach (var reminder in reminders) reminder.ScheduleFor(nextTime);
        }

        /// <summary>The next due date (task) or start (event) of an item that is already saved.</summary>
        private async Task<DateTime?> NextTimeAsync(CalendarItemKind kind, Guid itemId)
        {
            var now = DateTime.UtcNow;

            if (kind == CalendarItemKind.Task)
            {
                var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(itemId);
                return task == null ? null : ItemTimes.Next(task, now);
            }

            var calendarEvent = await _unitOfWork.Repository<CalendarEvent>()
                .GetEntityWithSpec(new EventsWithDetailsSpec(itemId));
            return calendarEvent == null ? null : ItemTimes.Next(calendarEvent, now);
        }

        private async Task<List<Reminder>> FindMineAsync(CalendarItemKind kind, Guid itemId)
        {
            var userId = _currentUser.RequireUserId();
            return (await Reminders.FindAsync(
                r => r.UserId == userId && r.ItemKind == kind && r.ItemId == itemId)).ToList();
        }

        /// <summary>A reminder of somebody else does not exist as far as the caller is concerned.</summary>
        private async Task<Reminder?> FindMineAsync(Guid reminderId)
        {
            var userId = _currentUser.RequireUserId();
            var reminder = await Reminders.GetByIdAsync(reminderId);
            return reminder == null || reminder.UserId != userId ? null : reminder;
        }

        /// <summary>Maps reminders and adds the title of what each one is on: two queries, whatever the count.</summary>
        private async Task<List<ReminderDto>> ToDtosAsync(IEnumerable<Reminder> reminders)
        {
            var list = reminders.ToList();

            var titles = new Dictionary<(CalendarItemKind, Guid), string>();

            var taskIds = IdsOf(list, CalendarItemKind.Task);
            if (taskIds.Count > 0)
                foreach (var task in await _unitOfWork.Repository<TaskItem>()
                             .SelectAsync(t => taskIds.Contains(t.Id), t => new { t.Id, t.Title }))
                    titles[(CalendarItemKind.Task, task.Id)] = task.Title;

            var eventIds = IdsOf(list, CalendarItemKind.Event);
            if (eventIds.Count > 0)
                foreach (var calendarEvent in await _unitOfWork.Repository<CalendarEvent>()
                             .SelectAsync(e => eventIds.Contains(e.Id), e => new { e.Id, e.Title }))
                    titles[(CalendarItemKind.Event, calendarEvent.Id)] = calendarEvent.Title;

            return list.Select(r => new ReminderDto
            {
                Id = r.Id,
                ItemKind = r.ItemKind,
                ItemId = r.ItemId,
                ItemTitle = titles.GetValueOrDefault((r.ItemKind, r.ItemId)),
                MinutesBefore = r.MinutesBefore,
                RemindAt = r.RemindAt,
                LastSentAt = r.LastSentAt
            }).ToList();
        }

        private static List<Guid> IdsOf(IEnumerable<Reminder> reminders, CalendarItemKind kind) =>
            reminders.Where(r => r.ItemKind == kind).Select(r => r.ItemId).Distinct().ToList();
    }
}
