using Miqat.Domain.Enumerations;

namespace Miqat.Application.Modules;

public class EventDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }

    public RecurrencePattern Recurrence { get; set; }
    public int RecurrenceInterval { get; set; } = 1;
    public DateTime? RecurrenceEndDate { get; set; }

    public Guid CalendarId { get; set; }
    public string? CalendarName { get; set; }
    public string? Color { get; set; }

    public Guid? GroupId { get; set; }
    public string? GroupName { get; set; }

    public Guid OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerProfilePictureUrl { get; set; }

    /// <summary>True when the caller created the event and may change it.</summary>
    public bool IsOwner { get; set; }

    /// <summary>The answer of the caller when they are invited; null for their own events.</summary>
    public AttendeeStatus? MyResponse { get; set; }

    public List<AttendeeDto> Attendees { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AttendeeDto
{
    public Guid UserId { get; set; }
    public string? FullName { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public AttendeeStatus Status { get; set; }
    public DateTime? RespondedAt { get; set; }
}

/// <summary>Create / update payload of an event. PUT replaces the whole event.</summary>
public class SaveEventDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }

    public RecurrencePattern Recurrence { get; set; } = RecurrencePattern.None;
    public int RecurrenceInterval { get; set; } = 1;
    public DateTime? RecurrenceEndDate { get; set; }

    /// <summary>Left out: the default calendar of the caller.</summary>
    public Guid? CalendarId { get; set; }

    /// <summary>Optional project; its members can then see the event.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Create only: people to invite straight away.</summary>
    public List<Guid>? AttendeeIds { get; set; }

    /// <summary>
    /// Create only: "minutes before" reminders for the caller. Left out, the
    /// default reminder from the calendar settings is added.
    /// </summary>
    public List<int>? ReminderMinutes { get; set; }
}

/// <summary>Changes or skips one date of a repeating event.</summary>
public class SaveOccurrenceDto
{
    /// <summary>Which date: the occurrenceStart value the calendar feed gave it.</summary>
    public DateTime OccurrenceStart { get; set; }

    /// <summary>True removes this date from the series; the other values are then ignored.</summary>
    public bool Cancelled { get; set; }

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? Title { get; set; }
    public string? Location { get; set; }
}

public class InviteAttendeesDto
{
    public List<Guid> UserIds { get; set; } = new();
}

public class RespondToEventDto
{
    public AttendeeStatus Status { get; set; }
}
