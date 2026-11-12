using Miqat.Domain.Enumerations;

namespace Miqat.Application.Modules;

public class CalendarDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    /// <summary>The calendar new events go to when none is chosen. It cannot be deleted.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Hidden calendars are left out of the calendar feed.</summary>
    public bool IsVisible { get; set; }

    public int EventCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Create / update payload. On update, values that are left out keep what is stored.</summary>
public class SaveCalendarDto
{
    public string? Name { get; set; }
    public string? Color { get; set; }
    public bool? IsVisible { get; set; }
}

/// <summary>Calendar preferences of the caller. PUT replaces all of them.</summary>
public class CalendarSettingsDto
{
    public DayOfWeek WeekStartsOn { get; set; }
    public CalendarView DefaultView { get; set; }
    public bool Use24HourClock { get; set; }

    /// <summary>"HH:mm" on the wall clock of the user, for example "09:00".</summary>
    public string WorkDayStart { get; set; } = "09:00";
    public string WorkDayEnd { get; set; } = "17:00";
    public List<DayOfWeek> WorkDays { get; set; } = new();

    public int DefaultEventMinutes { get; set; } = 60;

    /// <summary>Minutes before the start that every new event reminds; null for no reminder.</summary>
    public int? DefaultReminderMinutes { get; set; }

    /// <summary>Read-only here: the time zone is part of the profile (PUT /api/User/me).</summary>
    public string? TimeZone { get; set; }
}

/// <summary>
/// One thing on the calendar on one date: an event, a task, or a single date
/// of a repeating one. This is what GET /api/Calendar/feed returns.
/// </summary>
public class CalendarItemDto
{
    public CalendarItemKind Kind { get; set; }

    /// <summary>Id of the event or task. A repeating item has the same id on every date.</summary>
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }

    public bool IsRecurring { get; set; }

    /// <summary>
    /// For a repeating item: when the series placed this date. Send it back to
    /// change or skip only this date (PUT /api/Event/{id}/occurrences).
    /// </summary>
    public DateTime? OccurrenceStart { get; set; }

    public string? Color { get; set; }
    public Guid? CalendarId { get; set; }
    public string? CalendarName { get; set; }
    public Guid? GroupId { get; set; }
    public string? GroupName { get; set; }

    // ── Events only ──────────────────────────────────────────────────────────
    public bool IsOwner { get; set; }

    /// <summary>The answer of the caller when they are invited; null for their own events.</summary>
    public AttendeeStatus? MyResponse { get; set; }
    public int AttendeeCount { get; set; }

    // ── Tasks only ───────────────────────────────────────────────────────────
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public bool IsOverdue { get; set; }
}

public class ImportResultDto
{
    public Guid CalendarId { get; set; }
    public int Imported { get; set; }

    /// <summary>Events that were already imported before and were brought up to date.</summary>
    public int Updated { get; set; }

    /// <summary>Entries of the file that could not be read (no start date, for example).</summary>
    public int Skipped { get; set; }
}
