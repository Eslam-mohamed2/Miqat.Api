using Miqat.Application.Scheduling;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>
/// Calendar files (.ics). What matters is that events arrive at the right
/// moment whichever way the file wrote its times, and that what Miqat writes
/// can be read back unchanged.
/// </summary>
public class IcsFormatTests
{
    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static string File(params string[] lines) =>
        string.Join("\r\n", new[] { "BEGIN:VCALENDAR", "VERSION:2.0" }.Concat(lines).Append("END:VCALENDAR")) + "\r\n";

    [Fact]
    public void What_is_written_reads_back_the_same()
    {
        var events = new[]
        {
            new IcsEvent("a@test", "Planning; budget, roadmap", "Line one\nLine two", "Room 2",
                Utc(2026, 11, 12, 9), Utc(2026, 11, 12, 10), false,
                new RecurrenceRule(RecurrencePattern.Weekly, 2, Utc(2026, 12, 31, 23, 59)),
                new[] { Utc(2026, 11, 26, 9) }),
            // An all-day event: midnight to midnight in the zone of the user.
            new IcsEvent("b@test", "Day off", null, null,
                TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 20), Cairo),
                TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 21), Cairo), true, default)
        };

        var text = IcsFormat.Write("Miqat", events, Cairo, Utc(2026, 11, 11));
        var read = IcsFormat.Read(text, Cairo);

        Assert.Equal(0, read.Skipped);
        Assert.Equal(2, read.Events.Count);

        var meeting = read.Events[0];
        Assert.Equal("a@test", meeting.Uid);
        Assert.Equal("Planning; budget, roadmap", meeting.Title);
        Assert.Equal("Line one\nLine two", meeting.Description);
        Assert.Equal("Room 2", meeting.Location);
        Assert.Equal(events[0].Start, meeting.Start);
        Assert.Equal(events[0].End, meeting.End);
        Assert.Equal(events[0].Rule, meeting.Rule);
        Assert.Equal(new[] { Utc(2026, 11, 26, 9) }, meeting.SkippedStarts);

        var dayOff = read.Events[1];
        Assert.True(dayOff.IsAllDay);
        Assert.Equal(events[1].Start, dayOff.Start);
        Assert.Equal(events[1].End, dayOff.End);
        Assert.False(dayOff.Rule.Repeats);
    }

    [Fact]
    public void No_written_line_is_longer_than_the_format_allows()
    {
        var longTitle = string.Concat(Enumerable.Repeat("اجتماع الفريق الأسبوعي ", 12));
        var text = IcsFormat.Write("Miqat", new[]
        {
            new IcsEvent("c@test", longTitle, null, null, Utc(2026, 1, 1, 9), Utc(2026, 1, 1, 10), false, default)
        }, TimeZoneInfo.Utc, Utc(2026, 1, 1));

        Assert.All(text.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75));
        Assert.Equal(longTitle.Trim(), IcsFormat.Read(text, TimeZoneInfo.Utc).Events.Single().Title);
    }

    [Fact]
    public void Times_are_read_whichever_way_the_file_wrote_them()
    {
        var read = IcsFormat.Read(File(
            "BEGIN:VEVENT", "UID:utc", "SUMMARY:In UTC", "DTSTART:20261112T070000Z", "DTEND:20261112T080000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:zone", "SUMMARY:In a named zone", "DTSTART;TZID=Africa/Cairo:20261112T090000", "DTEND;TZID=Africa/Cairo:20261112T100000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:floating", "SUMMARY:No zone at all", "DTSTART:20261112T090000", "DURATION:PT1H30M", "END:VEVENT",
            "BEGIN:VEVENT", "UID:day", "SUMMARY:A whole day", "DTSTART;VALUE=DATE:20261112", "END:VEVENT"), Cairo);

        // Cairo is two hours ahead of UTC in November.
        Assert.Equal(4, read.Events.Count);
        Assert.All(read.Events.Take(3), e => Assert.Equal(Utc(2026, 11, 12, 7), e.Start));
        Assert.Equal(Utc(2026, 11, 12, 8), read.Events[0].End);
        Assert.Equal(Utc(2026, 11, 12, 8), read.Events[1].End);
        Assert.Equal(Utc(2026, 11, 12, 8, 30), read.Events[2].End);

        var day = read.Events[3];
        Assert.True(day.IsAllDay);
        Assert.Equal(Utc(2026, 11, 11, 22), day.Start);
        Assert.Equal(Utc(2026, 11, 12, 22), day.End);
    }

    [Fact]
    public void A_repeat_given_as_a_number_of_times_ends_on_the_last_of_them()
    {
        var read = IcsFormat.Read(File(
            "BEGIN:VEVENT", "UID:count", "SUMMARY:Course", "DTSTART:20261102T170000Z", "DTEND:20261102T180000Z",
            "RRULE:FREQ=WEEKLY;COUNT=4", "EXDATE:20261109T170000Z,20261116T170000Z", "END:VEVENT"), TimeZoneInfo.Utc);

        var course = read.Events.Single();
        Assert.Equal(RecurrencePattern.Weekly, course.Rule.Pattern);
        Assert.Equal(Utc(2026, 11, 23, 17), course.Rule.Until);
        Assert.Equal(new[] { Utc(2026, 11, 9, 17), Utc(2026, 11, 16, 17) }, course.SkippedStarts);
    }

    [Fact]
    public void An_alarm_inside_an_event_does_not_leak_into_it_and_folded_lines_are_joined()
    {
        var read = IcsFormat.Read(File(
            "BEGIN:VEVENT", "UID:alarm", "SUMMARY:Review the quarterly ", " numbers\\, then lunch",
            "DTSTART:20261112T070000Z", "DTEND:20261112T080000Z",
            "BEGIN:VALARM", "ACTION:DISPLAY", "DESCRIPTION:This is an event reminder", "TRIGGER:-PT10M", "END:VALARM",
            "END:VEVENT"), TimeZoneInfo.Utc);

        var review = read.Events.Single();
        Assert.Equal("Review the quarterly numbers, then lunch", review.Title);
        Assert.Null(review.Description);
    }

    [Fact]
    public void Entries_that_cannot_be_placed_are_counted_not_guessed()
    {
        var read = IcsFormat.Read(File(
            "BEGIN:VEVENT", "UID:no-start", "SUMMARY:No start", "END:VEVENT",
            "BEGIN:VEVENT", "UID:cancelled", "SUMMARY:Called off", "STATUS:CANCELLED", "DTSTART:20261112T070000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:one-date", "SUMMARY:One changed date", "RECURRENCE-ID:20261112T070000Z", "DTSTART:20261112T090000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:fine", "DTSTART:20261112T070000Z", "END:VEVENT"), TimeZoneInfo.Utc);

        Assert.Equal(3, read.Skipped);

        var kept = read.Events.Single();
        Assert.Equal("(No title)", kept.Title);
        Assert.Equal(kept.Start.AddHours(1), kept.End);
    }
}
