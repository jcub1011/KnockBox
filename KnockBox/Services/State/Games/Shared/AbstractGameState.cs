using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Events;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Users;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Services.State.Games.Shared
{
    public abstract class AbstractGameState(User host, ILogger logger) : IDisposable
    {
        private readonly Lock _disposeLock = new();
        private readonly SemaphoreSlim _executeLock = new(1, 1);
        private readonly Lock _scheduledLock = new();
        private readonly List<CancellationTokenSource> _scheduledCallbacks = [];
        private readonly Lock _playerLock = new();
        private readonly Dictionary<User, IDisposable> _players = [];
        private readonly HashSet<User> _kickedPlayers = [];
        private readonly CancellationTokenSource _disposeCts = new();
        private int _disposed;

        /// <summary>
        /// True if this state has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed == 1;

        /// <summary>
        /// Fired when the state is disposed.
        /// </summary>
        public event Action? OnStateDisposed;

        /// <summary>
        /// Raises when any state changes.
        /// </summary>
        public readonly IThreadSafeEventManager StateChangedEventManager
            = new ThreadSafeEventManager(logger);

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
        public IReadOnlyList<User> Players
        {
            get
            {
                using var scope = _playerLock.EnterScope();
                return [.. _players.Keys];
            }
        }

        /// <summary>
        /// Players that have been kicked from this game.
        /// </summary>
        public IReadOnlyList<User> KickedPlayers
        {
            get
            {
                using var scope = _playerLock.EnterScope();
                return [.. _kickedPlayers];
            }
        }

        /// <summary>
        /// Registers the player. Unregisters the player when the <see cref="IDisposable"/> is disposed.
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public ValueResult<IDisposable> RegisterPlayer(User player)
        {
            if (TryGetDisposeError(out var ode))
                return ValueResult<IDisposable>.FromError("Error registering player.", ode.ToString());

            if (!IsJoinable)
                return ValueResult<IDisposable>.FromError("The game is not currently joinable.");

            if (Host == player)
                return ValueResult<IDisposable>.FromError("Host cannot be a player in the game.");

            bool wasAdded = false;
            var unsubscriber = new DisposableAction(() => Execute(() =>
            {
                using var scope = _playerLock.EnterScope();
                if (wasAdded) _players.Remove(player);
            }));

            using var scope = _playerLock.EnterScope();
            if (_kickedPlayers.Contains(player))
            {
                return ValueResult<IDisposable>.FromError("You have been kicked from this lobby and cannot rejoin.", $"Player [{player.Name}] was kicked and cannot rejoin.");
            }
            else if (_players.TryAdd(player, unsubscriber))
            {
                wasAdded = true;
                logger.LogInformation("User [{userId}] entered game [{type}] hosted by user [{hostId}].", player.Id, GetType().Name, Host.Id);
                return unsubscriber;
            }
            else
            {
                wasAdded = false;
                unsubscriber.Dispose();
                return ValueResult<IDisposable>.FromError("You are already in this lobby.", $"Player [{player.Name}] is already registered.");
            }
        }

        /// <summary>
        /// Kicks the player.
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public Result KickPlayer(User player)
        {
            using var scope = _playerLock.EnterScope();
            if (_players.TryGetValue(player, out var leaveToken))
            {
                _kickedPlayers.Add(player);
                leaveToken.Dispose();
                return Result.Success;
            }

            return Result.FromError("User is not in this game.");
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
                StateChangedEventManager.Notify();
            }
        }

        /// <summary>
        /// Executes the provided action async.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async ValueTask<Result> ExecuteAsync(Func<ValueTask> action, CancellationToken ct = default)
        {
            if (TryGetDisposeError(out var ode))
                return Result.FromError("Unable to subscribe to state change events.", ode.ToString());

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
                    StateChangedEventManager.Notify();
                }
            }
            catch (OperationCanceledException)
            {
                return Result.FromCancellation();
            }
            catch (Exception ex)
            {
                return Result.FromError("Error executing action.", ex.ToString());
            }
        }

        /// <summary>
        /// Executes the provided action sync.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public Result Execute(Action action)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("Unable to execute action.", ode.ToString());

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
                    StateChangedEventManager.Notify();
                }
            }
            catch (OperationCanceledException)
            {
                return Result.FromCancellation();
            }
            catch (Exception ex)
            {
                return Result.FromError("Error executing action.", ex.ToString());
            }
        }

        /// <summary>
        /// Executes the provided action sync with return.
        /// </summary>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="action"></param>
        /// <returns></returns>
        public ValueResult<TReturn> Execute<TReturn>(Func<TReturn> action)
        {
            if (TryGetDisposeError(out var ode)) return ValueResult<TReturn>.FromError("Unable to execute action.", ode.ToString());

            try
            {
                _executeLock.Wait();

                try
                {
                    return action();
                }
                finally
                {
                    _executeLock.Release();
                    StateChangedEventManager.Notify();
                }
            }
            catch (OperationCanceledException)
            {
                return ValueResult<TReturn>.FromCancellation();
            }
            catch (Exception ex)
            {
                return ValueResult<TReturn>.FromError("Error executing action.", ex.ToString());
            }
        }

        /// <summary>
        /// Executes the action with exclusive read access to the game state.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public async ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("Unable to read values.", ode.ToString());

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
            catch (OperationCanceledException)
            {
                return Result.FromCancellation();
            }
            catch (Exception ex)
            {
                return Result.FromError("Error executing read.", ex.ToString());
            }
        }

        /// <summary>
        /// Executes the action with exclusive read access to the game state.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public Result WithExclusiveRead(Action action)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("Unable to read values.", ode.ToString());

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
            catch (OperationCanceledException)
            {
                return Result.FromCancellation();
            }
            catch (Exception ex)
            {
                return Result.FromError("Error executing read.", ex.ToString());
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
        public ValueResult<CancellationTokenSource> ScheduleCallback(TimeSpan delay, Func<Task> action)
        {
            if (TryGetDisposeError(out var ode))
            {
                logger.LogError(ode, "Error scheduling callback.");
                return ValueResult<CancellationTokenSource>.FromError("Unable to schedule callback.", ode.ToString());
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);

            lock (_scheduledLock)
            {
                _scheduledCallbacks.Add(cts);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);

                    if (_disposeCts.IsCancellationRequested)
                        return;

                    await ExecuteAsync(async () => await action(), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Silently discard if cancelled before or during execution.
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
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            try
            {
                OnStateDisposed?.Invoke();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invoking OnStateDisposed.");
            }

            _disposeCts.Cancel();

            CancellationTokenSource[] pendingCallbacks;

            lock (_scheduledLock)
            {
                pendingCallbacks = [.. _scheduledCallbacks];
                _scheduledCallbacks.Clear();
            }

            foreach (var cts in pendingCallbacks)
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { } // Ignore canceled and disposed exceptions
            }

            lock (_disposeLock)
            {
                _executeLock.Dispose();
                _disposeCts.Dispose();
            }

            logger.LogInformation("Game state [{type}] ended with host [{id}].", GetType().Name, Host.Id);

            GC.SuppressFinalize(this);
        }

        private bool TryGetDisposeError([NotNullWhen(true)] out ObjectDisposedException? disposeError)
        {
            disposeError = null;

            if (_disposed == 1)
            {
                disposeError = new ObjectDisposedException(GetType().Name);
                return true;
            }

            return false;
        }
    }
}
