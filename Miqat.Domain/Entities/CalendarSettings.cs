using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities;

/// <summary>
/// How one person likes their calendar: week start, working hours, the view it
/// opens with and the defaults for new events.
/// </summary>
public class CalendarSettings : BaseEntity
{
    public const int MinutesPerDay = 24 * 60;

    public Guid UserId { get; private set; }
    public virtual User User { get; private set; } = null!;

    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Sunday;
    public CalendarView DefaultView { get; set; } = CalendarView.Month;
    public bool Use24HourClock { get; set; }

    /// <summary>Minutes after midnight, in the time zone of the user.</summary>
    public int WorkDayStartMinutes { get; private set; } = 9 * 60;
    public int WorkDayEndMinutes { get; private set; } = 17 * 60;

    /// <summary>One bit per <see cref="DayOfWeek"/> value. Monday to Friday by default.</summary>
    public int WorkDaysMask { get; private set; } = MaskOf(new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    });

    public int DefaultEventMinutes { get; set; } = 60;

    /// <summary>Reminder added to every new event; null adds none.</summary>
    public int? DefaultReminderMinutes { get; set; } = 15;

    public CalendarSettings(Guid userId) => UserId = userId;

    private CalendarSettings() { }

    public IReadOnlyList<DayOfWeek> WorkDays =>
        Enum.GetValues<DayOfWeek>().Where(IsWorkDay).ToList();

    public bool IsWorkDay(DayOfWeek day) => (WorkDaysMask & (1 << (int)day)) != 0;

    public void SetWorkDays(IEnumerable<DayOfWeek> days) => WorkDaysMask = MaskOf(days);

    public void SetWorkingHours(int startMinutes, int endMinutes)
    {
        if (startMinutes < 0 || endMinutes > MinutesPerDay || endMinutes <= startMinutes)
            throw new ArgumentException("Working hours must start before they end, within one day.");

        WorkDayStartMinutes = startMinutes;
        WorkDayEndMinutes = endMinutes;
    }

    private static int MaskOf(IEnumerable<DayOfWeek> days) =>
        days.Aggregate(0, (mask, day) => mask | (1 << (int)day));
}
