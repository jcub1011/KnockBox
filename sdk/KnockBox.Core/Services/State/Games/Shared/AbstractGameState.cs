using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Events;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Core.Services.State.Games.Shared
{
    /// <summary>
    /// Base class for per-room game state. One instance is created per lobby by
    /// the owning <c>AbstractGameEngine.CreateStateAsync</c>, stashed on the
    /// lobby's <c>LobbyRegistration</c>, and consumed by Razor pages and the
    /// engine's command methods.
    /// </summary>
    /// <remarks>
    /// <para><b>Concurrency contract:</b> mutations must flow through
    /// <see cref="Execute(Action)"/> / <c>ExecuteAsync</c>, which acquire a
    /// per-state <c>SemaphoreSlim(1,1)</c>, run the caller's lambda, release
    /// the lock, and <i>then</i> fire <see cref="StateChangedEventManager"/>.
    /// Non-mutating serialized reads use
    /// <c>WithExclusiveRead</c> / <c>WithExclusiveReadAsync</c> — those do not
    /// notify subscribers. Direct field writes from outside these helpers
    /// bypass both the lock and the notification and should be avoided.</para>
    /// <para><b>Why notification fires outside the lock:</b> to keep lock-hold
    /// time minimal and to let subscribers (e.g., disconnect handlers) call
    /// <c>Execute</c> reentrantly without deadlocking. The
    /// <see cref="PlayerUnregistered"/> event is raised with the same
    /// "outside-the-lock" guarantee for the same reason.</para>
    /// <para><b>Lifecycle:</b> the owning lobby disposes the state when the
    /// game ends or the host leaves; the <see cref="OnStateDisposed"/> event
    /// lets pages and background handlers unsubscribe cleanly.</para>
    /// </remarks>
    public abstract class AbstractGameState(User host, ILogger logger) : IDisposable
    {
        private readonly Lock _disposeLock = new();
        private readonly SemaphoreSlim _executeLock = new(1, 1);
        private readonly Lock _scheduledLock = new();
        private readonly List<CancellationTokenSource> _scheduledCallbacks = [];
        private readonly Lock _playerLock = new();
        private readonly Dictionary<string, (User User, IDisposable Token)> _players = [];
        private readonly Dictionary<string, User> _kickedPlayers = [];
        private readonly CancellationTokenSource _disposeCts = new();
        private int _disposed;

        /// <summary>
        /// Notifies all subscribers that the state has changed.
        /// </summary>
        protected void NotifyStateChanged()
        {
            StateChangedEventManager.Notify();
        }

        /// <summary>
        /// The UTC time when this state was created.
        /// </summary>
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        /// <summary>
        /// True if this state has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed == 1;

        /// <summary>
        /// Fired when the state is disposed.
        /// </summary>
        public event Action? OnStateDisposed;

        /// <summary>
        /// Fired after a player is successfully removed from this game (disconnected, left, or kicked).
        /// Raised outside the execute lock so subscribers may safely call <see cref="Execute"/>.
        /// </summary>
        public event Action<User>? PlayerUnregistered;

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
                if (_players.Count == 0) return [];
                var result = new User[_players.Count];
                int i = 0;
                foreach (var entry in _players.Values)
                    result[i++] = entry.User;
                return result;
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
                if (_kickedPlayers.Count == 0) return [];
                var result = new User[_kickedPlayers.Count];
                int i = 0;
                foreach (var user in _kickedPlayers.Values)
                    result[i++] = user;
                return result;
            }
        }

        /// <summary>
        /// Checks if a player has been kicked from this game.
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public bool IsKicked(User? user)
        {
            if (user is null) return false;
            using var scope = _playerLock.EnterScope();
            return _kickedPlayers.ContainsKey(user.Id);
        }

        /// <summary>
        /// Registers the player. Unregisters the player when the returned <see cref="IDisposable"/> is disposed.
        /// </summary>
        /// <remarks>
        /// <para>Runs the entire gate check (<see cref="IsJoinable"/>, host/kicked rejection, name collision)
        /// and the _players dictionary mutation inside a single <see cref="Execute(Action)"/> so that
        /// a concurrent <see cref="SetJoinable"/> cannot race with a late join. Callers must therefore
        /// <b>not</b> pre-wrap this call in their own <see cref="Execute(Action)"/>.</para>
        /// </remarks>
        public ValueResult<IDisposable> RegisterPlayer(User player)
        {
            if (TryGetDisposeError(out var ode))
                return ValueResult<IDisposable>.FromError("Error registering player.", ode.ToString());

            ValueResult<IDisposable> registration = default;
            bool registrationSet = false;

            var exec = Execute(() =>
            {
                if (!IsJoinable)
                {
                    registration = ValueResult<IDisposable>.FromError("The game is not currently joinable.");
                    registrationSet = true;
                    return;
                }

                if (Host.Id == player.Id)
                {
                    registration = ValueResult<IDisposable>.FromError("Host cannot be a player in the game.");
                    registrationSet = true;
                    return;
                }

                using var scope = _playerLock.EnterScope();
                if (_kickedPlayers.ContainsKey(player.Id))
                {
                    registration = ValueResult<IDisposable>.FromError("You have been kicked from this lobby and cannot rejoin.", $"Player [{player.Name}] was kicked and cannot rejoin.");
                    registrationSet = true;
                    return;
                }

                // Check for re-join to avoid renaming if the player is already in the lobby (by ID).
                bool isRejoin = _players.ContainsKey(player.Id);

                if (!isRejoin && IsNameTaken(player.Name))
                {
                    string originalName = player.Name;
                    int counter = 1;
                    while (true)
                    {
                        string suffix = $" ({counter})";
                        int maxBaseLength = 12 - suffix.Length;
                        string baseName = originalName.Length > maxBaseLength
                            ? originalName[..maxBaseLength]
                            : originalName;
                        string candidate = baseName + suffix;

                        if (!IsNameTaken(candidate))
                        {
                            player.Name = candidate;
                            break;
                        }
                        counter++;
                    }
                }

                bool IsNameTaken(string name)
                {
                    if (string.Equals(Host.Name, name, StringComparison.Ordinal)) return true;
                    foreach (var entry in _players.Values)
                    {
                        if (string.Equals(entry.User.Name, name, StringComparison.Ordinal))
                            return true;
                    }
                    return false;
                }

                // Self-reference allows the dispose closure to verify that this is still the
                // authoritative token for this player.  If the player re-registers before
                // disposing (e.g. re-joining from the home page during the grace period), the
                // old token is superseded and its dispose becomes a no-op, so the player is
                // not accidentally removed from the lobby.
                //
                // The variable must be declared nullable and assigned on the next line so that
                // the closure can capture the variable itself (not a value) and still see the
                // final reference once the constructor completes — a standard C# self-referential
                // closure pattern.
                DisposableAction? unsubscriber = null;
                unsubscriber = new DisposableAction(() =>
                {
                    bool shouldFire = false;
                    Execute(() =>
                    {
                        using var innerScope = _playerLock.EnterScope();
                        if (_players.TryGetValue(player.Id, out var current) && ReferenceEquals(current.Token, unsubscriber))
                        {
                            _players.Remove(player.Id);
                            shouldFire = true;
                        }
                    });
                    if (shouldFire) SafeInvoke(PlayerUnregistered, player, nameof(PlayerUnregistered));
                });

                _players[player.Id] = (player, unsubscriber);

                if (!isRejoin)
                    logger.LogInformation("User [{userId}] entered game [{type}] hosted by user [{hostId}].", player.Id, GetType().Name, Host.Id);
                else
                    logger.LogInformation("User [{userId}] rejoined game [{type}] hosted by user [{hostId}].", player.Id, GetType().Name, Host.Id);

                registration = unsubscriber;
                registrationSet = true;
            });

            if (exec.IsCanceled) return ValueResult<IDisposable>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<IDisposable>.FromError(execErr);
            if (!registrationSet) return ValueResult<IDisposable>.FromError("Player registration did not complete.");
            return registration;
        }

        /// <summary>
        /// Kicks the player.
        /// </summary>
        /// <remarks>
        /// The kicked-set mutation is routed through <see cref="Execute(Action)"/> so subscribers
        /// observe the change. The token is disposed *after* <see cref="Execute"/> returns to avoid
        /// re-entering the non-reentrant <see cref="_executeLock"/> — the dispose action itself
        /// re-enters <see cref="Execute"/> to remove the player from <see cref="_players"/>.
        /// </remarks>
        public Result KickPlayer(User player)
        {
            IDisposable? token = null;

            var result = Execute(() =>
            {
                using var scope = _playerLock.EnterScope();
                if (_players.TryGetValue(player.Id, out var registration))
                {
                    _kickedPlayers[player.Id] = player;
                    _players.Remove(player.Id);
                    token = registration.Token;
                }
            });

            if (!result.IsSuccess) return result;
            if (token is null) return Result.FromError("User is not in this game.");

            SafeInvoke(PlayerUnregistered, player, nameof(PlayerUnregistered));
            return Result.Success;
        }

        /// <summary>
        /// Sets the joinable status of the current game.
        /// </summary>
        /// <remarks>
        /// <b>Must be invoked from inside an <see cref="Execute(Action)"/> / <see cref="ExecuteAsync"/>
        /// block</b> so that readers which gate on <see cref="IsJoinable"/> (e.g. <see cref="RegisterPlayer"/>)
        /// are serialized against the transition and so <see cref="StateChangedEventManager"/>
        /// subscribers see the new value once the lock is released. Calling this outside the
        /// execute lock is a programmer error; the method itself does not take a lock.
        /// </remarks>
        public void SetJoinable(bool isJoinable)
        {
            IsJoinable = isJoinable;
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
                return Result.FromError("State was disposed.", ode.ToString());

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
            catch (ObjectDisposedException ode2)
            {
                return Result.FromError("State was disposed during execute.", ode2.ToString());
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
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

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
            catch (ObjectDisposedException ode2)
            {
                return Result.FromError("State was disposed during execute.", ode2.ToString());
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
            if (TryGetDisposeError(out var ode)) return ValueResult<TReturn>.FromError("State was disposed.", ode.ToString());

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
            catch (ObjectDisposedException ode2)
            {
                return ValueResult<TReturn>.FromError("State was disposed during execute.", ode2.ToString());
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
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

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
            catch (ObjectDisposedException ode2)
            {
                return Result.FromError("State was disposed during read.", ode2.ToString());
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
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

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
            catch (ObjectDisposedException ode2)
            {
                return Result.FromError("State was disposed during read.", ode2.ToString());
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
        /// The returned <see cref="IScheduledCallbackHandle"/> may be used to cancel the scheduled
        /// work before it runs. Both <see cref="IScheduledCallbackHandle.Cancel"/> and
        /// <see cref="IDisposable.Dispose"/> are idempotent and safe to call after the owning state
        /// has been disposed. All outstanding callbacks are automatically cancelled when the state
        /// is disposed.
        /// </remarks>
        /// <param name="delay">How long to wait before executing the action.</param>
        /// <param name="action">The action to run inside <see cref="ExecuteAsync"/>.</param>
        public ValueResult<IScheduledCallbackHandle> ScheduleCallback(TimeSpan delay, Func<Task> action)
        {
            if (TryGetDisposeError(out var ode))
            {
                logger.LogError(ode, "Error scheduling callback.");
                return ValueResult<IScheduledCallbackHandle>.FromError("Unable to schedule callback.", ode.ToString());
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

                    await ExecuteAsync(() => new ValueTask(action()), cts.Token);
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

            return new ScheduledCallbackHandle(cts);
        }

        /// <summary>
        /// Opaque handle returned by <see cref="ScheduleCallback"/>. Wraps the linked CTS so that
        /// callers can <see cref="Cancel"/> or <see cref="Dispose"/> idempotently without risk of
        /// double-disposing a CTS that is owned by the scheduling state.
        /// </summary>
        private sealed class ScheduledCallbackHandle : IScheduledCallbackHandle
        {
            private readonly CancellationTokenSource _cts;
            private int _disposed;

            public ScheduledCallbackHandle(CancellationTokenSource cts) => _cts = cts;

            public bool IsCancelled
            {
                get
                {
                    try { return _cts.IsCancellationRequested; }
                    catch (ObjectDisposedException) { return true; }
                }
            }

            public void Cancel()
            {
                try { _cts.Cancel(); }
                catch (ObjectDisposedException) { /* CTS already disposed by callback finally — cancellation implied. */ }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
                Cancel();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            SafeInvoke(OnStateDisposed, nameof(OnStateDisposed));

            // Null out event fields after firing so that delegate chains (which may hold
            // references to Blazor components or engine closures) are released promptly
            // rather than waiting for GC to detect the cycle.
            OnStateDisposed = null;
            PlayerUnregistered = null;

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

        /// <summary>
        /// Dispatches each subscriber in <paramref name="event"/> inside an independent try/catch so
        /// one throwing handler does not short-circuit the rest of the invocation list.
        /// </summary>
        private void SafeInvoke(Action? @event, string eventName)
        {
            if (@event is null) return;
            foreach (Action handler in @event.GetInvocationList().Cast<Action>())
            {
                try { handler(); }
                catch (Exception ex) { logger.LogError(ex, "Subscriber to [{Event}] threw.", eventName); }
            }
        }

        /// <summary>
        /// Dispatches each subscriber in <paramref name="event"/> inside an independent try/catch so
        /// one throwing handler does not short-circuit the rest of the invocation list.
        /// </summary>
        private void SafeInvoke<T>(Action<T>? @event, T arg, string eventName)
        {
            if (@event is null) return;
            foreach (Action<T> handler in @event.GetInvocationList().Cast<Action<T>>())
            {
                try { handler(arg); }
                catch (Exception ex) { logger.LogError(ex, "Subscriber to [{Event}] threw.", eventName); }
            }
        }
    }
}
