namespace Miqat.API.Background
{
    /// <summary>
    /// The timer loop every recurring job needs, written once: wait for
    /// start-up to finish, run a pass, wait, run again. Each pass gets its own
    /// services, and a pass that fails (database briefly away, for example) is
    /// logged and tried again on the next tick instead of ending the loop.
    /// </summary>
    public abstract class PeriodicWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        protected PeriodicWorker(IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>Shown in the log lines of this job.</summary>
        protected abstract string Name { get; }

        protected abstract bool Enabled { get; }
        protected abstract TimeSpan Interval { get; }

        /// <summary>Gives start-up (migrations, seeding) time to finish before the first pass.</summary>
        protected virtual TimeSpan InitialDelay => TimeSpan.FromSeconds(30);

        /// <summary>One pass. Returns how many things it did, for the log.</summary>
        protected abstract Task<int> RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled)
            {
                _logger.LogInformation("{Job} is switched off in the configuration.", Name);
                return;
            }

            try
            {
                await Task.Delay(InitialDelay, stoppingToken);

                using var timer = new PeriodicTimer(Interval);
                do
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var done = await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                        if (done > 0) _logger.LogInformation("{Job}: {Count} sent.", Name, done);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "{Job} pass failed.", Name);
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }
    }
}
