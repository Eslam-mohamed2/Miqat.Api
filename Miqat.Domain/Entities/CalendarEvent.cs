using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities;

/// <summary>
/// A meeting or appointment: something that happens from one moment to
/// another. (A task is something to finish by a date; that is the difference.)
/// <para>
/// A repeating event is stored once. Its occurrences are worked out when the
/// calendar is read, and <see cref="Exceptions"/> holds the single dates that
/// were moved or skipped.
/// </para>
/// </summary>
public class CalendarEvent : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }

    /// <summary>UTC. For an all-day event: midnight of the first day in the time zone of the owner.</summary>
    public DateTime StartsAt { get; private set; }

    /// <summary>UTC, always after <see cref="StartsAt"/>.</summary>
    public DateTime EndsAt { get; private set; }

    public bool IsAllDay { get; private set; }

    public RecurrencePattern Recurrence { get; private set; } = RecurrencePattern.None;

    /// <summary>Every N days / weeks / months / years. 1 unless the event repeats.</summary>
    public int RecurrenceInterval { get; private set; } = 1;

    /// <summary>Last moment an occurrence may start; null repeats forever.</summary>
    public DateTime? RecurrenceEndDate { get; private set; }

    /// <summary>
    /// The UID the event had in the .ics file it was imported from, so that
    /// importing the same file twice updates the event instead of copying it.
    /// </summary>
    public string? ExternalUid { get; set; }

    public Guid OwnerId { get; private set; }
    public virtual User Owner { get; private set; } = null!;

    public Guid CalendarId { get; set; }
    public virtual UserCalendar Calendar { get; set; } = null!;

    /// <summary>Optional project. Its members can see the event.</summary>
    public Guid? GroupId { get; set; }
    public virtual Group? Group { get; set; }

    public virtual ICollection<EventAttendee> Attendees { get; private set; } = new List<EventAttendee>();
    public virtual ICollection<EventException> Exceptions { get; private set; } = new List<EventException>();

    public bool IsRecurring => Recurrence != RecurrencePattern.None;
    public TimeSpan Duration => EndsAt - StartsAt;

    /// <param name="owner">
    /// The whole user, not just the id: a repeating event is unfolded on the
    /// wall clock of its owner, so their time zone is needed from the start.
    /// </param>
    public CalendarEvent(string title, DateTime startsAt, DateTime endsAt, User owner, Guid calendarId, bool isAllDay = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));

        Title = title.Trim();
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        OwnerId = owner.Id;
        CalendarId = calendarId;
        Reschedule(startsAt, endsAt, isAllDay);
    }

    private CalendarEvent() { }

    public void Reschedule(DateTime startsAt, DateTime endsAt, bool isAllDay)
    {
        if (endsAt <= startsAt)
            throw new ArgumentException("An event must end after it starts.", nameof(endsAt));

        StartsAt = startsAt;
        EndsAt = endsAt;
        IsAllDay = isAllDay;
    }

    /// <summary>Sets how the event repeats. <see cref="RecurrencePattern.None"/> clears the rest.</summary>
    public void Repeat(RecurrencePattern pattern, int interval = 1, DateTime? until = null)
    {
        var repeats = pattern != RecurrencePattern.None;

        Recurrence = pattern;
        RecurrenceInterval = repeats ? Math.Max(1, interval) : 1;
        RecurrenceEndDate = repeats ? until : null;
    }
}
