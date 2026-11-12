using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities;

/// <summary>A person invited to an event, and their answer.</summary>
public class EventAttendee : BaseEntity
{
    public Guid EventId { get; private set; }
    public virtual CalendarEvent Event { get; private set; } = null!;

    public Guid UserId { get; private set; }
    public virtual User User { get; private set; } = null!;

    public AttendeeStatus Status { get; private set; } = AttendeeStatus.Pending;
    public DateTime? RespondedAt { get; private set; }

    public EventAttendee(Guid eventId, Guid userId)
    {
        EventId = eventId;
        UserId = userId;
    }

    private EventAttendee() { }

    public void Respond(AttendeeStatus status)
    {
        Status = status;
        RespondedAt = status == AttendeeStatus.Pending ? null : DateTime.UtcNow;
    }
}
