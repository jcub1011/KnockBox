using KnockBox.Core.Extensions.Returns;

namespace KnockBox.Core.Services.State.Shared
{
    public interface ITickService
    {
        /// <summary>
        /// Automatically unsubscribes the callback when the disposable object is disposed.
        /// </summary>
        /// <param name="tickCallback"></param>
        /// <param name="tickInterval">The amount of ticks that should elapse before the tick callback is invoked. 1 indicates the callback will be invoked every tick.</param>
        /// <returns>Fails if there is a duplicate subscription.</returns>
        ValueResult<IDisposable> RegisterTickCallback(Action tickCallback, int tickInterval = 1);

        /// <summary>
        /// The ticks that elapse per second.
        /// </summary>
        int TicksPerSecond { get; }

        /// <summary>
        /// The time between ticks.
        /// </summary>
        TimeSpan TickInterval { get; }
    }
}