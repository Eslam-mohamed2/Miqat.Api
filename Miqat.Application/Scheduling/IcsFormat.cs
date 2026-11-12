using System.Globalization;
using System.Text;
using Miqat.Application.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Scheduling;

/// <summary>One event as it travels in an .ics file. Times are UTC.</summary>
/// <param name="SkippedStarts">Dates of a repeating event that do not happen.</param>
public sealed record IcsEvent(
    string? Uid, string Title, string? Description, string? Location,
    DateTime Start, DateTime End, bool IsAllDay, RecurrenceRule Rule,
    IReadOnlyList<DateTime>? SkippedStarts = null);

public sealed record IcsReadResult(List<IcsEvent> Events, int Skipped);

/// <summary>
/// Reads and writes iCalendar (.ics, RFC 5545) - the file format Google
/// Calendar, Outlook and Apple Calendar all export and import.
/// <para>
/// Covers what Miqat itself can store: one-off and all-day events, and events
/// repeating every N days, weeks, months or years. Two things a file may hold
/// are simplified on the way in: a weekly rule on several weekdays keeps only
/// the weekday of its first date, and changes to single dates of a series
/// (RECURRENCE-ID entries) are left out.
/// </para>
/// </summary>
public static class IcsFormat
{
    private const string DayFormat = "yyyyMMdd";
    private const string LocalFormat = "yyyyMMdd'T'HHmmss";
    private const string UtcFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const int MaxLineOctets = 75;

    // ── Writing ───────────────────────────────────────────────────────────────

    /// <param name="zone">Used for all-day events only: their dates are the days of this zone.</param>
    public static string Write(string calendarName, IEnumerable<IcsEvent> events, TimeZoneInfo zone, DateTime now)
    {
        var text = new StringBuilder();

        void Line(string name, string value) => AppendFolded(text, name + ":" + value);

        Line("BEGIN", "VCALENDAR");
        Line("VERSION", "2.0");
        Line("PRODID", "-//Miqat//Smart Calendar//EN");
        Line("CALSCALE", "GREGORIAN");
        Line("X-WR-CALNAME", Escape(calendarName));

        foreach (var item in events)
        {
            Line("BEGIN", "VEVENT");
            Line("UID", item.Uid ?? Guid.NewGuid().ToString());
            Line("DTSTAMP", Format(now, UtcFormat));

            if (item.IsAllDay)
            {
                Line("DTSTART;VALUE=DATE", Format(TimeZones.ToLocal(item.Start, zone), DayFormat));
                Line("DTEND;VALUE=DATE", Format(TimeZones.ToLocal(item.End, zone), DayFormat));
            }
            else
            {
                Line("DTSTART", Format(item.Start, UtcFormat));
                Line("DTEND", Format(item.End, UtcFormat));
            }

            Line("SUMMARY", Escape(item.Title));
            if (!string.IsNullOrWhiteSpace(item.Description)) Line("DESCRIPTION", Escape(item.Description));
            if (!string.IsNullOrWhiteSpace(item.Location)) Line("LOCATION", Escape(item.Location));

            if (item.Rule.Repeats)
            {
                var rule = $"FREQ={item.Rule.Pattern.ToString().ToUpperInvariant()};INTERVAL={Math.Max(1, item.Rule.Interval)}";
                if (item.Rule.Until.HasValue) rule += ";UNTIL=" + Format(item.Rule.Until.Value, UtcFormat);
                Line("RRULE", rule);

                foreach (var skipped in item.SkippedStarts ?? Array.Empty<DateTime>())
                    Line("EXDATE", Format(skipped, UtcFormat));
            }

            Line("END", "VEVENT");
        }

        Line("END", "VCALENDAR");
        return text.ToString();
    }

    /// <summary>Long lines continue on the next line after a space; the limit counts bytes, not characters.</summary>
    private static void AppendFolded(StringBuilder text, string line)
    {
        var octets = 0;

        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                text.Append("\r\n ");
                octets = 1;
            }

            text.Append(rune.ToString());
            octets += size;
        }

        text.Append("\r\n");
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");

    private static string Format(DateTime value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <param name="defaultZone">The zone of times that name none ("floating" times) and of all-day dates.</param>
    public static IcsReadResult Read(string text, TimeZoneInfo defaultZone)
    {
        var events = new List<IcsEvent>();
        var skipped = 0;
        Dictionary<string, Property>? current = null;
        var skippedDates = new List<Property>();
        var nested = 0;

        foreach (var line in Unfold(text))
        {
            var property = Property.Parse(line);
            if (property == null) continue;

            if (current == null)
            {
                if (property.Is("BEGIN", "VEVENT"))
                {
                    current = new Dictionary<string, Property>(StringComparer.OrdinalIgnoreCase);
                    skippedDates.Clear();
                    nested = 0;
                }
            }
            else if (property.Name.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                // An alarm (or another block) inside the event. Its lines must
                // not be mistaken for the event's own: an alarm has a
                // DESCRIPTION too.
                nested++;
            }
            else if (nested > 0)
            {
                if (property.Name.Equals("END", StringComparison.OrdinalIgnoreCase)) nested--;
            }
            else if (property.Is("END", "VEVENT"))
            {
                var parsed = ToEvent(current, skippedDates, defaultZone);
                if (parsed != null) events.Add(parsed);
                else skipped++;
                current = null;
            }
            else if (property.Name.Equals("EXDATE", StringComparison.OrdinalIgnoreCase))
            {
                skippedDates.Add(property);
            }
            else
            {
                current.TryAdd(property.Name, property);
            }
        }

        return new IcsReadResult(events, skipped);
    }

    private static IEnumerable<string> Unfold(string text)
    {
        string? pending = null;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                pending += raw[1..];
                continue;
            }

            if (!string.IsNullOrEmpty(pending)) yield return pending;
            pending = raw;
        }

        if (!string.IsNullOrEmpty(pending)) yield return pending;
    }

    /// <summary>Null for entries Miqat cannot place: no start, cancelled, or a changed single date of a series.</summary>
    private static IcsEvent? ToEvent(
        Dictionary<string, Property> properties, List<Property> skippedDates, TimeZoneInfo defaultZone)
    {
        if (properties.ContainsKey("RECURRENCE-ID")) return null;
        if (properties.TryGetValue("STATUS", out var status) && status.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return null;
        if (!properties.TryGetValue("DTSTART", out var startProperty)) return null;

        var start = ParseMoment(startProperty, defaultZone);
        if (start == null) return null;

        var isAllDay = startProperty.IsDate;
        var end = properties.TryGetValue("DTEND", out var endProperty) ? ParseMoment(endProperty, defaultZone) : null;
        end ??= properties.TryGetValue("DURATION", out var duration) ? start + ParseDuration(duration.Value) : null;

        // Without an end the format means "one day" for a date and "no length"
        // for a time; an event here needs a length, so a time gets an hour.
        if (end == null || end <= start)
            end = isAllDay ? TimeZones.ToUtc(TimeZones.ToLocal(start.Value, defaultZone).AddDays(1), defaultZone) : start.Value.AddHours(1);

        var title = properties.TryGetValue("SUMMARY", out var summary) ? Unescape(summary.Value).Trim() : string.Empty;

        return new IcsEvent(
            properties.GetValueOrDefault("UID")?.Value.Trim(),
            title.Length == 0 ? "(No title)" : title,
            properties.TryGetValue("DESCRIPTION", out var description) ? Unescape(description.Value) : null,
            properties.TryGetValue("LOCATION", out var location) ? Unescape(location.Value) : null,
            start.Value, end.Value, isAllDay,
            properties.TryGetValue("RRULE", out var rule) ? ParseRule(rule.Value, start.Value, defaultZone) : default,
            ParseSkippedDates(skippedDates, defaultZone));
    }

    /// <summary>EXDATE lines: one or several comma-separated moments each.</summary>
    private static List<DateTime> ParseSkippedDates(List<Property> lines, TimeZoneInfo defaultZone) =>
        lines
            .SelectMany(line => line.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => ParseMoment(line with { Value = value }, defaultZone)))
            .Where(moment => moment.HasValue)
            .Select(moment => moment!.Value)
            .ToList();

    /// <summary>
    /// A date ("20261112"), a UTC time ("...T090000Z"), a time in a named zone
    /// (TZID parameter) or a time with no zone at all, which is read in
    /// <paramref name="defaultZone"/>.
    /// </summary>
    private static DateTime? ParseMoment(Property property, TimeZoneInfo defaultZone)
    {
        var value = property.Value.Trim();

        if (property.IsDate)
            return TryParse(value, DayFormat, out var day) ? TimeZones.ToUtc(day, defaultZone) : null;

        if (value.EndsWith('Z'))
            return TryParse(value, UtcFormat, out var utc) ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : null;

        if (!TryParse(value, LocalFormat, out var local)) return null;

        // A zone name this server does not know (some programs invent their
        // own) is read as the zone of the user rather than as UTC.
        var zone = property.Parameters.TryGetValue("TZID", out var zoneId)
            ? TimeZones.Find(zoneId) ?? defaultZone
            : defaultZone;
        return TimeZones.ToUtc(local, zone);
    }

    private static RecurrenceRule ParseRule(string value, DateTime start, TimeZoneInfo zone)
    {
        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim().ToUpperInvariant(), pair => pair[1].Trim());

        // FREQ=WEEKLY matches the enum by name; anything finer than a day
        // (HOURLY, ...) is not something a calendar here can repeat.
        if (!parts.TryGetValue("FREQ", out var frequency) ||
            !EnumText.TryParseDefined<RecurrencePattern>(frequency, out var pattern) ||
            pattern == RecurrencePattern.None)
            return default;

        var interval = parts.TryGetValue("INTERVAL", out var every) && int.TryParse(every, out var n) && n > 0 ? n : 1;
        var rule = new RecurrenceRule(pattern, interval);

        if (parts.TryGetValue("UNTIL", out var until))
        {
            var last = until.EndsWith('Z') && TryParse(until, UtcFormat, out var utc) ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                : TryParse(until, LocalFormat, out var local) ? TimeZones.ToUtc(local, zone)
                : TryParse(until, DayFormat, out var day) ? TimeZones.ToUtc(day, zone)
                : (DateTime?)null;

            return rule with { Until = last };
        }

        // "Repeat 10 times" becomes "repeat until the 10th date".
        if (parts.TryGetValue("COUNT", out var count) && int.TryParse(count, out var times) && times > 0)
        {
            var last = start;
            for (var i = 1; i < Math.Min(times, Recurrence.MaxOccurrences); i++)
                last = Recurrence.NextStart(start, rule, zone, last) ?? last;

            return rule with { Until = last };
        }

        return rule;
    }

    /// <summary>ISO 8601 lengths as iCalendar writes them: P1D, PT1H30M, P1W.</summary>
    private static TimeSpan ParseDuration(string value)
    {
        var total = TimeSpan.Zero;
        var digits = new StringBuilder();
        var inTime = false;

        foreach (var c in value.Trim().ToUpperInvariant())
        {
            if (char.IsDigit(c)) { digits.Append(c); continue; }
            if (c == 'T') { inTime = true; continue; }
            if (digits.Length == 0 || !int.TryParse(digits.ToString(), out var amount)) continue;
            digits.Clear();

            total += c switch
            {
                'W' => TimeSpan.FromDays(7 * amount),
                'D' => TimeSpan.FromDays(amount),
                'H' => TimeSpan.FromHours(amount),
                'M' when inTime => TimeSpan.FromMinutes(amount),
                'S' => TimeSpan.FromSeconds(amount),
                _ => TimeSpan.Zero
            };
        }

        return total;
    }

    private static string Unescape(string value)
    {
        var text = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                text.Append(value[i]);
                continue;
            }

            var next = value[++i];
            text.Append(next is 'n' or 'N' ? '\n' : next);
        }

        return text.ToString();
    }

    private static bool TryParse(string value, string format, out DateTime result) =>
        DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>One line of the file: NAME;PARAMETER=value:VALUE.</summary>
    private sealed record Property(string Name, Dictionary<string, string> Parameters, string Value)
    {
        public bool IsDate =>
            Parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase);

        public bool Is(string name, string value) =>
            Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);

        public static Property? Parse(string line)
        {
            // The value starts at the first colon that is not inside a quoted parameter.
            var colon = -1;
            var quoted = false;
            for (var i = 0; i < line.Length && colon < 0; i++)
            {
                if (line[i] == '"') quoted = !quoted;
                else if (line[i] == ':' && !quoted) colon = i;
            }

            if (colon <= 0) return null;

            var head = line[..colon].Split(';');
            var parameters = head.Skip(1)
                .Select(part => part.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .GroupBy(pair => pair[0].Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First()[1].Trim('"'), StringComparer.OrdinalIgnoreCase);

            return new Property(head[0].Trim(), parameters, line[(colon + 1)..]);
        }
    }
}
