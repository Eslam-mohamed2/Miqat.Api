namespace Miqat.Application.Modules;

/// <summary>A stretch of time, in UTC.</summary>
public class TimeSlotDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

/// <summary>
/// When one person is busy. Only the times are shared, never what the events
/// are, so asking for the availability of someone tells nothing about their plans.
/// </summary>
public class UserAvailabilityDto
{
    public Guid UserId { get; set; }
    public string? FullName { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public List<TimeSlotDto> Busy { get; set; } = new();
}

public class SuggestTimesDto
{
    /// <summary>The people to meet. The caller is always included.</summary>
    public List<Guid> AttendeeIds { get; set; } = new();

    public int DurationMinutes { get; set; } = 60;

    /// <summary>Left out: from now.</summary>
    public DateTime? From { get; set; }

    /// <summary>Left out: two weeks after <see cref="From"/>.</summary>
    public DateTime? To { get; set; }

    public int MaxResults { get; set; } = 5;

    /// <summary>Only offer times inside the working hours of every person (their calendar settings).</summary>
    public bool WorkingHoursOnly { get; set; } = true;
}
