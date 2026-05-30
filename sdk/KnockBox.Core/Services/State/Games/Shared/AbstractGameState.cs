using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Events;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Primitives.ThreadSafety;
using KnockBox.Core.Services.State.Users;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

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
    /// <see cref="Execute(Action)"/> / <c>ExecuteAsync</c>, which acquire
    /// the per-state lock in <i>write</i> mode (exclusive against all other
    /// writers and readers), run the caller's lambda, release the lock,
    /// and <i>then</i> fire state-change notifications via
    /// <see cref="StateChangedEventManager"/>. Non-mutating reads use
    /// <c>WithExclusiveRead</c> / <c>WithExclusiveReadAsync</c>, which
    /// acquire the lock in <i>read</i> mode — multiple readers run
    /// concurrently with each other, but a writer waits for all active
    /// readers to release before acquiring (and new readers queue behind
    /// any waiting writer, so writers do not starve under continuous read
    /// load). Read mode does not fire subscriber notifications. Direct
    /// field writes from outside these helpers bypass both the lock and
    /// the notification and should be avoided.</para>
    /// <para><b>Why notification fires outside the lock:</b> to keep lock-hold
    /// time minimal and to let subscribers (e.g., disconnect handlers) call
    /// <c>Execute</c> reentrantly without deadlocking. The
    /// <see cref="SubscribePlayerUnregistered"/> handlers are invoked with the
    /// same "outside-the-lock" guarantee for the same reason.</para>
    /// <para><b>Snapshot reads:</b> the roster (<see cref="Players"/>,
    /// <see cref="RosterIncludingHost"/>, <see cref="Participants"/>,
    /// <see cref="KickedPlayers"/>) is exposed as <see cref="ImmutableArray{T}"/>
    /// — published from inside <see cref="Execute(Action)"/> and read
    /// lock-free by external threads. Stale snapshots are always
    /// self-consistent because the immutable struct holds a single immutable
    /// reference. There is no per-state player dictionary — all player
    /// lookups (register, kick, rejoin checks) scan the cached array
    /// directly, which is faster for the expected 4–8 player count.</para>
    /// <para><b>"Inside Execute" detection</b> is provided by a single
    /// process-wide static <see cref="AsyncLocal{T}"/> stamped with the
    /// state currently executing on the calling async flow. As a result,
    /// <see cref="ThrowIfNotExecuting"/> requires the calling flow to be
    /// inside <i>this</i> state's <see cref="Execute(Action)"/> /
    /// <see cref="ExecuteAsync(Func{ValueTask}, CancellationToken)"/> —
    /// calling another state's <c>SetJoinable</c> from inside this state's
    /// Execute will <see cref="InvalidOperationException">throw</see>,
    /// because that is the lock-violation the assertion exists to catch.</para>
    /// </remarks>
    public abstract class AbstractGameState : IDisposable
    {
        // Single process-wide marker: holds the state whose Execute lambda is
        // currently running on this async flow. One AsyncLocal slot regardless
        // of how many states exist — avoids ExecutionContext bloat scaling
        // with lobby count.
        private static readonly AsyncLocal<AbstractGameState?> s_executingState = new();

        // Guards lazy-init and mutation of _scheduledCallbacks / _disposeCts.
        // Event subscriber lists are managed inside the ThreadSafeEventManager
        // instances themselves; player snapshots are serialized by _executeLock.
        private readonly Lock _syncRoot = new();

        // Async reader/writer lock — Execute/ExecuteAsync take the write
        // side, WithExclusiveRead/WithExclusiveReadAsync take the read side.
        private readonly AsyncReaderWriterLock _executeLock = new();
        private readonly User _host;
        private readonly ILogger _logger;

        private readonly ThreadSafeEventManager _stateChanged;
        private readonly ThreadSafeEventManager _stateDisposed;
        private readonly ThreadSafeEventManager<User> _playerUnregistered;

        // Roster snapshots. Mutated only inside Execute (write lock); read
        // lock-free by external threads. ImmutableArray is a struct over a
        // single T[] reference — assignment is observed atomically and the
        // payload is deeply immutable.
        private ImmutableArray<PlayerEntry> _cachedPlayerEntries = [];
        private ImmutableArray<PlayerEntry> _cachedRoster = [];
        private ImmutableArray<PlayerEntry> _cachedParticipants = [];
        private ImmutableArray<User> _cachedKickedUsers = [];

        // Ids of every user that has successfully registered (and not been kicked).
        // Read & mutated only inside Execute — no concurrent reader, so HashSet's
        // O(1) operations beat ImmutableHashSet's O(log n).
        private readonly HashSet<string> _everJoinedIds = new(StringComparer.Ordinal);

        // Lazily allocated — most states never schedule callbacks.
        private CancellationTokenSource? _disposeCts;
        private List<CancellationTokenSource>? _scheduledCallbacks;

        private bool _hostIsParticipant;
        private int _disposed;

        protected AbstractGameState(User host, ILogger logger)
        {
            _host = host;
            _logger = logger;
            _stateChanged = new ThreadSafeEventManager(logger);
            _stateDisposed = new ThreadSafeEventManager(logger);
            _playerUnregistered = new ThreadSafeEventManager<User>(logger);

            // Roster always contains the host at index 0; players are appended on join.
            _cachedRoster = [new PlayerEntry(host, host.Name, null)];

            // Seed the participant view from the subclass's pre-start default. The
            // engine usually overwrites this at game start via SetHostIsParticipant;
            // the default only governs the window before the game begins.
            _hostIsParticipant = DefaultHostIsParticipant;
            _cachedParticipants = _hostIsParticipant ? _cachedRoster : _cachedPlayerEntries;
        }

        /// <summary>
        /// The value <see cref="HostIsParticipant"/> takes before the game starts.
        /// Defaults to <c>false</c> (host is the shared display). Games where the host
        /// plays by default until <see cref="SetHostIsParticipant"/> fixes the value at
        /// game start can override this to <c>true</c>. The override must be a constant
        /// (it is read from the base constructor, before subclass fields initialize).
        /// </summary>
        protected virtual bool DefaultHostIsParticipant => false;

        /// <summary>
        /// The UTC time when this state was created.
        /// </summary>
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        /// <summary>
        /// True if this state has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed == 1;

        /// <summary>
        /// The host of the game.
        /// </summary>
        public User Host => _host;

        /// <summary>
        /// If this lobby is open for players to join.
        /// This does not indicate if there is room available, just that the game state is in the phase for players to join.
        /// </summary>
        public bool IsJoinable { get; private set; }

        /// <summary>
        /// When <c>true</c>, the host is treated as a game participant — appearing
        /// in <see cref="Participants"/> alongside registered players, and counting
        /// toward any participant-count check. The pre-start value comes from
        /// <see cref="DefaultHostIsParticipant"/> (<c>false</c> by default, preserving
        /// the "host is the shared display" behavior). Toggled via
        /// <see cref="SetHostIsParticipant"/> from inside <see cref="Execute(Action)"/>.
        /// </summary>
        public bool HostIsParticipant => _hostIsParticipant;

        /// <summary>
        /// The players in this game. Each entry carries both the authoritative
        /// <see cref="User"/> and the per-lobby <see cref="PlayerEntry.DisplayName"/>
        /// (which may differ from <c>User.Name</c> after disambiguation).
        /// </summary>
        public ImmutableArray<PlayerEntry> Players => _cachedPlayerEntries;

        /// <summary>
        /// The full roster for the lobby — <see cref="Host"/> first, then every
        /// registered player. The host's entry carries a null <c>Token</c> and a
        /// <c>DisplayName</c> equal to <c>Host.Name</c>.
        /// </summary>
        public ImmutableArray<PlayerEntry> RosterIncludingHost => _cachedRoster;

        /// <summary>
        /// Participants for gameplay purposes — equals <see cref="Players"/> when
        /// <see cref="HostIsParticipant"/> is <c>false</c>; otherwise
        /// <c>{hostEntry, ...Players}</c>.
        /// </summary>
        public ImmutableArray<PlayerEntry> Participants => _cachedParticipants;

        /// <summary>
        /// Players that have been kicked from this game.
        /// </summary>
        public ImmutableArray<User> KickedPlayers => _cachedKickedUsers;

        /// <summary>
        /// Raised when any state changes. Subscribers should call
        /// <see cref="IThreadSafeEventManager.Subscribe"/> and dispose the returned
        /// handle to unsubscribe. Notifications fire <i>after</i> the execute
        /// lock has been released.
        /// </summary>
        public IThreadSafeEventManager StateChangedEventManager => _stateChanged;

        /// <summary>
        /// When <c>true</c>, players who successfully joined the lobby before
        /// <see cref="IsJoinable"/> was set to <c>false</c> may still re-register
        /// after game start (e.g. after a circuit drop past the reconnect grace
        /// window). Strangers — anyone who never joined the open lobby — are
        /// still rejected by the <c>!IsJoinable</c> gate, and kicked players are
        /// still blocked by the kicked-set. Defaults to <c>false</c>; opt in by
        /// overriding.
        /// </summary>
        protected virtual bool AllowRejoinAfterStart => false;

        /// <summary>
        /// Subscribes to the "state disposed" signal. The handler is invoked
        /// when <see cref="Dispose"/> runs. The returned <see cref="IDisposable"/>
        /// unsubscribes the handler when disposed.
        /// </summary>
        public IDisposable SubscribeStateDisposed(Action handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            return _stateDisposed.Subscribe(() => { handler(); return ValueTask.CompletedTask; });
        }

        /// <summary>
        /// Subscribes to the "player unregistered" signal. The handler is fired
        /// outside the execute lock so it may safely call <see cref="Execute"/>.
        /// The returned <see cref="IDisposable"/> unsubscribes the handler.
        /// </summary>
        public IDisposable SubscribePlayerUnregistered(Action<User> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            return _playerUnregistered.Subscribe(user => { handler(user); return ValueTask.CompletedTask; });
        }

        /// <summary>
        /// Notifies all <see cref="StateChangedEventManager"/> subscribers that
        /// the state has changed. Callers must invoke this only after the execute
        /// lock has been released — the lock-discipline contract on
        /// <see cref="ThreadSafeEventManager.Notify"/> applies.
        /// </summary>
        protected void NotifyStateChanged() => _stateChanged.Notify();

        /// <summary>
        /// Throws if the code was reached outside of an <see cref="Execute"/> or
        /// <see cref="ExecuteAsync"/> wrapper <i>on this state</i>. Calling from
        /// inside a different state's Execute throws — that is the lock-violation
        /// the check is designed to detect.
        /// </summary>
        protected void ThrowIfNotExecuting()
        {
            if (!ReferenceEquals(s_executingState.Value, this))
                throw new InvalidOperationException("Code was reached outside of an Execute or ExecuteAsync wrapper on this state.");
        }

        /// <summary>
        /// Sets the joinable status of the current game. Must be invoked from inside
        /// an <see cref="Execute(Action)"/> / <see cref="ExecuteAsync"/> block.
        /// </summary>
        public void SetJoinable(bool isJoinable)
        {
            ThrowIfNotExecuting();
            IsJoinable = isJoinable;
        }

        /// <summary>
        /// Sets <see cref="HostIsParticipant"/> and republishes the participant
        /// snapshot. Must be invoked from inside an <see cref="Execute(Action)"/>
        /// block on this state. Public — like <see cref="SetJoinable"/> — so a
        /// game's engine can toggle it from outside the state class.
        /// </summary>
        public void SetHostIsParticipant(bool value)
        {
            ThrowIfNotExecuting();
            if (_hostIsParticipant == value) return;
            _hostIsParticipant = value;
            _cachedParticipants = value ? _cachedRoster : _cachedPlayerEntries;
        }

        /// <summary>
        /// Checks if a player has been kicked from this game.
        /// </summary>
        public bool IsKicked(User? user) =>
            user is not null && ContainsUserId(_cachedKickedUsers, user.Id);

        private static bool ContainsUserId(ImmutableArray<User> users, string userId)
        {
            for (int i = 0; i < users.Length; i++)
                if (string.Equals(users[i].Id, userId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Linear scan over the player snapshot for a player by id. Faster than
        /// a dictionary lookup at the expected 4–8 player count.
        /// </summary>
        private bool TryFindPlayerIndex(string userId, out int index)
        {
            var entries = _cachedPlayerEntries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].User.Id, userId, StringComparison.Ordinal))
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        /// <summary>
        /// Replaces the cached player array with <paramref name="newEntries"/> and
        /// rebuilds the host-prepended roster and participant view in the same pass.
        /// </summary>
        private void PublishPlayerEntries(ImmutableArray<PlayerEntry> newEntries)
        {
            _cachedPlayerEntries = newEntries;
            _cachedRoster = [new PlayerEntry(_host, _host.Name, null), .. newEntries];
            _cachedParticipants = _hostIsParticipant ? _cachedRoster : newEntries;
        }

        private bool IsNameTaken(string name)
        {
            if (string.Equals(_host.Name, name, StringComparison.Ordinal)) return true;
            var current = _cachedPlayerEntries;
            for (int i = 0; i < current.Length; i++)
                if (string.Equals(current[i].DisplayName, name, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Picks the first non-colliding "{base} ({n})" candidate, trimming
        /// the requested name to leave room for the suffix within the 12-char
        /// display limit. The base name is shortened — never the suffix — so
        /// every candidate ends in " (n)". An upper-bound on counter guarantees
        /// the loop terminates even under pathological collisions; the fallback
        /// uses a short random hex suffix.
        /// </summary>
        private string Disambiguate(string requestedName)
        {
            const int maxDisplayLength = 12;
            const int maxAttempts = 10_000;
            for (int counter = 1; counter <= maxAttempts; counter++)
            {
                string suffix = $" ({counter})";
                int maxBaseLength = maxDisplayLength - suffix.Length;
                string baseName = requestedName.Length > maxBaseLength
                    ? requestedName[..maxBaseLength]
                    : requestedName;
                string candidate = baseName + suffix;
                if (!IsNameTaken(candidate)) return candidate;
            }

            // Unreachable in practice (max 8 players), but kills the worst-case
            // infinite loop if IsNameTaken's invariants ever drift.
            string fallback = $"{requestedName[..Math.Min(requestedName.Length, 4)]}-{Guid.NewGuid():N}"[..maxDisplayLength];
            _logger.LogWarning("Disambiguate exhausted {max} attempts for [{name}]; falling back to [{fallback}].",
                maxAttempts, requestedName, fallback);
            return fallback;
        }

        /// <summary>
        /// Registers the player. Unregisters the player when the returned <see cref="IDisposable"/> is disposed.
        /// </summary>
        /// <remarks>
        /// Runs the entire gate check (<see cref="IsJoinable"/>, host/kicked rejection, name collision)
        /// and the player-cache rebuild inside a single <see cref="Execute(Action)"/>. Callers must not
        /// pre-wrap this call in their own <see cref="Execute(Action)"/>.
        /// </remarks>
        public ValueResult<IDisposable> RegisterPlayer(User player)
        {
            if (TryGetDisposeError(out var ode))
                return ValueResult<IDisposable>.FromError("Error registering player.", ode.ToString());

            ValueResult<IDisposable> registration = default;
            bool registrationSet = false;

            var exec = Execute(() =>
            {
                if (!IsJoinable && !(AllowRejoinAfterStart && _everJoinedIds.Contains(player.Id)))
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

                if (ContainsUserId(_cachedKickedUsers, player.Id))
                {
                    registration = ValueResult<IDisposable>.FromError("You have been kicked from this lobby and cannot rejoin.", $"Player [{player.Name}] was kicked and cannot rejoin.");
                    registrationSet = true;
                    return;
                }

                bool isRejoin = TryFindPlayerIndex(player.Id, out int existingIndex);

                // Compute the per-lobby display name locally. Never mutate
                // player.Name — that reference is shared with IUserService.
                string displayName = isRejoin
                    ? _cachedPlayerEntries[existingIndex].DisplayName
                    : (IsNameTaken(player.Name) ? Disambiguate(player.Name) : player.Name);

                // Self-reference allows the dispose closure to verify that this is still the
                // authoritative token for this player. If the player re-registers before
                // disposing (e.g. re-joining from the home page during the grace period), the
                // old token is superseded and its dispose becomes a no-op.
                DisposableAction? unsubscriber = null;
                unsubscriber = new DisposableAction(() =>
                {
                    bool shouldFire = false;
                    Execute(() =>
                    {
                        if (TryFindPlayerIndex(player.Id, out int idx)
                            && ReferenceEquals(_cachedPlayerEntries[idx].Token, unsubscriber))
                        {
                            PublishPlayerEntries(_cachedPlayerEntries.RemoveAt(idx));
                            shouldFire = true;
                        }
                    });
                    if (shouldFire) _playerUnregistered.Notify(player);
                });

                var newEntry = new PlayerEntry(player, displayName, unsubscriber);
                PublishPlayerEntries(isRejoin
                    ? _cachedPlayerEntries.SetItem(existingIndex, newEntry)
                    : _cachedPlayerEntries.Add(newEntry));
                _everJoinedIds.Add(player.Id);

                _logger.LogInformation(
                    isRejoin
                        ? "User [{userId}] rejoined game [{type}] hosted by user [{hostId}]."
                        : "User [{userId}] entered game [{type}] hosted by user [{hostId}].",
                    player.Id, GetType().Name, _host.Id);

                registration = unsubscriber;
                registrationSet = true;
            });

            if (exec.IsCanceled) return ValueResult<IDisposable>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<IDisposable>.FromError(execErr);
            if (!registrationSet) return ValueResult<IDisposable>.FromError("Player registration did not complete.");
            return registration;
        }

        /// <summary>
        /// Kicks the player. Only the host may kick.
        /// </summary>
        /// <remarks>
        /// The kick mutates both the kicked-set and the player array inline inside
        /// <see cref="Execute(Action)"/> so subscribers observe a single consistent transition.
        /// The player's registration token is not disposed here — it is disposed later by the
        /// player's session lifecycle, at which point the token's self-check fails and the
        /// dispose becomes a no-op. <see cref="SubscribePlayerUnregistered"/> handlers are
        /// invoked outside the execute lock so they may safely call <see cref="Execute(Action)"/>.
        /// </remarks>
        public Result KickPlayer(User caller, User player)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(player);
            if (caller.Id != _host.Id)
                return Result.FromError("Only the host may kick players.");

            IDisposable? token = null;

            var result = Execute(() =>
            {
                if (TryFindPlayerIndex(player.Id, out int idx))
                {
                    token = _cachedPlayerEntries[idx].Token;
                    _cachedKickedUsers = _cachedKickedUsers.Add(player);
                    // Drop the kicked user from the rejoin allowlist so the
                    // AllowRejoinAfterStart gate cannot bypass the kicked-set.
                    _everJoinedIds.Remove(player.Id);
                    PublishPlayerEntries(_cachedPlayerEntries.RemoveAt(idx));
                }
            });

            if (!result.IsSuccess) return result;
            if (token is null) return Result.FromError("User is not in this game.");

            _playerUnregistered.Notify(player);
            return Result.Success;
        }

        // ── Execute / Read entry points ──────────────────────────────────────────

        /// <summary>
        /// Executes the provided action sync, holding the write lock for its duration
        /// and firing state-change notifications after the lock is released.
        /// </summary>
        public Result Execute(Action action) => RunSync(action, writer: true);

        /// <summary>
        /// Executes the provided action async, holding the write lock for its duration
        /// and firing state-change notifications after the lock is released.
        /// </summary>
        public ValueTask<Result> ExecuteAsync(Func<ValueTask> action, CancellationToken ct = default) =>
            RunAsync(action, writer: true, ct);

        /// <summary>
        /// Runs <paramref name="action"/> with a shared read lock held on the state —
        /// concurrent with any other readers, exclusive against writers. The lambda
        /// must not mutate state. Does not fire <see cref="StateChangedEventManager"/>.
        /// </summary>
        public Result WithExclusiveRead(Action action) => RunSync(action, writer: false);

        /// <summary>
        /// Runs <paramref name="action"/> with a shared read lock held on the state —
        /// concurrent with any other readers, exclusive against writers. The lambda
        /// must not mutate state. Does not fire <see cref="StateChangedEventManager"/>.
        /// </summary>
        public ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default) =>
            RunAsync(action, writer: false, ct);

        /// <summary>
        /// Executes the provided action sync and returns its result. Holds the write
        /// lock for the duration and fires state-change notifications after release.
        /// </summary>
        public ValueResult<TReturn> Execute<TReturn>(Func<TReturn> action)
        {
            TReturn captured = default!;
            bool produced = false;
            var exec = RunSync(() => { captured = action(); produced = true; }, writer: true);

            if (exec.IsCanceled) return ValueResult<TReturn>.FromCancellation();
            if (exec.TryGetFailure(out var err)) return ValueResult<TReturn>.FromError(err);
            if (!produced) return ValueResult<TReturn>.FromError("Execute did not produce a value.");
            return captured;
        }

        // Shared try/catch/notify scaffolding for all four Execute/Read overloads.
        // The only differences between writer and reader paths are the lock side
        // taken, whether StateChanged fires on completion, and the error labels.

        private Result RunSync(Action action, bool writer)
        {
            if (TryGetDisposeError(out var ode))
                return Result.FromError("State was disposed.", ode.ToString());

            bool notify = false;
            try
            {
                if (writer) _executeLock.WaitWrite();
                else _executeLock.WaitRead();

                var previous = s_executingState.Value;
                s_executingState.Value = this;
                try
                {
                    action();
                    notify = writer;
                    return Result.Success;
                }
                finally
                {
                    s_executingState.Value = previous;
                    if (writer) _executeLock.ReleaseWrite();
                    else _executeLock.ReleaseRead();
                }
            }
            catch (OperationCanceledException) { return Result.FromCancellation(); }
            catch (ObjectDisposedException ode2)
            {
                return Result.FromError(
                    writer ? "State was disposed during execute." : "State was disposed during read.",
                    ode2.ToString());
            }
            catch (Exception ex)
            {
                return Result.FromError(
                    writer ? "Error executing action." : "Error executing read.",
                    ex.ToString());
            }
            finally { if (notify) _stateChanged.Notify(); }
        }

        private async ValueTask<Result> RunAsync(Func<ValueTask> action, bool writer, CancellationToken ct)
        {
            if (TryGetDisposeError(out var ode))
                return Result.FromError("State was disposed.", ode.ToString());

            bool notify = false;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (writer) await _executeLock.WaitWriteAsync(ct);
                else await _executeLock.WaitReadAsync(ct);

                var previous = s_executingState.Value;
                s_executingState.Value = this;
                try
                {
                    await action();
                    notify = writer;
                    return Result.Success;
                }
                finally
                {
                    s_executingState.Value = previous;
                    if (writer) _executeLock.ReleaseWrite();
                    else _executeLock.ReleaseRead();
                }
            }
            catch (OperationCanceledException) { return Result.FromCancellation(); }
            catch (ObjectDisposedException ode2)
            {
                return Result.FromError(
                    writer ? "State was disposed during execute." : "State was disposed during read.",
                    ode2.ToString());
            }
            catch (Exception ex)
            {
                return Result.FromError(
                    writer ? "Error executing action." : "Error executing read.",
                    ex.ToString());
            }
            finally { if (notify) _stateChanged.Notify(); }
        }

        // ── Scheduling ───────────────────────────────────────────────────────────

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

            lock (_syncRoot) { _scheduledCallbacks!.Add(cts); }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);

                    if (disposeCts.IsCancellationRequested)
                        return;

                    await ExecuteAsync(() => new ValueTask(action()), cts.Token);
                }
                catch (OperationCanceledException) { /* dropped silently */ }
                catch (Exception ex) { _logger.LogError(ex, "Error executing scheduled callback."); }
                finally
                {
                    lock (_syncRoot) { _scheduledCallbacks?.Remove(cts); }
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

        private sealed class ScheduledCallbackHandle(CancellationTokenSource cts) : IScheduledCallbackHandle
        {
            private int _disposed;

            public bool IsCancelled
            {
                get
                {
                    try { return cts.IsCancellationRequested; }
                    catch (ObjectDisposedException) { return true; }
                }
            }

            public void Cancel()
            {
                try { cts.Cancel(); }
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

            _stateDisposed.Notify();

            // Drop subscribers now that the disposed signal has fired — their captures
            // hold component/engine references, so clearing promptly breaks the
            // state↔subscriber cycle instead of waiting for GC.
            _stateChanged.Clear();
            _playerUnregistered.Clear();
            _stateDisposed.Clear();

            CancellationTokenSource? disposeCts;
            ImmutableArray<CancellationTokenSource> pendingCallbacks;
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
                try { cts.Cancel(); cts.Dispose(); }
                catch { /* already disposed or canceled */ }
            }

            _executeLock.Dispose();
            disposeCts?.Dispose();

            _logger.LogInformation("Game state [{type}] ended with host [{id}].", GetType().Name, _host.Id);
            GC.SuppressFinalize(this);
        }

        private bool TryGetDisposeError([NotNullWhen(true)] out ObjectDisposedException? disposeError)
        {
            if (_disposed == 1)
            {
                disposeError = new ObjectDisposedException(GetType().Name);
                return true;
            }
            disposeError = null;
            return false;
        }
    }
}
