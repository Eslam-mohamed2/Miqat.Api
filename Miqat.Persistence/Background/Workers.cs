using Miqat.Application.Services;

namespace Miqat.API.Background
{
    /// <summary>
    /// Sends reminders: the ones people set on tasks and events, and the
    /// automatic "due soon" notice for tasks. The work itself is in
    /// <see cref="ReminderDispatcher"/>.
    /// <para>
    /// Settings (all optional): Reminders:Enabled (true),
    /// Reminders:IntervalSeconds (60 - a "10 minutes before" reminder is at
    /// most that late), Reminders:WindowHours (24 - how far ahead of the due
    /// date the "due soon" notice goes out), Reminders:InitialDelaySeconds (30).
    /// </para>
    /// </summary>
    public class ReminderWorker : PeriodicWorker
    {
        private readonly IConfiguration _configuration;

        public ReminderWorker(
            IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ReminderWorker> logger)
            : base(scopeFactory, logger)
        {
            _configuration = configuration;
        }

        protected override string Name => "Reminders";
        protected override bool Enabled => _configuration.GetValue("Reminders:Enabled", true);

        protected override TimeSpan Interval =>
            TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue("Reminders:IntervalSeconds", 60)));

        protected override TimeSpan InitialDelay =>
            TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("Reminders:InitialDelaySeconds", 30)));

        private TimeSpan DueSoonWindow =>
            TimeSpan.FromHours(Math.Clamp(_configuration.GetValue("Reminders:WindowHours", 24), 1, 168));

        protected override Task<int> RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken) =>
            services.GetRequiredService<ReminderDispatcher>().RunAsync(DateTime.UtcNow, DueSoonWindow);
    }

    /// <summary>
    /// Sends the weekly summary email to the people who switched it on. The
    /// work itself is in <see cref="WeeklySummaryService"/>; the settings are
    /// the WeeklySummary section (Enabled, Day, Hour, IntervalSeconds).
    /// </summary>
    public class WeeklySummaryWorker : PeriodicWorker
    {
        private readonly WeeklySummarySettings _settings;

        public WeeklySummaryWorker(
            IServiceScopeFactory scopeFactory, WeeklySummarySettings settings, ILogger<WeeklySummaryWorker> logger)
            : base(scopeFactory, logger)
        {
            _settings = settings;
        }

        protected override string Name => "Weekly summary";
        protected override bool Enabled => _settings.Enabled;
        protected override TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(10, _settings.IntervalSeconds));

        protected override Task<int> RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken) =>
            services.GetRequiredService<WeeklySummaryService>().RunAsync(DateTime.UtcNow);
    }
}
