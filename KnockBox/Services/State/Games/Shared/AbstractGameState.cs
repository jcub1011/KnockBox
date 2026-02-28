using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Events;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.Shared
{
    public abstract class AbstractGameState(User host, ILogger logger) : IDisposable
    {
        private const string STATE_CHANGED_GROUP = "StateChanged";
        private readonly Lock _disposeLock = new();
        private readonly SemaphoreSlim _executeLock = new(1, 1);
        private readonly Lock _scheduledLock = new();
        private readonly List<CancellationTokenSource> _scheduledCallbacks = [];
        private readonly ConcurrentDictionary<User, IDisposable> _players = [];

        /// <summary>
        /// The event manager for this game state.
        /// </summary>
        public ITypedThreadSafeEventManager EventManager { get; private set; }
            = new TypedThreadSafeEventManager();

        /// <summary>
        /// If this lobby is open for players to join. 
        /// This does not indicate if there is room available, just that the game state is in the phase for players to join.
        /// </summary>
        public bool IsJoinable { get; private set; }

        /// <summary>
        /// The host of the game.
        /// </summary>
        public User Host => host;

        /// <summary>
        /// The players in this game.
        /// </summary>
        public IReadOnlyList<User> Players => [.. _players.Keys];

        /// <summary>
        /// Registers the player. Unregisters the player when the <see cref="IDisposable"/> is disposed.
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public Result<IDisposable> RegisterPlayer(User player)
        {
            if (Host == player)
                return Result.FromError<IDisposable>(new InvalidOperationException($"Host cannot be a player in the game."));

            bool wasAdded = false;
            var unsubscriber = new DisposableAction(() => Execute(() =>
            {
                if (wasAdded) _players.TryRemove(player, out _);
            }));

            if (_players.TryAdd(player, unsubscriber))
            {
                wasAdded = true;
                return Result.FromValue<IDisposable>(unsubscriber);
            }
            else
            {
                wasAdded = false;
                unsubscriber.Dispose();
                return Result.FromError<IDisposable>(new InvalidOperationException($"Player [{player.Name}] is already registered."));
            }
        }

        /// <summary>
        /// Updates the joinable status of the current game.
        /// </summary>
        /// <param name="isJoinable"></param>
        public void UpdateJoinableStatus(bool isJoinable)
        {
            if (isJoinable != IsJoinable)
            {
                IsJoinable = isJoinable;
            }
        }

        /// <summary>
        /// Subscribes to state has changed events.
        /// </summary>
        /// <remarks>
        /// Automatically unsubscribes when the <see cref="IDisposable"/> is disposed.
        /// </remarks>
        /// <param name="handler"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Schedules <paramref name="action"/> to execute after <paramref name="delay"/> via
        /// <see cref="ExecuteAsync"/>, preserving locking and state-change notification semantics.
        /// </summary>
        /// <remarks>
        /// The caller may cancel the scheduled work by calling <see cref="CancellationTokenSource.Cancel"/>
        /// on the returned token source before the delay elapses. The state is responsible for disposing
        /// the token source; the caller must not call <see cref="CancellationTokenSource.Dispose"/> on it.
        /// All outstanding callbacks are automatically cancelled when the state is disposed.
        /// </remarks>
        /// <param name="delay">How long to wait before executing the action.</param>
        /// <param name="action">The action to run inside <see cref="ExecuteAsync"/>.</param>
        /// <returns>
        /// A <see cref="CancellationTokenSource"/> whose token can be cancelled to discard the callback.
        /// </returns>
        public CancellationTokenSource ScheduleCallback(TimeSpan delay, Func<Task> action)
        {
            var cts = new CancellationTokenSource();

            lock (_scheduledLock)
            {
                _scheduledCallbacks.Add(cts);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);
                    await ExecuteAsync(async () => await action(), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Silently discard if cancelled before or during execution.
                }
                catch (ObjectDisposedException)
                {
                    // State was disposed before the callback could acquire the lock.
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error executing scheduled callback.");
                }
                finally
                {
                    lock (_scheduledLock)
                    {
                        _scheduledCallbacks.Remove(cts);
                    }
                    cts.Dispose();
                }
            });

            return cts;
        }

        public void Dispose()
        {
            CancellationTokenSource[] pendingCallbacks;

            lock (_scheduledLock)
            {
                pendingCallbacks = [.. _scheduledCallbacks];
                _scheduledCallbacks.Clear();
            }

            foreach (var cts in pendingCallbacks)
                cts.Cancel();

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
