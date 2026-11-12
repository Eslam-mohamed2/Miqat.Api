using System.Globalization;

namespace Miqat.Application.Common;

/// <summary>
/// Times of day on the wire ("09:00") and in storage (minutes after midnight).
/// "24:00" is accepted as the end of the day.
/// </summary>
public static class ClockTime
{
    private const int MinutesPerDay = 24 * 60;
    private const string EndOfDay = "24:00";

    /// <summary>Null when the text is not a time of day.</summary>
    public static int? ToMinutes(string? text)
    {
        var trimmed = text?.Trim();
        if (trimmed == EndOfDay) return MinutesPerDay;

        return TimeOnly.TryParseExact(trimmed, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.Hour * 60 + time.Minute
            : null;
    }

    public static string FromMinutes(int minutes) =>
        minutes >= MinutesPerDay ? EndOfDay : $"{minutes / 60:00}:{minutes % 60:00}";

    /// <summary>
    /// "in 10 minutes", "in about 3 hours", "in about 2 days". Relative on
    /// purpose: the server does not know the time zone of each reader, and
    /// "in about 3 hours" is right for everyone.
    /// </summary>
    public static string Until(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1)) return "now";

        if (left < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Round(left.TotalMinutes);
            return minutes == 1 ? "in 1 minute" : $"in {minutes} minutes";
        }

        if (left < TimeSpan.FromDays(1))
        {
            var hours = (int)Math.Round(left.TotalHours);
            return hours == 1 ? "in about an hour" : $"in about {hours} hours";
        }

        var days = (int)Math.Round(left.TotalDays);
        return days == 1 ? "in about a day" : $"in about {days} days";
    }
}
