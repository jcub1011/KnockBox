using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Events;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace KnockBox.Core.Services.State.Games.Shared
{
    /// <summary>
    /// A seat in a lobby's roster: the underlying <see cref="User"/>
    /// (authoritative identity, owned by <c>IUserService</c>), the
    /// <see cref="DisplayName"/> used for this lobby only (may differ from
    /// <c>User.Name</c> after disambiguation of colliding names), and an
    /// optional <see cref="Token"/> that unregisters the player when disposed.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a separate <see cref="DisplayName"/>:</b> two players in
    /// the same lobby with the same <c>User.Name</c> would otherwise render
    /// identically. The state appends " (1)", " (2)", … to the second and
    /// subsequent occurrences. That rename applies <i>only</i> to this lobby;
    /// the underlying <c>User</c> is not mutated, so other lobbies and the
    /// user's global identity (<c>IUserService.CurrentUser</c>) are
    /// unaffected.</para>
    /// <para><b>When is <see cref="Token"/> null:</b> registered players always
    /// have a non-null token — the owner disposes it to leave the lobby.
    /// Synthetic entries yielded by <see cref="AbstractGameState.RosterIncludingHost"/>
    /// for the host carry a null token, since the host isn't a registered
    /// player and has no unregistration handle.</para>
    /// </remarks>
    public readonly record struct PlayerEntry(User User, string DisplayName, IDisposable? Token);

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
    /// the lock, and <i>then</i> fire state-change notifications via
    /// <see cref="StateChangedEventManager"/>. Non-mutating serialized reads use
    /// <c>WithExclusiveRead</c> / <c>WithExclusiveReadAsync</c> — those do not
    /// notify subscribers. Direct field writes from outside these helpers
    /// bypass both the lock and the notification and should be avoided.</para>
    /// <para><b>Why notification fires outside the lock:</b> to keep lock-hold
    /// time minimal and to let subscribers (e.g., disconnect handlers) call
    /// <c>Execute</c> reentrantly without deadlocking. The
    /// <see cref="PlayerUnregistered"/> event is raised with the same
    /// "outside-the-lock" guarantee for the same reason.</para>
    /// <para><b>Memory layout:</b> this class implements
    /// <see cref="IThreadSafeEventManager"/> directly to avoid allocating a
    /// separate manager object per lobby, and lazily allocates the scheduling
    /// subsystem (CTS + callback list) only on the first
    /// <see cref="ScheduleCallback"/> call. Player / kicked-user reads are
    /// served from volatile snapshot arrays rebuilt only when membership
    /// changes, so concurrent reads need no additional lock.</para>
    /// </remarks>
    public abstract class AbstractGameState : IDisposable, IThreadSafeEventManager
    {
        // Single shared sync root: guards listener mutations (subscribe/unsubscribe)
        // and lazy-init/mutation of _scheduledCallbacks. Player and kicked-set
        // mutations are serialized by _executeLock instead.
        private readonly Lock _syncRoot = new();
        private readonly AsyncLocal<bool> _isExecuting = new();
        private readonly SemaphoreSlim _executeLock = new(1, 1);
        private readonly User _host;
        private readonly ILogger _logger;

        // Mutated only inside Execute. Read by IsKicked/KickedPlayers via volatile snapshot fields.
        private readonly Dictionary<string, PlayerEntry> _players = [];
        // Hot read: Players, RosterIncludingHost, KickedPlayers, IsKicked.
        private volatile PlayerEntry[] _cachedPlayerEntries = [];
        private volatile PlayerEntry[] _cachedRoster;
        private volatile User[] _cachedKickedUsers = [];

        // Inlined IThreadSafeEventManager state. Snapshot is read lock-free; writes
        // copy-on-swap under _syncRoot.
        private Func<ValueTask>[] _listeners = [];

        // Lazily allocated — most states never schedule callbacks. Both fields are
        // published via _syncRoot the first time ScheduleCallback runs.
        private CancellationTokenSource? _disposeCts;
        private List<CancellationTokenSource>? _scheduledCallbacks;

        private int _disposed;

        protected AbstractGameState(User host, ILogger logger)
        {
            _host = host;
            _logger = logger;
            // Roster always contains the host at index 0; players are appended on join.
            _cachedRoster = [new PlayerEntry(host, host.Name, null)];
        }

        /// <summary>
        /// Notifies all subscribers that the state has changed.
        /// </summary>
        protected void NotifyStateChanged() => Notify();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdatePlayerCacheUnsafe()
        {
            ThrowIfNotExecuting();

            var count = _players.Count;
            var entries = count == 0 ? [] : new PlayerEntry[count];
            var roster = new PlayerEntry[count + 1];
            roster[0] = new PlayerEntry(_host, _host.Name, null);

            int i = 0;
            foreach (var entry in _players.Values)
            {
                entries[i] = entry;
                roster[i + 1] = entry;
                i++;
            }

            _cachedPlayerEntries = entries;
            _cachedRoster = roster;
        }

        /// <summary>
        /// Throws if the code was reached outside of an <see cref="Execute"/> or <see cref="ExecuteAsync"/> wrapper.
        /// </summary>
        protected void ThrowIfNotExecuting()
        {
            if (!_isExecuting.Value) throw new InvalidOperationException("Code was reached outside of an Execute or ExecuteAsync wrapper.");
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
        /// Raises when any state changes. The state manages its own subscriber
        /// list (this property returns <c>this</c>) so no additional event-manager
        /// object is allocated per lobby.
        /// </summary>
        public IThreadSafeEventManager StateChangedEventManager => this;

        /// <summary>
        /// If this lobby is open for players to join.
        /// This does not indicate if there is room available, just that the game state is in the phase for players to join.
        /// </summary>
        public bool IsJoinable { get; private set; }

        /// <summary>
        /// The host of the game.
        /// </summary>
        public User Host => _host;

        /// <summary>
        /// The players in this game. Each entry carries both the authoritative
        /// <see cref="User"/> and the per-lobby <see cref="PlayerEntry.DisplayName"/>
        /// (which may differ from <c>User.Name</c> after disambiguation).
        /// </summary>
        public IReadOnlyList<PlayerEntry> Players => _cachedPlayerEntries;

        /// <summary>
        /// The full roster for the lobby — <see cref="Host"/> first, then every
        /// registered player. The host's entry carries a null <c>Token</c>
        /// (the host isn't a registered player) and a <c>DisplayName</c> equal
        /// to <c>Host.Name</c>; player entries carry the per-lobby
        /// disambiguated <c>DisplayName</c>. Returns a cached snapshot — no
        /// allocation per read.
        /// </summary>
        public IReadOnlyList<PlayerEntry> RosterIncludingHost => _cachedRoster;

        /// <summary>
        /// Players that have been kicked from this game. Reads return a cached
        /// snapshot rebuilt on kick; no allocation per read.
        /// </summary>
        public IReadOnlyList<User> KickedPlayers => _cachedKickedUsers;

        /// <summary>
        /// Checks if a player has been kicked from this game.
        /// </summary>
        public bool IsKicked(User? user)
        {
            if (user is null) return false;
            var snapshot = _cachedKickedUsers;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(snapshot[i].Id, user.Id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private bool IsKickedByIdUnsafe(string userId)
        {
            // Read inside Execute; snapshot is stable for the duration.
            var snapshot = _cachedKickedUsers;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(snapshot[i].Id, userId, StringComparison.Ordinal))
                    return true;
            }
            return false;
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

                if (_host.Id == player.Id)
                {
                    registration = ValueResult<IDisposable>.FromError("Host cannot be a player in the game.");
                    registrationSet = true;
                    return;
                }

                if (IsKickedByIdUnsafe(player.Id))
                {
                    registration = ValueResult<IDisposable>.FromError("You have been kicked from this lobby and cannot rejoin.", $"Player [{player.Name}] was kicked and cannot rejoin.");
                    registrationSet = true;
                    return;
                }

                // Check for re-join to avoid renaming if the player is already in the lobby (by ID).
                bool isRejoin = _players.TryGetValue(player.Id, out var existingEntry);

                // Compute the per-lobby display name locally. Never mutate
                // player.Name — that reference is shared with IUserService.
                string displayName = isRejoin ? existingEntry.DisplayName : player.Name;

                if (!isRejoin && IsNameTaken(displayName))
                {
                    string originalName = displayName;
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
                            displayName = candidate;
                            break;
                        }
                        counter++;
                    }
                }

                bool IsNameTaken(string name)
                {
                    if (string.Equals(_host.Name, name, StringComparison.Ordinal)) return true;
                    foreach (var entry in _players.Values)
                    {
                        if (string.Equals(entry.DisplayName, name, StringComparison.Ordinal))
                            return true;
                    }
                    return false;
                }

                // Self-reference allows the dispose closure to verify that this is still the
                // authoritative token for this player. If the player re-registers before
                // disposing (e.g. re-joining from the home page during the grace period), the
                // old token is superseded and its dispose becomes a no-op, so the player is
                // not accidentally removed from the lobby.
                DisposableAction? unsubscriber = null;
                unsubscriber = new DisposableAction(() =>
                {
                    bool shouldFire = false;
                    Execute(() =>
                    {
                        if (_players.TryGetValue(player.Id, out var current) && ReferenceEquals(current.Token, unsubscriber))
                        {
                            _players.Remove(player.Id);
                            UpdatePlayerCacheUnsafe();
                            shouldFire = true;
                        }
                    });
                    if (shouldFire) SafeInvoke(PlayerUnregistered, player, nameof(PlayerUnregistered));
                });

                _players[player.Id] = new PlayerEntry(player, displayName, unsubscriber);
                UpdatePlayerCacheUnsafe();

                if (!isRejoin)
                    _logger.LogInformation("User [{userId}] entered game [{type}] hosted by user [{hostId}].", player.Id, GetType().Name, _host.Id);
                else
                    _logger.LogInformation("User [{userId}] rejoined game [{type}] hosted by user [{hostId}].", player.Id, GetType().Name, _host.Id);

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
                if (_players.TryGetValue(player.Id, out var registration))
                {
                    // Append to the kicked snapshot, then remove from the player dict.
                    var existing = _cachedKickedUsers;
                    var updated = new User[existing.Length + 1];
                    if (existing.Length > 0) Array.Copy(existing, updated, existing.Length);
                    updated[existing.Length] = player;
                    _cachedKickedUsers = updated;

                    _players.Remove(player.Id);
                    UpdatePlayerCacheUnsafe();
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
        /// execute lock throws <see cref="InvalidOperationException"/> in every build — the race
        /// is a programmer error either way.
        /// </remarks>
        public void SetJoinable(bool isJoinable)
        {
            ThrowIfNotExecuting();
            IsJoinable = isJoinable;
        }

        /// <summary>
        /// Executes the provided action async.
        /// </summary>
        public async ValueTask<Result> ExecuteAsync(Func<ValueTask> action, CancellationToken ct = default)
        {
            if (TryGetDisposeError(out var ode))
                return Result.FromError("State was disposed.", ode.ToString());

            bool notify = false;
            try
            {
                ct.ThrowIfCancellationRequested();
                await _executeLock.WaitAsync(ct);
                _isExecuting.Value = true;

                try
                {
                    await action();
                    notify = true;
                    return Result.Success;
                }
                finally
                {
                    _isExecuting.Value = false;
                    _executeLock.Release();
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
            finally
            {
                if (notify) Notify();
            }
        }

        /// <summary>
        /// Executes the provided action sync.
        /// </summary>
        public Result Execute(Action action)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

            bool notify = false;
            try
            {
                _executeLock.Wait();
                _isExecuting.Value = true;

                try
                {
                    action();
                    notify = true;
                    return Result.Success;
                }
                finally
                {
                    _isExecuting.Value = false;
                    _executeLock.Release();
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
            finally
            {
                if (notify) Notify();
            }
        }

        /// <summary>
        /// Executes the provided action sync with return.
        /// </summary>
        public ValueResult<TReturn> Execute<TReturn>(Func<TReturn> action)
        {
            if (TryGetDisposeError(out var ode)) return ValueResult<TReturn>.FromError("State was disposed.", ode.ToString());

            bool notify = false;
            try
            {
                _executeLock.Wait();
                _isExecuting.Value = true;

                try
                {
                    var result = action();
                    notify = true;
                    return result;
                }
                finally
                {
                    _isExecuting.Value = false;
                    _executeLock.Release();
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
            finally
            {
                if (notify) Notify();
            }
        }

        /// <summary>
        /// Executes the action with exclusive read access to the game state.
        /// </summary>
        public async ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

            try
            {
                ct.ThrowIfCancellationRequested();
                await _executeLock.WaitAsync(ct);
                _isExecuting.Value = true;

                try
                {
                    await action();
                    return Result.Success;
                }
                finally
                {
                    _isExecuting.Value = false;
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
        public Result WithExclusiveRead(Action action)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

            try
            {
                _executeLock.Wait();
                _isExecuting.Value = true;

                try
                {
                    action();
                    return Result.Success;
                }
                finally
                {
                    _isExecuting.Value = false;
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
        /// is disposed. The scheduling subsystem (CTS + list) is allocated lazily on first call,
        /// so states that never schedule pay no overhead.
        /// </remarks>
        public ValueResult<IScheduledCallbackHandle> ScheduleCallback(TimeSpan delay, Func<Task> action)
        {
            if (TryGetDisposeError(out var ode))
            {
                _logger.LogError(ode, "Error scheduling callback.");
                return ValueResult<IScheduledCallbackHandle>.FromError("Unable to schedule callback.", ode.ToString());
            }

            var disposeCts = EnsureScheduling();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token);

            lock (_syncRoot)
            {
                _scheduledCallbacks!.Add(cts);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);

                    if (disposeCts.IsCancellationRequested)
                        return;

                    await ExecuteAsync(() => new ValueTask(action()), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Silently discard if cancelled before or during execution.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing scheduled callback.");
                }
                finally
                {
                    lock (_syncRoot)
                    {
                        _scheduledCallbacks?.Remove(cts);
                    }
                    cts.Dispose();
                }
            });

            return new ScheduledCallbackHandle(cts);
        }

        private CancellationTokenSource EnsureScheduling()
        {
            var existing = Volatile.Read(ref _disposeCts);
            if (existing is not null) return existing;

            lock (_syncRoot)
            {
                if (_disposeCts is null)
                {
                    _scheduledCallbacks = [];
                    _disposeCts = new CancellationTokenSource();
                }
                return _disposeCts;
            }
        }

        /// <summary>
        /// Opaque handle returned by <see cref="ScheduleCallback"/>.
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
            // Drop subscribers too — they hold component references via captures.
            lock (_syncRoot) { _listeners = []; }

            CancellationTokenSource? disposeCts;
            CancellationTokenSource[] pendingCallbacks;
            lock (_syncRoot)
            {
                disposeCts = _disposeCts;
                pendingCallbacks = _scheduledCallbacks is { Count: > 0 } list
                    ? [.. list]
                    : [];
                _scheduledCallbacks?.Clear();
            }

            disposeCts?.Cancel();

            foreach (var cts in pendingCallbacks)
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { } // Ignore canceled and disposed exceptions
            }

            _executeLock.Dispose();
            disposeCts?.Dispose();

            _logger.LogInformation("Game state [{type}] ended with host [{id}].", GetType().Name, _host.Id);

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
            var invocationList = @event.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try { ((Action)invocationList[i])(); }
                catch (Exception ex) { _logger.LogError(ex, "Subscriber to [{Event}] threw.", eventName); }
            }
        }

        /// <summary>
        /// Dispatches each subscriber in <paramref name="event"/> inside an independent try/catch so
        /// one throwing handler does not short-circuit the rest of the invocation list.
        /// </summary>
        private void SafeInvoke<T>(Action<T>? @event, T arg, string eventName)
        {
            if (@event is null) return;
            var invocationList = @event.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try { ((Action<T>)invocationList[i])(arg); }
                catch (Exception ex) { _logger.LogError(ex, "Subscriber to [{Event}] threw.", eventName); }
            }
        }

        // ── IThreadSafeEventManager ──────────────────────────────────────────────

        IDisposable IThreadSafeEventManager.Subscribe(Func<ValueTask> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_syncRoot)
            {
                _listeners = ThreadSafeEventManagerHelper.AddListener(_listeners, callback);
            }

            return new DisposableAction(() =>
            {
                lock (_syncRoot)
                {
                    _listeners = ThreadSafeEventManagerHelper.RemoveListener(_listeners, callback);
                }
            });
        }

        Task IThreadSafeEventManager.NotifyAsync()
        {
            Func<ValueTask>[] snapshot;
            lock (_syncRoot) { snapshot = _listeners; }
            return ThreadSafeEventManagerHelper.DispatchAsync(snapshot, SafeInvokeListenerAsync);
        }

        void IThreadSafeEventManager.Notify() => Notify();

        /// <summary>
        /// Dispatches the state-change notification. Sync handlers run on the calling
        /// thread (after the execute lock has been released); async handlers are
        /// awaited fire-and-forget so the caller is never blocked by a slow subscriber.
        /// No <c>Task.Run</c> closure is allocated on the sync-completion path.
        /// </summary>
        private void Notify()
        {
            var snapshot = Volatile.Read(ref _listeners);
            if (snapshot.Length == 0) return;

            for (int i = 0; i < snapshot.Length; i++)
            {
                ValueTask vt;
                try
                {
                    vt = snapshot[i]();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying subscriber.");
                    continue;
                }

                if (vt.IsCompletedSuccessfully) continue;
                _ = AwaitListenerAsync(vt);
            }
        }

        private async Task AwaitListenerAsync(ValueTask vt)
        {
            try { await vt.ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogError(ex, "Error notifying subscriber."); }
        }

        private Task SafeInvokeListenerAsync(Func<ValueTask> callback)
        {
            try
            {
                var vt = callback();
                if (vt.IsCompletedSuccessfully) return Task.CompletedTask;
                return AwaitListenerAsync(vt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying subscriber.");
                return Task.CompletedTask;
            }
        }
    }
}
