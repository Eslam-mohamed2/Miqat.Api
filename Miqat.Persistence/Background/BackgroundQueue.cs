using System.Threading.Channels;
using Miqat.Application.Interfaces;

namespace Miqat.API.Background
{
    /// <summary>
    /// Runs queued work (emails, device notifications) one after another,
    /// outside the request that asked for it. Each piece gets its own services,
    /// and one that fails is logged and dropped: it must not stop the rest.
    /// <para>
    /// Kept in memory on purpose: what goes through here is a courtesy copy of
    /// a notification that is already stored. Losing the queue in a restart
    /// loses those copies, never the notifications.
    /// </para>
    /// </summary>
    public class BackgroundQueue : BackgroundService, IBackgroundQueue
    {
        /// <summary>Beyond this the mail or push provider is clearly down; new work is dropped instead of piling up.</summary>
        private const int Capacity = 2000;

        private readonly Channel<Func<IServiceProvider, Task>> _work =
            Channel.CreateBounded<Func<IServiceProvider, Task>>(new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackgroundQueue> _logger;

        public BackgroundQueue(IServiceScopeFactory scopeFactory, ILogger<BackgroundQueue> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void Enqueue(Func<IServiceProvider, Task> work)
        {
            if (!_work.Writer.TryWrite(work))
                _logger.LogWarning("The background queue is full; a piece of work was dropped.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await work(scope.ServiceProvider);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background work failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }
    }
}
