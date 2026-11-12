using Microsoft.Extensions.Logging;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Scheduling;
using Miqat.Application.Specifications.Events;
using Miqat.Domain.Entities;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Services
{
    /// <summary>When the weekly summary goes out: a day of the week and an hour, on the clock of each person.</summary>
    public class WeeklySummarySettings
    {
        public bool Enabled { get; set; } = true;
        public DayOfWeek Day { get; set; } = DayOfWeek.Monday;
        public int Hour { get; set; } = 8;
        public int IntervalSeconds { get; set; } = 1800;
    }

    /// <summary>
    /// Sends the weekly summary email to the people who switched it on
    /// (Settings > Notifications). Run on a timer by the host; each person gets
    /// it once a week, at the configured hour of their own time zone.
    /// </summary>
    public class WeeklySummaryService
    {
        private const int ComingUpLines = 5;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailService _email;
        private readonly WeeklySummarySettings _settings;
        private readonly ILogger<WeeklySummaryService> _logger;

        public WeeklySummaryService(
            IUnitOfWork unitOfWork,
            IEmailService email,
            WeeklySummarySettings settings,
            ILogger<WeeklySummaryService> logger)
        {
            _unitOfWork = unitOfWork;
            _email = email;
            _settings = settings;
            _logger = logger;
        }

        /// <summary>Returns how many summaries were sent.</summary>
        public async Task<int> RunAsync(DateTime now)
        {
            // Without a mail provider there is nothing to send with.
            if (!_email.IsConfigured) return 0;

            // The database leaves out the people who just got theirs; whether the
            // weekly moment has come depends on each time zone and is checked below.
            var notSince = now.AddDays(-1);
            var candidates = await _unitOfWork.Repository<User>().FindAsync(
                u => u.WeeklySummaryEnabled && u.IsActive && u.IsVerified && !u.IsDeleted &&
                     (u.WeeklySummarySentAt == null || u.WeeklySummarySentAt < notSince));

            var sent = 0;

            foreach (var user in candidates)
            {
                var zone = TimeZones.Resolve(user.TimeZone);
                if (!IsDue(now, zone, user.WeeklySummarySentAt, _settings.Day, _settings.Hour)) continue;

                try
                {
                    await _email.SendWeeklySummaryAsync(user.Email, user.FullName, await BuildAsync(user.Id, zone, now));

                    // Saved per person: a failure further down the list must
                    // not make this one go out twice.
                    user.WeeklySummarySentAt = now;
                    await _unitOfWork.CompleteAsync();
                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Weekly summary for {UserId} failed; it is tried again on the next pass.", user.Id);
                }
            }

            return sent;
        }

        /// <summary>
        /// True when the weekly moment (for example Monday 08:00 on the clock
        /// of the person) has come since the last summary went out.
        /// </summary>
        public static bool IsDue(DateTime nowUtc, TimeZoneInfo zone, DateTime? lastSentUtc, DayOfWeek day, int hour)
        {
            var localNow = TimeZones.ToLocal(nowUtc, zone);

            var daysSince = ((int)localNow.DayOfWeek - (int)day + 7) % 7;
            var slot = localNow.Date.AddDays(-daysSince).AddHours(hour);
            if (slot > localNow) slot = slot.AddDays(-7);

            return lastSentUtc == null || TimeZones.ToLocal(lastSentUtc.Value, zone) < slot;
        }

        private async Task<WeeklySummaryDto> BuildAsync(Guid userId, TimeZoneInfo zone, DateTime now)
        {
            var nextWeek = new DateRange(now, now.AddDays(7));
            var weekAgo = now.AddDays(-7);

            var tasks = await _unitOfWork.Repository<TaskItem>().SelectAsync(
                t => (t.UserId == userId || t.AssignedToUserId == userId) && !t.IsDeleted,
                t => new { t.Title, t.Status, t.DueDate, t.CompletedAt });
            var open = tasks.Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled).ToList();
            var dueSoon = open.Where(t => t.DueDate >= nextWeek.From && t.DueDate < nextWeek.To).ToList();

            var events = await _unitOfWork.Repository<CalendarEvent>()
                .ListAsync(new EventsInRangeSpec(new[] { userId }, Array.Empty<Guid>(), nextWeek));
            var dates = events.SelectMany(e => EventDates.In(e, nextWeek)).ToList();

            string Line(DateTime when, string title) =>
                $"{TimeZones.ToLocal(when, zone):ddd d MMM, HH:mm} - {title}";

            return new WeeklySummaryDto
            {
                CompletedLastWeek = tasks.Count(t => t.CompletedAt >= weekAgo),
                DueThisWeek = dueSoon.Count,
                Overdue = open.Count(t => t.DueDate < now),
                EventsThisWeek = dates.Count,
                ComingUp = dates.Select(d => (When: d.Start, Text: Line(d.Start, d.Title)))
                    .Concat(dueSoon.Select(t => (When: t.DueDate!.Value, Text: Line(t.DueDate.Value, t.Title + " (due)"))))
                    .OrderBy(x => x.When)
                    .Take(ComingUpLines)
                    .Select(x => x.Text)
                    .ToList()
            };
        }
    }
}
