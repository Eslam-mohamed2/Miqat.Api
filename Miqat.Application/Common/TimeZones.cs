namespace Miqat.Application.Common;

/// <summary>
/// Everything is stored in UTC; these convert to and from the wall clock of a
/// user, in one place, so the daylight-saving edge cases are handled once.
/// </summary>
public static class TimeZones
{
    /// <summary>The IANA zone of a user ("Africa/Cairo"), or UTC when it is missing or unknown.</summary>
    public static TimeZoneInfo Resolve(string? id) => Find(id) ?? TimeZoneInfo.Utc;

    /// <summary>The zone with this name; null when it is missing or unknown.</summary>
    public static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>
    /// Wall-clock time to UTC. When clocks go forward an hour of the day does
    /// not exist (in Africa/Cairo that hour is midnight); such a time is moved
    /// to the first moment that does exist instead of throwing.
    /// </summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wallClock)) wallClock = wallClock.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(wallClock, zone);
    }

    /// <summary>The moment (UTC) at which the calendar day containing <paramref name="utc"/> began for this zone.</summary>
    public static DateTime StartOfDay(DateTime utc, TimeZoneInfo zone) =>
        ToUtc(ToLocal(utc, zone).Date, zone);
}
