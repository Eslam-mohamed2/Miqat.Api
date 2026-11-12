using Miqat.Application.Common;
using Miqat.Domain.Entities;

namespace Miqat.Application.Scheduling;

/// <summary>
/// One date of an event as it really happens: where the series put it
/// (<see cref="OccurrenceStart"/>, null for a one-off event) and what it looks
/// like after any change made to just that date.
/// </summary>
public sealed record EventDate(
    CalendarEvent Event, DateTime Start, DateTime End, DateTime? OccurrenceStart, string Title, string? Location);

/// <summary>
/// Turns a stored event into the dates it happens on: the series is unfolded
/// in the time zone of its owner, skipped dates are left out and moved dates
/// appear where they were moved to. The event must have been loaded with its
/// owner and its exceptions.
/// </summary>
public static class EventDates
{
    public static IEnumerable<EventDate> In(CalendarEvent calendarEvent, DateRange range)
    {
        if (!calendarEvent.IsRecurring)
        {
            if (range.Overlaps(calendarEvent.StartsAt, calendarEvent.EndsAt))
                yield return Plain(calendarEvent, calendarEvent.StartsAt, calendarEvent.EndsAt, occurrenceStart: null);
            yield break;
        }

        var changed = calendarEvent.Exceptions.Select(x => x.OccurrenceStart).ToHashSet();

        foreach (var occurrence in Series(calendarEvent, range))
            if (!changed.Contains(occurrence.Start))
                yield return Plain(calendarEvent, occurrence.Start, occurrence.End, occurrence.Start);

        // A moved date counts where it was moved to, which may be in the range
        // although its original place is not.
        foreach (var moved in calendarEvent.Exceptions.Where(x => !x.IsCancelled && x.StartsAt.HasValue))
            if (range.Overlaps(moved.StartsAt!.Value, moved.EndsAt!.Value))
                yield return new EventDate(
                    calendarEvent, moved.StartsAt.Value, moved.EndsAt.Value, moved.OccurrenceStart,
                    moved.Title ?? calendarEvent.Title, moved.Location ?? calendarEvent.Location);
    }

    /// <summary>True when the series itself puts a date at exactly this moment.</summary>
    public static bool IsSeriesDate(CalendarEvent calendarEvent, DateTime occurrenceStart) =>
        calendarEvent.IsRecurring &&
        Series(calendarEvent, new DateRange(occurrenceStart, occurrenceStart.AddTicks(1)))
            .Any(o => o.Start == occurrenceStart);

    /// <summary>The first start after <paramref name="after"/>, or null when nothing is ahead.</summary>
    public static DateTime? NextStart(CalendarEvent calendarEvent, DateTime after)
    {
        if (!calendarEvent.IsRecurring)
            return calendarEvent.StartsAt > after ? calendarEvent.StartsAt : null;

        var exceptions = calendarEvent.Exceptions.ToDictionary(x => x.OccurrenceStart);
        var zone = ZoneOf(calendarEvent);
        var rule = RecurrenceRule.Of(calendarEvent);

        // The next date the series gives that was neither skipped nor moved...
        DateTime? next = null;
        var cursor = after;
        for (var guard = 0; guard <= exceptions.Count; guard++)
        {
            var candidate = Recurrence.NextStart(calendarEvent.StartsAt, rule, zone, cursor);
            if (candidate == null) break;
            if (!exceptions.ContainsKey(candidate.Value)) { next = candidate; break; }
            cursor = candidate.Value;
        }

        // ...unless a moved date comes first.
        var moved = exceptions.Values
            .Where(x => !x.IsCancelled && x.StartsAt > after)
            .Select(x => x.StartsAt)
            .Min();

        return moved.HasValue && (next == null || moved < next) ? moved : next;
    }

    private static IEnumerable<Occurrence> Series(CalendarEvent calendarEvent, DateRange range) =>
        Recurrence.Between(
            calendarEvent.StartsAt, calendarEvent.EndsAt, RecurrenceRule.Of(calendarEvent), ZoneOf(calendarEvent), range);

    private static TimeZoneInfo ZoneOf(CalendarEvent calendarEvent) =>
        TimeZones.Resolve(calendarEvent.Owner?.TimeZone);

    private static EventDate Plain(CalendarEvent calendarEvent, DateTime start, DateTime end, DateTime? occurrenceStart) =>
        new(calendarEvent, start, end, occurrenceStart, calendarEvent.Title, calendarEvent.Location);
}
