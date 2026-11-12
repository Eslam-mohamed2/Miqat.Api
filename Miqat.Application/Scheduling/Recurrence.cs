using Miqat.Application.Common;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Scheduling;

/// <summary>How a series repeats: every <see cref="Interval"/> days / weeks / months / years, up to and including the day of <see cref="Until"/>.</summary>
public readonly record struct RecurrenceRule(RecurrencePattern Pattern, int Interval = 1, DateTime? Until = null)
{
    public bool Repeats => Pattern != RecurrencePattern.None;

    public static RecurrenceRule Of(CalendarEvent calendarEvent) =>
        new(calendarEvent.Recurrence, calendarEvent.RecurrenceInterval, calendarEvent.RecurrenceEndDate);

    public static RecurrenceRule Of(TaskItem task) =>
        new(task.Recurrence, 1, task.RecurrenceEndDate);
}

/// <summary>One date of a series, in UTC.</summary>
public readonly record struct Occurrence(DateTime Start, DateTime End);

/// <summary>
/// Works out the dates of a repeating task or event.
/// <para>
/// A series is stored once (its first start and end plus a rule), and its
/// dates are calculated when they are needed. Two things make that correct:
/// every date is counted from the first one ("the 31st" stays the 31st after a
/// short month), and the steps are taken on the wall clock of the owner (a
/// 9:00 meeting stays at 9:00 when daylight saving starts or ends).
/// </para>
/// </summary>
public static class Recurrence
{
    /// <summary>Upper limit of dates read from one series in one call; a safety net, not a product rule.</summary>
    public const int MaxOccurrences = 750;

    /// <summary>The occurrences that touch the range, earliest first.</summary>
    public static IEnumerable<Occurrence> Between(
        DateTime firstStart, DateTime firstEnd, RecurrenceRule rule, TimeZoneInfo zone, DateRange range)
    {
        // The length is kept on the wall clock too, so an all-day event still
        // ends at midnight on the day the clocks change.
        var length = TimeZones.ToLocal(firstEnd, zone) - TimeZones.ToLocal(firstStart, zone);

        foreach (var (localStart, start) in Series(firstStart, rule, zone, range.From - length))
        {
            if (start >= range.To) yield break;

            var end = TimeZones.ToUtc(localStart + length, zone);
            if (range.Overlaps(start, end)) yield return new Occurrence(start, end);
        }
    }

    /// <summary>The first start strictly after <paramref name="after"/>, or null when the series is over.</summary>
    public static DateTime? NextStart(DateTime firstStart, RecurrenceRule rule, TimeZoneInfo zone, DateTime after)
    {
        foreach (var (_, start) in Series(firstStart, rule, zone, after))
            if (start > after) return start;

        return null;
    }

    /// <summary>
    /// The starts of the series as (wall clock, UTC) pairs, beginning a little
    /// before <paramref name="notBefore"/> rather than at the first date - a
    /// daily event created years ago does not get walked through day by day.
    /// </summary>
    private static IEnumerable<(DateTime Local, DateTime Utc)> Series(
        DateTime firstStart, RecurrenceRule rule, TimeZoneInfo zone, DateTime notBefore)
    {
        var anchor = TimeZones.ToLocal(firstStart, zone);

        if (!rule.Repeats)
        {
            yield return (anchor, firstStart);
            yield break;
        }

        var lastDay = rule.Until.HasValue ? TimeZones.ToLocal(rule.Until.Value, zone).Date : (DateTime?)null;
        var searchFrom = notBefore > firstStart ? TimeZones.ToLocal(notBefore, zone) : anchor;
        var index = IndexJustBefore(anchor, rule, searchFrom);

        for (var steps = 0; steps < MaxOccurrences; steps++, index++)
        {
            var local = Step(anchor, rule, index);
            if (lastDay.HasValue && local.Date > lastDay.Value) yield break;

            yield return (local, TimeZones.ToUtc(local, zone));
        }
    }

    private static DateTime Step(DateTime anchor, RecurrenceRule rule, int index)
    {
        var count = index * Math.Max(1, rule.Interval);

        return rule.Pattern switch
        {
            RecurrencePattern.Daily => anchor.AddDays(count),
            RecurrencePattern.Weekly => anchor.AddDays(7 * count),
            RecurrencePattern.Monthly => anchor.AddMonths(count),
            RecurrencePattern.Yearly => anchor.AddYears(count),
            _ => anchor
        };
    }

    /// <summary>
    /// An index whose date is at or before <paramref name="searchFrom"/>.
    /// Deliberately rounds down (31-day months, 366-day years): starting a step
    /// or two early costs nothing, starting late would lose a date.
    /// </summary>
    private static int IndexJustBefore(DateTime anchor, RecurrenceRule rule, DateTime searchFrom)
    {
        if (searchFrom <= anchor) return 0;

        var daysPerStep = Math.Max(1, rule.Interval) * rule.Pattern switch
        {
            RecurrencePattern.Daily => 1,
            RecurrencePattern.Weekly => 7,
            RecurrencePattern.Monthly => 31,
            RecurrencePattern.Yearly => 366,
            _ => 1
        };

        return Math.Max(0, (int)((searchFrom - anchor).TotalDays / daysPerStep) - 1);
    }
}
