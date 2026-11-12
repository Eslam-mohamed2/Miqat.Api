using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Scheduling;
using Miqat.Application.Specifications.Events;
using Miqat.Application.Specifications.Tasks;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Four queries whatever the range: the user, their projects, the events
    /// and the tasks. Repeating items are unfolded in memory.
    /// </remarks>
    public class CalendarFeedService : ICalendarFeedService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly PeopleDirectory _people;

        public CalendarFeedService(IUnitOfWork unitOfWork, ICurrentUserService currentUser, PeopleDirectory people)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _people = people;
        }

        public async Task<List<CalendarItemDto>> GetFeedAsync(
            DateRange range, IReadOnlyCollection<Guid>? calendarIds, bool includeTasks)
        {
            var userId = _currentUser.RequireUserId();
            var onlyCalendars = calendarIds is { Count: > 0 } ? calendarIds.ToHashSet() : null;

            var items = await GetEventItemsAsync(userId, range, onlyCalendars);

            // Tasks are not in any calendar, so asking for specific calendars leaves them out.
            if (includeTasks && onlyCalendars == null)
                items.AddRange(await GetTaskItemsAsync(userId, range));

            return items.OrderBy(i => i.Start).ThenBy(i => i.Title).ToList();
        }

        private async Task<List<CalendarItemDto>> GetEventItemsAsync(Guid userId, DateRange range, HashSet<Guid>? onlyCalendars)
        {
            var projectIds = await _people.GetProjectIdsAsync(userId);
            var events = await _unitOfWork.Repository<CalendarEvent>()
                .ListAsync(new EventsInRangeSpec(new[] { userId }, projectIds, range));

            return events
                .Where(e => onlyCalendars?.Contains(e.CalendarId)
                            // A calendar the user hid keeps its events out of their own view.
                            ?? (e.OwnerId != userId || e.Calendar is not { IsVisible: false }))
                .SelectMany(e => EventDates.In(e, range).Select(date => ToItem(date, userId)))
                .ToList();
        }

        private async Task<List<CalendarItemDto>> GetTaskItemsAsync(Guid userId, DateRange range)
        {
            var tasks = await _unitOfWork.Repository<TaskItem>().ListAsync(new TasksOnCalendarSpec(userId, range));
            if (tasks.Count == 0) return new List<CalendarItemDto>();

            // The dates of a repeating task follow the wall clock of the person looking.
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            var zone = TimeZones.Resolve(user?.TimeZone);

            return tasks.SelectMany(task => DatesOf(task, zone, range).Select(date => ToItem(task, date))).ToList();
        }

        /// <summary>
        /// A task sits on its due date (from its start date, when it has one).
        /// An open repeating task also shows its coming dates; once it is
        /// finished the next one is a task of its own (see TaskService).
        /// </summary>
        private static IEnumerable<Occurrence> DatesOf(TaskItem task, TimeZoneInfo zone, DateRange range)
        {
            var end = task.DueDate!.Value;
            var start = task.StartDate ?? end;
            var rule = task.IsOpen ? RecurrenceRule.Of(task) : default;

            return Recurrence.Between(start, end, rule, zone, range);
        }

        private static CalendarItemDto ToItem(EventDate date, Guid userId)
        {
            var calendarEvent = date.Event;
            var isOwner = calendarEvent.OwnerId == userId;

            return new CalendarItemDto
            {
                Kind = CalendarItemKind.Event,
                Id = calendarEvent.Id,
                Title = date.Title,
                Start = date.Start,
                End = date.End,
                IsAllDay = calendarEvent.IsAllDay,
                Location = date.Location,
                IsRecurring = calendarEvent.IsRecurring,
                OccurrenceStart = date.OccurrenceStart,
                Color = calendarEvent.Calendar?.Color,
                CalendarId = isOwner ? calendarEvent.CalendarId : null,
                CalendarName = isOwner ? calendarEvent.Calendar?.Name : null,
                GroupId = calendarEvent.GroupId,
                GroupName = calendarEvent.Group?.Name,
                IsOwner = isOwner,
                MyResponse = calendarEvent.Attendees.FirstOrDefault(a => a.UserId == userId)?.Status,
                AttendeeCount = calendarEvent.Attendees.Count
            };
        }

        private static CalendarItemDto ToItem(TaskItem task, Occurrence date)
        {
            var isFirstDate = date.End == task.DueDate;

            return new CalendarItemDto
            {
                Kind = CalendarItemKind.Task,
                Id = task.Id,
                Title = task.Title,
                Start = date.Start,
                End = date.End,
                IsRecurring = task.Recurrence != RecurrencePattern.None,
                OccurrenceStart = task.Recurrence != RecurrencePattern.None ? date.Start : null,
                Color = task.Group?.Color,
                GroupId = task.GroupId,
                GroupName = task.Group?.Name,
                Status = task.Status.ToString(),
                Priority = task.Priority.ToString(),

                // Only the real date can be late; the coming ones have not happened yet.
                IsOverdue = isFirstDate && TaskMapper.IsOverdue(task)
            };
        }
    }
}
