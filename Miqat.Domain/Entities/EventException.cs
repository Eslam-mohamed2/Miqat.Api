using Miqat.Domain.Common;

namespace Miqat.Domain.Entities;

/// <summary>
/// One date of a repeating event that differs from the series: either skipped
/// (<see cref="IsCancelled"/>) or changed (the values set here replace the
/// ones the series would have given that date).
/// </summary>
public class EventException : BaseEntity
{
    public Guid EventId { get; private set; }
    public virtual CalendarEvent Event { get; private set; } = null!;

    /// <summary>When the series would have started this occurrence (UTC). Says which date this is about.</summary>
    public DateTime OccurrenceStart { get; private set; }

    public bool IsCancelled { get; private set; }
    public DateTime? StartsAt { get; private set; }
    public DateTime? EndsAt { get; private set; }
    public string? Title { get; private set; }
    public string? Location { get; private set; }

    public EventException(Guid eventId, DateTime occurrenceStart)
    {
        EventId = eventId;
        OccurrenceStart = occurrenceStart;
    }

    private EventException() { }

    public void Cancel()
    {
        IsCancelled = true;
        StartsAt = EndsAt = null;
        Title = Location = null;
    }

    public void Change(DateTime startsAt, DateTime endsAt, string? title, string? location)
    {
        if (endsAt <= startsAt)
            throw new ArgumentException("An event must end after it starts.", nameof(endsAt));

        IsCancelled = false;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
    }
}
