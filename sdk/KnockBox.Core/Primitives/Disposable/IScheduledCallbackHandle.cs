namespace KnockBox.Core.Primitives.Disposable
{
    /// <summary>
    /// Handle to a callback scheduled via <c>AbstractGameState.ScheduleCallback</c>.
    /// Callers may <see cref="Cancel"/> the scheduled work before it runs, or
    /// <see cref="IDisposable.Dispose"/> the handle to cancel-and-forget. Both
    /// operations are idempotent and safe to call after the owning state has
    /// been disposed.
    /// </summary>
    /// <remarks>
    /// The handle does not own the underlying <see cref="CancellationTokenSource"/>:
    /// the owning state is responsible for disposing it once the scheduled task
    /// completes or the state is torn down. Implementations must therefore tolerate
    /// the CTS having been disposed out from under them.
    /// </remarks>
    public interface IScheduledCallbackHandle : IDisposable
    {
        /// <summary>
        /// True once the scheduled callback has been cancelled (either directly via
        /// <see cref="Cancel"/>/<see cref="IDisposable.Dispose"/>, or transitively
        /// because the owning state was disposed).
        /// </summary>
        bool IsCancelled { get; }

        /// <summary>
        /// Requests cancellation of the scheduled callback. Idempotent; safe to call
        /// multiple times and safe to call after the callback has already completed
        /// or the owning state has been disposed.
        /// </summary>
        void Cancel();
    }
}
