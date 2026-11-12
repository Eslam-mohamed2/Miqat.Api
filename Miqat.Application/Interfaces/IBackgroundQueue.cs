namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// Work that should happen soon but must not make the current request wait:
    /// sending an email or a device notification can take seconds.
    /// </summary>
    public interface IBackgroundQueue
    {
        /// <summary>
        /// Queues the work. It runs later with its own services (the provider it
        /// is given), so it must not use anything that belongs to the request.
        /// </summary>
        void Enqueue(Func<IServiceProvider, Task> work);
    }
}
