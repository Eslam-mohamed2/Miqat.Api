using Miqat.Application.Common;

namespace Miqat.Application.Scheduling;

/// <summary>One person to find a time for: where they are, when they work and when they are already busy.</summary>
public sealed record Participant(
    TimeZoneInfo Zone, int WorkStartMinutes, int WorkEndMinutes, Func<DayOfWeek, bool> IsWorkDay,
    IReadOnlyList<Occurrence> Busy);

/// <summary>
/// Finds times at which a group of people are all free. Plain rules, no
/// guessing: a time is offered when nobody has something else then and - if
/// asked - it lies inside the working hours of every person, each in their
/// own time zone.
/// </summary>
public static class SlotFinder
{
    /// <summary>Suggested times start on the hour or the half hour.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(30);

    /// <param name="perDay">At most this many suggestions on one day (a day of <paramref name="dayZone"/>), so the list spreads over several days.</param>
    public static List<Occurrence> Find(
        IReadOnlyList<Participant> people, TimeSpan duration, DateRange window,
        int max, int perDay, bool workingHoursOnly, TimeZoneInfo dayZone)
    {
        var slots = new List<Occurrence>();
        var offeredOn = new Dictionary<DateTime, int>();
        var start = RoundUp(window.From);

        while (slots.Count < max && start + duration <= window.To)
        {
            var end = start + duration;
            var day = TimeZones.ToLocal(start, dayZone).Date;

            var suits = offeredOn.GetValueOrDefault(day) < perDay &&
                        people.All(p => IsFree(p, start, end) && (!workingHoursOnly || IsWorkingTime(p, start, end)));

            if (!suits)
            {
                start += Step;
                continue;
            }

            slots.Add(new Occurrence(start, end));
            offeredOn[day] = offeredOn.GetValueOrDefault(day) + 1;

            // The next suggestion starts after this one, so the list never offers overlapping times.
            start = RoundUp(end);
        }

        return slots;
    }

    /// <summary>Sorts busy times and joins the ones that touch or overlap.</summary>
    public static List<Occurrence> Merge(IEnumerable<Occurrence> busy)
    {
        var merged = new List<Occurrence>();

        foreach (var block in busy.OrderBy(b => b.Start))
        {
            if (merged.Count > 0 && block.Start <= merged[^1].End)
                merged[^1] = merged[^1] with { End = block.End > merged[^1].End ? block.End : merged[^1].End };
            else
                merged.Add(block);
        }

        return merged;
    }

    private static bool IsFree(Participant person, DateTime start, DateTime end) =>
        !person.Busy.Any(b => b.Start < end && b.End > start);

    /// <summary>The whole stretch lies inside one working day of the person.</summary>
    private static bool IsWorkingTime(Participant person, DateTime start, DateTime end)
    {
        var localStart = TimeZones.ToLocal(start, person.Zone);
        var localEnd = TimeZones.ToLocal(end, person.Zone);

        // Measured from the midnight the stretch starts after, so a meeting
        // that ends exactly at midnight counts as ending at 24:00 of its day.
        var endMinutes = (localEnd - localStart.Date).TotalMinutes;

        return person.IsWorkDay(localStart.DayOfWeek) &&
               localStart.TimeOfDay.TotalMinutes >= person.WorkStartMinutes &&
               endMinutes <= person.WorkEndMinutes;
    }

    private static DateTime RoundUp(DateTime moment)
    {
        var remainder = moment.Ticks % Step.Ticks;
        return remainder == 0 ? moment : moment.AddTicks(Step.Ticks - remainder);
    }
}
