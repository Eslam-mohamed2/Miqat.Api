using Miqat.Application.Common;
using Miqat.Application.Scheduling;
using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>
/// The date arithmetic behind the calendar: repeating series, single dates
/// that were moved or skipped, and finding a time when several people are free.
///
/// Europe/London is used for the time-zone cases because its daylight-saving
/// rules are stable: clocks go forward on the last Sunday of March (29 March
/// in 2026) and back on the last Sunday of October.
/// </summary>
public class SchedulingTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static List<DateTime> Starts(DateTime first, RecurrenceRule rule, TimeZoneInfo zone, DateTime from, DateTime to) =>
        Recurrence.Between(first, first.AddHours(1), rule, zone, new DateRange(from, to)).Select(o => o.Start).ToList();

    // ── Repeating series ─────────────────────────────────────────────────────

    [Fact]
    public void A_weekly_meeting_stays_at_the_same_wall_clock_time_when_the_clocks_change()
    {
        // 09:00 in London is 09:00 UTC in winter and 08:00 UTC in summer.
        var starts = Starts(Utc(2026, 3, 16, 9), new RecurrenceRule(RecurrencePattern.Weekly), London,
            Utc(2026, 3, 16), Utc(2026, 4, 7));

        Assert.Equal(new[] { Utc(2026, 3, 16, 9), Utc(2026, 3, 23, 9), Utc(2026, 3, 30, 8), Utc(2026, 4, 6, 8) }, starts);
    }

    [Fact]
    public void A_monthly_event_on_the_31st_comes_back_to_the_31st_after_a_short_month()
    {
        var starts = Starts(Utc(2026, 1, 31, 12), new RecurrenceRule(RecurrencePattern.Monthly), TimeZoneInfo.Utc,
            Utc(2026, 1, 1), Utc(2026, 4, 1));

        Assert.Equal(new[] { Utc(2026, 1, 31, 12), Utc(2026, 2, 28, 12), Utc(2026, 3, 31, 12) }, starts);
    }

    [Fact]
    public void Repeat_until_a_day_includes_the_date_on_that_day()
    {
        // A date picker sends midnight; the 09:00 meeting of that day still happens.
        var rule = new RecurrenceRule(RecurrencePattern.Daily, 1, Until: Utc(2026, 5, 3));

        var starts = Starts(Utc(2026, 5, 1, 9), rule, TimeZoneInfo.Utc, Utc(2026, 5, 1), Utc(2026, 6, 1));

        Assert.Equal(new[] { Utc(2026, 5, 1, 9), Utc(2026, 5, 2, 9), Utc(2026, 5, 3, 9) }, starts);
    }

    [Fact]
    public void Every_second_week_skips_the_weeks_in_between()
    {
        var starts = Starts(Utc(2026, 6, 1, 10), new RecurrenceRule(RecurrencePattern.Weekly, Interval: 2), TimeZoneInfo.Utc,
            Utc(2026, 6, 1), Utc(2026, 7, 1));

        Assert.Equal(new[] { Utc(2026, 6, 1, 10), Utc(2026, 6, 15, 10), Utc(2026, 6, 29, 10) }, starts);
    }

    [Fact]
    public void A_series_that_started_years_ago_gives_exactly_the_dates_of_the_week_asked_for()
    {
        var starts = Starts(Utc(2012, 1, 1, 7), new RecurrenceRule(RecurrencePattern.Daily), TimeZoneInfo.Utc,
            Utc(2026, 11, 2), Utc(2026, 11, 9));

        Assert.Equal(7, starts.Count);
        Assert.Equal(Utc(2026, 11, 2, 7), starts.First());
        Assert.Equal(Utc(2026, 11, 8, 7), starts.Last());
    }

    [Fact]
    public void A_date_that_began_before_the_range_but_is_still_on_counts()
    {
        // A three-day event that started the evening before the range.
        var dates = Recurrence.Between(
            Utc(2026, 8, 9, 20), Utc(2026, 8, 12, 20), default, TimeZoneInfo.Utc,
            new DateRange(Utc(2026, 8, 10), Utc(2026, 8, 11))).ToList();

        Assert.Single(dates);
    }

    [Fact]
    public void The_next_start_is_strictly_after_the_moment_asked_about()
    {
        var first = Utc(2026, 9, 7, 9);
        var rule = new RecurrenceRule(RecurrencePattern.Weekly);

        Assert.Equal(first, Recurrence.NextStart(first, rule, TimeZoneInfo.Utc, first.AddMinutes(-1)));
        Assert.Equal(first.AddDays(7), Recurrence.NextStart(first, rule, TimeZoneInfo.Utc, first));
        Assert.Null(Recurrence.NextStart(first, default, TimeZoneInfo.Utc, first));
    }

    // ── Single dates of an event ─────────────────────────────────────────────

    private static CalendarEvent WeeklyEvent(DateTime start, string zone = "UTC")
    {
        var owner = new User("Sara Ali", "sara@example.com", Utc(1990, 1, 1), null, null, null, zone);
        var calendarEvent = new CalendarEvent("Stand-up", start, start.AddMinutes(30), owner, Guid.NewGuid());
        calendarEvent.Repeat(RecurrencePattern.Weekly);
        return calendarEvent;
    }

    [Fact]
    public void A_skipped_date_is_left_out_and_a_moved_date_appears_where_it_was_moved_to()
    {
        var first = Utc(2026, 10, 5, 9);
        var calendarEvent = WeeklyEvent(first);

        var skipped = new EventException(calendarEvent.Id, first.AddDays(7));
        skipped.Cancel();
        var moved = new EventException(calendarEvent.Id, first.AddDays(14));
        moved.Change(first.AddDays(15).AddHours(2), first.AddDays(15).AddHours(3), "Stand-up (moved)", null);
        calendarEvent.Exceptions.Add(skipped);
        calendarEvent.Exceptions.Add(moved);

        var dates = EventDates.In(calendarEvent, new DateRange(first, first.AddDays(22)))
            .OrderBy(d => d.Start).ToList();

        Assert.Equal(new[] { first, first.AddDays(15).AddHours(2), first.AddDays(21) }, dates.Select(d => d.Start));
        Assert.Equal("Stand-up (moved)", dates[1].Title);
        Assert.Equal(first.AddDays(14), dates[1].OccurrenceStart);
    }

    [Fact]
    public void The_next_start_of_an_event_steps_over_a_skipped_date()
    {
        var first = Utc(2026, 10, 5, 9);
        var calendarEvent = WeeklyEvent(first);
        var skipped = new EventException(calendarEvent.Id, first.AddDays(7));
        skipped.Cancel();
        calendarEvent.Exceptions.Add(skipped);

        Assert.Equal(first.AddDays(14), EventDates.NextStart(calendarEvent, first));
    }

    [Fact]
    public void Only_real_dates_of_the_series_can_be_changed()
    {
        var first = Utc(2026, 10, 5, 9);
        var calendarEvent = WeeklyEvent(first);

        Assert.True(EventDates.IsSeriesDate(calendarEvent, first.AddDays(7)));
        Assert.False(EventDates.IsSeriesDate(calendarEvent, first.AddDays(8)));
    }

    // ── Finding a time ───────────────────────────────────────────────────────

    private static Participant NineToFive(TimeZoneInfo zone, params Occurrence[] busy) =>
        new(zone, 9 * 60, 17 * 60, day => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday), busy);

    [Fact]
    public void A_suggested_time_is_inside_the_working_hours_of_everyone_in_their_own_time_zone()
    {
        // Monday 2 Nov 2026, winter time: London works 09-17 UTC, Cairo (UTC+2) works 07-15 UTC.
        var slots = SlotFinder.Find(
            new[] { NineToFive(London), NineToFive(Cairo) }, TimeSpan.FromHours(1),
            new DateRange(Utc(2026, 11, 2), Utc(2026, 11, 3)), max: 20, perDay: 20,
            workingHoursOnly: true, dayZone: London);

        Assert.Equal(Utc(2026, 11, 2, 9), slots.First().Start);
        Assert.Equal(Utc(2026, 11, 2, 15), slots.Last().End);
    }

    [Fact]
    public void Busy_times_are_avoided_and_suggestions_never_overlap()
    {
        var busy = new Occurrence(Utc(2026, 11, 2, 9), Utc(2026, 11, 2, 10, 30));

        var slots = SlotFinder.Find(
            new[] { NineToFive(TimeZoneInfo.Utc, busy) }, TimeSpan.FromHours(1),
            new DateRange(Utc(2026, 11, 2), Utc(2026, 11, 3)), max: 3, perDay: 3,
            workingHoursOnly: true, dayZone: TimeZoneInfo.Utc);

        Assert.Equal(new[] { Utc(2026, 11, 2, 10, 30), Utc(2026, 11, 2, 11, 30), Utc(2026, 11, 2, 12, 30) },
            slots.Select(s => s.Start));
    }

    [Fact]
    public void Suggestions_spread_over_days_and_skip_the_weekend()
    {
        // From Friday: two on Friday, none on Saturday or Sunday, then Monday.
        var slots = SlotFinder.Find(
            new[] { NineToFive(TimeZoneInfo.Utc) }, TimeSpan.FromHours(1),
            new DateRange(Utc(2026, 11, 6), Utc(2026, 11, 10)), max: 3, perDay: 2,
            workingHoursOnly: true, dayZone: TimeZoneInfo.Utc);

        Assert.Equal(new[] { Utc(2026, 11, 6, 9), Utc(2026, 11, 6, 10), Utc(2026, 11, 9, 9) }, slots.Select(s => s.Start));
    }

    [Fact]
    public void Busy_times_that_touch_or_overlap_are_joined()
    {
        var merged = SlotFinder.Merge(new[]
        {
            new Occurrence(Utc(2026, 1, 1, 13), Utc(2026, 1, 1, 14)),
            new Occurrence(Utc(2026, 1, 1, 9), Utc(2026, 1, 1, 10)),
            new Occurrence(Utc(2026, 1, 1, 10), Utc(2026, 1, 1, 11)),
            new Occurrence(Utc(2026, 1, 1, 9, 30), Utc(2026, 1, 1, 9, 45))
        });

        Assert.Equal(
            new[] { new Occurrence(Utc(2026, 1, 1, 9), Utc(2026, 1, 1, 11)), new Occurrence(Utc(2026, 1, 1, 13), Utc(2026, 1, 1, 14)) },
            merged);
    }

    // ── Small pieces ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("09:00", 540)]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 1439)]
    [InlineData("24:00", 1440)]
    public void Times_of_day_convert_to_minutes_and_back(string text, int minutes)
    {
        Assert.Equal(minutes, ClockTime.ToMinutes(text));
        Assert.Equal(text, ClockTime.FromMinutes(minutes));
    }

    [Theory]
    [InlineData("9:00")]
    [InlineData("25:00")]
    [InlineData("nine")]
    [InlineData("")]
    [InlineData(null)]
    public void Text_that_is_not_a_time_of_day_is_refused(string? text)
    {
        Assert.Null(ClockTime.ToMinutes(text));
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(10, "in 10 minutes")]
    [InlineData(60, "in about an hour")]
    [InlineData(180, "in about 3 hours")]
    [InlineData(2880, "in about 2 days")]
    public void Time_left_is_said_the_way_a_person_would(int minutes, string expected)
    {
        Assert.Equal(expected, ClockTime.Until(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void The_weekly_summary_is_due_once_the_weekly_moment_has_passed_and_only_once()
    {
        // Monday 08:00 in London (winter: the same in UTC). 2 Nov 2026 is a Monday.
        var mondayEight = Utc(2026, 11, 2, 8);

        Assert.False(WeeklySummaryService.IsDue(mondayEight.AddMinutes(-1), London, mondayEight.AddDays(-7), DayOfWeek.Monday, 8));
        Assert.True(WeeklySummaryService.IsDue(mondayEight.AddMinutes(1), London, mondayEight.AddDays(-7), DayOfWeek.Monday, 8));
        Assert.False(WeeklySummaryService.IsDue(mondayEight.AddDays(3), London, mondayEight.AddMinutes(5), DayOfWeek.Monday, 8));
        Assert.True(WeeklySummaryService.IsDue(mondayEight.AddDays(3), London, lastSentUtc: null, DayOfWeek.Monday, 8));
    }

    [Fact]
    public void A_reminder_knows_whether_it_was_snoozed()
    {
        var reminder = new Reminder(Guid.NewGuid(), CalendarItemKind.Event, Guid.NewGuid(), minutesBefore: 10);
        var start = Utc(2026, 11, 2, 9);

        reminder.ScheduleFor(start);
        Assert.Equal(start.AddMinutes(-10), reminder.RemindAt);
        Assert.False(reminder.IsSnoozed);

        reminder.SnoozeUntil(start.AddMinutes(-5));
        Assert.True(reminder.IsSnoozed);
        Assert.Equal(start, reminder.ItemTime);

        reminder.ScheduleFor(null);
        Assert.Null(reminder.RemindAt);
        Assert.False(reminder.IsSnoozed);
    }
}
