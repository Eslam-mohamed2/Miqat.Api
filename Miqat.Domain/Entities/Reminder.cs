using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities;

/// <summary>
/// "Remind me N minutes before" on a task or an event, for one person.
/// <para>
/// <see cref="RemindAt"/> is worked out when the reminder is saved and again
/// whenever the item moves, so the background job only asks the database for
/// "reminders whose time has come" instead of reading every task and event.
/// </para>
/// </summary>
public class Reminder : BaseEntity
{
    public Guid UserId { get; private set; }
    public virtual User User { get; private set; } = null!;

    public CalendarItemKind ItemKind { get; private set; }
    public Guid ItemId { get; private set; }

    public int MinutesBefore { get; private set; }

    /// <summary>
    /// The start (event) or due time (task) the reminder is currently about;
    /// for a repeating event, the date it will announce next. Null when
    /// nothing is ahead.
    /// </summary>
    public DateTime? ItemTime { get; private set; }

    /// <summary>
    /// When it fires (UTC): <see cref="ItemTime"/> minus the minutes, or later
    /// when it was snoozed. Null when nothing is ahead.
    /// </summary>
    public DateTime? RemindAt { get; private set; }

    public DateTime? LastSentAt { get; private set; }

    public Reminder(Guid userId, CalendarItemKind itemKind, Guid itemId, int minutesBefore)
    {
        if (minutesBefore < 0)
            throw new ArgumentException("Minutes cannot be negative.", nameof(minutesBefore));

        UserId = userId;
        ItemKind = itemKind;
        ItemId = itemId;
        MinutesBefore = minutesBefore;
    }

    private Reminder() { }

    /// <summary>Points the reminder at a start (or due) time; null switches it off.</summary>
    public void ScheduleFor(DateTime? itemTime)
    {
        ItemTime = itemTime;
        RemindAt = itemTime?.AddMinutes(-MinutesBefore);
    }

    public void MarkSent(DateTime now, DateTime? nextItemTime)
    {
        LastSentAt = now;
        ScheduleFor(nextItemTime);
    }

    /// <summary>True when <see cref="RemindAt"/> is a snooze time rather than "the minutes before".</summary>
    public bool IsSnoozed =>
        RemindAt.HasValue && RemindAt != ItemTime?.AddMinutes(-MinutesBefore);

    /// <summary>Fires again later, still about the same date of the item.</summary>
    public void SnoozeUntil(DateTime remindAt) => RemindAt = remindAt;
}
