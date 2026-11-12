using Miqat.Application.Common;
using Miqat.Domain.Entities;

namespace Miqat.Application.Scheduling;

/// <summary>
/// "When does this happen?" for the two things reminders can be set on. A
/// task happens at its due date, an event at each of its starts.
/// </summary>
public static class ItemTimes
{
    /// <summary>How far ahead "the date that is on now or comes next" is looked for.</summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(366);

    /// <summary>The due date, while the task is open and that date is still ahead.</summary>
    public static DateTime? Next(TaskItem task, DateTime after) =>
        IsLive(task) && task.DueDate > after ? task.DueDate : null;

    public static DateTime? Next(CalendarEvent calendarEvent, DateTime after) =>
        calendarEvent.IsDeleted ? null : EventDates.NextStart(calendarEvent, after);

    /// <summary>The due date of an open task, even when it has just passed.</summary>
    public static DateTime? Current(TaskItem task) =>
        IsLive(task) ? task.DueDate : null;

    /// <summary>The start of the date that is on right now, or of the next one.</summary>
    public static DateTime? Current(CalendarEvent calendarEvent, DateTime now) =>
        calendarEvent.IsDeleted
            ? null
            : EventDates.In(calendarEvent, new DateRange(now, now + Horizon))
                .OrderBy(date => date.Start)
                .Select(date => (DateTime?)date.Start)
                .FirstOrDefault();

    private static bool IsLive(TaskItem task) => !task.IsDeleted && task.IsOpen;
}
