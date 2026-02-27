using KnockBox.Extensions.Events;
using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.Shared
{
    public abstract class AbstractGameState : IDisposable
    {
        private const string STATE_CHANGED_GROUP = "StateChanged";
        private readonly Lock _disposeLock = new();
        private readonly SemaphoreSlim _executeLock = new(1, 1);

        /// <summary>
        /// The event manager for this game state.
        /// </summary>
        public ITypedThreadSafeEventManager EventManager { get; private set; } 
            = new TypedThreadSafeEventManager();

        public IDisposable SubscribeToStateChanged(Func<ValueTask> handler)
        {
            ValueTask HandlerWrapper(int unusedInput) => handler();

            return EventManager.Subscribe<int>(STATE_CHANGED_GROUP, HandlerWrapper);
        }

        /// <summary>
        /// Executes the provided action async.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async ValueTask<Result> ExecuteAsync(Func<ValueTask> action, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await _executeLock.WaitAsync(ct);

                try
                {
                    await action();
                    return Result.Success;
                }
                finally
                {
                    _executeLock.Release();
                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                return Result.FromError(ex);
            }
        }

        /// <summary>
        /// Executes the provided action sync.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public Result Execute(Action action)
        {
            try
            {
                _executeLock.Wait();

                try
                {
                    action();
                    return Result.Success;
                }
                finally
                {
                    _executeLock.Release();
                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                return Result.FromError(ex);
            }
        }

        /// <summary>
        /// Executes the action with exclusive read access to the game state.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public async ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await _executeLock.WaitAsync(ct);

                try
                {
                    await action();
                    return Result.Success;
                }
                finally
                {
                    _executeLock.Release();
                }
            }
            catch (Exception ex)
            {
                return Result.FromError(ex);
            }
        }

        /// <summary>
        /// Executes the action with exclusive read access to the game state.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public Result WithExclusiveRead(Action action)
        {
            try
            {
                _executeLock.Wait();

                try
                {
                    action();
                    return Result.Success;
                }
                finally
                {
                    _executeLock.Release();
                }
            }
            catch (Exception ex)
            {
                return Result.FromError(ex);
            }
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                EventManager.Dispose();
                _executeLock.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        private void NotifyStateChanged()
        {
            EventManager.Notify(STATE_CHANGED_GROUP, 1);
        }
    }
}
