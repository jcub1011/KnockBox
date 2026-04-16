namespace KnockBox.Core.Extensions.Events
{
    public interface IThreadSafeEventManager
    {
        /// <summary>
        /// Subscribes to events.
        /// Unsubscribes automatically when the <see cref="IDisposable"/> is disposed.
        /// </summary>
        /// <param name="callback"></param>
        IDisposable Subscribe(Func<ValueTask> callback);

        /// <summary>
        /// Notifies all subscribers. Waits for all subscribers to receive the message.
        /// </summary>
        /// <param name="args"></param>
        Task NotifyAsync();

        /// <summary>
        /// Notifies all subscribers. Doesn't wait for all subscribers to receive the message.
        /// </summary>
        /// <param name="args"></param>
        void Notify();
    }

    public interface IThreadSafeEventManager<TEventArgs>
    {
        /// <summary>
        /// Subscribes to events.
        /// Unsubscribes automatically when the <see cref="IDisposable"/> is disposed.
        /// </summary>
        /// <param name="callback"></param>
        IDisposable Subscribe(Func<TEventArgs, ValueTask> callback);

        /// <summary>
        /// Notifies all subscribers with the provided args. Waits for all subscribers to receive the message.
        /// </summary>
        /// <param name="args"></param>
        Task NotifyAsync(TEventArgs args);

        /// <summary>
        /// Notifies all subscribers with the provided args. Doesn't wait for all subscribers to receive the message.
        /// </summary>
        /// <param name="args"></param>
        void Notify(TEventArgs args);
    }
}
