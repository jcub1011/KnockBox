using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Events;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Primitives.ThreadSafety;
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
    /// <see cref="PlayerUnregistered"/> event is raised with the same
    /// "outside-the-lock" guarantee for the same reason.</para>
    /// <para><b>Memory layout:</b> this class implements
    /// <see cref="IThreadSafeEventManager"/> directly to avoid allocating a
    /// separate manager object per lobby, and lazily allocates the scheduling
    /// subsystem (CTS + callback list) only on the first
    /// <see cref="ScheduleCallback"/> call. Player / kicked-user reads are
    /// served from volatile snapshot arrays rebuilt only when membership
    /// changes, so concurrent reads need no additional lock. There is no
    /// per-state player dictionary — all player lookups (register, kick,
    /// rejoin checks) scan the cached array directly, which is faster for
    /// the expected 4–8 player count and removes one heap object per
    /// lobby.</para>
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
    public abstract class AbstractGameState : IDisposable, IThreadSafeEventManager
    {
        // Single process-wide marker: holds the state whose Execute lambda is
        // currently running on this async flow. One AsyncLocal slot regardless
        // of how many states exist — avoids ExecutionContext bloat scaling
        // with lobby count.
        private static readonly AsyncLocal<AbstractGameState?> s_executingState = new();

        // Single shared sync root: guards listener mutations (subscribe/unsubscribe)
        // and lazy-init/mutation of _scheduledCallbacks. Player and kicked-set
        // mutations are serialized by _executeLock instead.
        private readonly Lock _syncRoot = new();
        // Async reader/writer lock — Execute/ExecuteAsync take the write
        // side, WithExclusiveRead/WithExclusiveReadAsync take the read side.
        // Multiple reads run concurrently; writers are exclusive and
        // preferred over later readers to avoid writer starvation.
        private readonly AsyncReaderWriterLock _executeLock = new();
        private readonly User _host;
        private readonly ILogger _logger;

        // Hot read: Players, RosterIncludingHost, KickedPlayers, IsKicked.
        // _cachedPlayerEntries is also the authoritative storage for the
        // player roster — mutations rebuild it; lookups scan it.
        private volatile PlayerEntry[] _cachedPlayerEntries = [];
        private volatile PlayerEntry[] _cachedRoster;
        // Equals _cachedPlayerEntries when HostIsParticipant is false; otherwise
        // {hostEntry, ...players}. Rebuilt on every player-set change and on every
        // HostIsParticipant toggle inside Execute.
        private volatile PlayerEntry[] _cachedParticipants = [];
        private volatile User[] _cachedKickedUsers = [];
        // Ids of every user that has successfully registered (and not been kicked).
        // Used by RegisterPlayer to admit lobby members back after IsJoinable goes
        // false, gated on AllowRejoinAfterStart. Mutated only inside Execute.
        private readonly HashSet<string> _everJoinedIds = new(StringComparer.Ordinal);

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
            // Participants default to "no host" — players-only view, identical to
            // _cachedPlayerEntries when HostIsParticipant is false.
            _cachedParticipants = [];
        }

        /// <summary>
        /// Notifies all subscribers that the state has changed.
        /// </summary>
        protected void NotifyStateChanged() => Notify();

        /// <summary>
        /// Replaces the cached player array with <paramref name="newEntries"/> and
        /// rebuilds the host-prepended roster in the same pass. The kicked-user
        /// snapshot is not touched here — kicks rebuild it inline.
        /// </summary>
        private void PublishPlayerEntries(PlayerEntry[] newEntries)
        {
            var roster = new PlayerEntry[newEntries.Length + 1];
            roster[0] = new PlayerEntry(_host, _host.Name, null);
            if (newEntries.Length > 0)
                Array.Copy(newEntries, 0, roster, 1, newEntries.Length);

            _cachedPlayerEntries = newEntries;
            _cachedRoster = roster;
            _cachedParticipants = _hostIsParticipant ? roster : newEntries;
        }

        /// <summary>
        /// Linear scan over <see cref="_cachedPlayerEntries"/> for a player by id.
        /// Faster and cheaper than a dictionary lookup at the expected 4–8 player count.
        /// </summary>
        private bool TryFindPlayerIndexUnsafe(string userId, out int index)
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
        /// The UTC time when this state was created.
        /// </summary>
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        /// <summary>
        /// True if this state has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed == 1;

        // Subscriber arrays for the two per-state signals. Snapshot-and-swap
        // under _syncRoot, same shape as the StateChanged listener list.
        // Replaces multicast delegate `event` fields (no more invocation-list
        // delegate-array churn per handler add/remove).
        private Action[] _stateDisposedListeners = [];
        private Action<User>[] _playerUnregisteredListeners = [];

        /// <summary>
        /// Subscribes to the "state disposed" signal. The returned
        /// <see cref="IDisposable"/> unsubscribes the handler when disposed.
        /// </summary>
        public IDisposable SubscribeStateDisposed(Action handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_syncRoot)
            {
                _stateDisposedListeners = ThreadSafeEventManagerHelper.AddListener(_stateDisposedListeners, handler);
            }
            return new DisposableAction(() =>
            {
                lock (_syncRoot)
                {
                    _stateDisposedListeners = ThreadSafeEventManagerHelper.RemoveListener(_stateDisposedListeners, handler);
                }
            });
        }

        /// <summary>
        /// Subscribes to the "player unregistered" signal. The handler is fired
        /// outside the execute lock so it may safely call <see cref="Execute"/>.
        /// The returned <see cref="IDisposable"/> unsubscribes the handler.
        /// </summary>
        public IDisposable SubscribePlayerUnregistered(Action<User> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_syncRoot)
            {
                _playerUnregisteredListeners = ThreadSafeEventManagerHelper.AddListener(_playerUnregisteredListeners, handler);
            }
            return new DisposableAction(() =>
            {
                lock (_syncRoot)
                {
                    _playerUnregisteredListeners = ThreadSafeEventManagerHelper.RemoveListener(_playerUnregisteredListeners, handler);
                }
            });
        }

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
        /// When <c>true</c>, players who successfully joined the lobby before
        /// <see cref="IsJoinable"/> was set to <c>false</c> may still re-register
        /// after game start (e.g. after a circuit drop past the reconnect grace
        /// window). Strangers — anyone who never joined the open lobby — are
        /// still rejected by the <c>!IsJoinable</c> gate, and kicked players are
        /// still blocked by the kicked-set. Defaults to <c>false</c> so existing
        /// games keep their current "no late joiners" behavior; opt in by
        /// overriding.
        /// </summary>
        protected virtual bool AllowRejoinAfterStart => false;

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

        // Backing field for HostIsParticipant. Mutated only inside Execute via
        // SetHostIsParticipant; PublishPlayerEntries reads it to choose between
        // the players-only or host-prepended snapshot.
        private bool _hostIsParticipant;

        /// <summary>
        /// When <c>true</c>, the host is treated as a game participant — appearing
        /// in <see cref="Participants"/> alongside registered players, and counting
        /// toward any participant-count check. Defaults to <c>false</c>, preserving
        /// the "host is the shared display" behavior. Toggled via
        /// <see cref="SetHostIsParticipant"/> from inside <see cref="Execute(Action)"/>.
        /// </summary>
        /// <remarks>
        /// The host stays a synthetic <see cref="PlayerEntry"/> with a null
        /// <c>Token</c> (just like <see cref="RosterIncludingHost"/>) — it is not
        /// registered through <see cref="RegisterPlayer"/>, and
        /// <see cref="PlayerUnregistered"/> never fires for the host. If the host's
        /// circuit drops mid-game the lobby is torn down by the session-level grace
        /// path, not by per-player disconnect handling.
        /// </remarks>
        public bool HostIsParticipant => _hostIsParticipant;

        /// <summary>
        /// Participants for gameplay purposes — equals <see cref="Players"/> when
        /// <see cref="HostIsParticipant"/> is <c>false</c>; otherwise
        /// <c>{hostEntry, ...Players}</c>. Returns a cached snapshot rebuilt on
        /// every player-set change or <see cref="SetHostIsParticipant"/> call —
        /// no allocation per read.
        /// </summary>
        public IReadOnlyList<PlayerEntry> Participants => _cachedParticipants;

        /// <summary>
        /// Sets <see cref="HostIsParticipant"/> and republishes the participant
        /// snapshot. Must be invoked from inside an <see cref="Execute(Action)"/>
        /// / <see cref="ExecuteAsync"/> block on this state so the transition is
        /// serialized with other player-set mutations and notification fires
        /// exactly once after the lock releases.
        /// </summary>
        protected void SetHostIsParticipant(bool value)
        {
            ThrowIfNotExecuting();
            if (_hostIsParticipant == value) return;
            _hostIsParticipant = value;
            _cachedParticipants = value ? _cachedRoster : _cachedPlayerEntries;
        }

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
        /// and the player-cache rebuild inside a single <see cref="Execute(Action)"/> so that
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

                if (IsKickedByIdUnsafe(player.Id))
                {
                    registration = ValueResult<IDisposable>.FromError("You have been kicked from this lobby and cannot rejoin.", $"Player [{player.Name}] was kicked and cannot rejoin.");
                    registrationSet = true;
                    return;
                }

                // Check for re-join to avoid renaming if the player is already in the lobby (by ID).
                var entries = _cachedPlayerEntries;
                bool isRejoin = TryFindPlayerIndexUnsafe(player.Id, out int existingIndex);
                PlayerEntry existingEntry = isRejoin ? entries[existingIndex] : default;

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
                    var current = _cachedPlayerEntries;
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (string.Equals(current[i].DisplayName, name, StringComparison.Ordinal))
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
                        if (TryFindPlayerIndexUnsafe(player.Id, out int idx)
                            && ReferenceEquals(_cachedPlayerEntries[idx].Token, unsubscriber))
                        {
                            // Build a new array without the removed entry.
                            var current = _cachedPlayerEntries;
                            var updated = new PlayerEntry[current.Length - 1];
                            if (idx > 0) Array.Copy(current, 0, updated, 0, idx);
                            if (idx < current.Length - 1) Array.Copy(current, idx + 1, updated, idx, current.Length - idx - 1);
                            PublishPlayerEntries(updated);
                            shouldFire = true;
                        }
                    });
                    if (shouldFire) InvokePlayerUnregistered(player);
                });

                // Build the new player array: either replace the existing slot (rejoin)
                // or append a fresh entry. Then republish (also rebuilds roster cache).
                PlayerEntry[] nextEntries;
                if (isRejoin)
                {
                    nextEntries = (PlayerEntry[])entries.Clone();
                    nextEntries[existingIndex] = new PlayerEntry(player, displayName, unsubscriber);
                }
                else
                {
                    nextEntries = new PlayerEntry[entries.Length + 1];
                    if (entries.Length > 0) Array.Copy(entries, nextEntries, entries.Length);
                    nextEntries[entries.Length] = new PlayerEntry(player, displayName, unsubscriber);
                }
                PublishPlayerEntries(nextEntries);
                _everJoinedIds.Add(player.Id);

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
        /// Kicks the player. Only the host may kick.
        /// </summary>
        /// <remarks>
        /// The kick mutates both the kicked-set and the player array inline inside
        /// <see cref="Execute(Action)"/> so subscribers observe a single consistent transition.
        /// The player's registration token is not disposed here — it is disposed later by the
        /// player's session lifecycle, at which point the token's self-check (matching its own
        /// reference against the current entry's <c>Token</c>) fails and the dispose becomes a
        /// no-op. <c>PlayerUnregistered</c> subscribers are invoked outside the execute lock so
        /// they may safely call <see cref="Execute(Action)"/>.
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
                if (TryFindPlayerIndexUnsafe(player.Id, out int idx))
                {
                    var current = _cachedPlayerEntries;
                    token = current[idx].Token;

                    // Append to the kicked snapshot.
                    var existing = _cachedKickedUsers;
                    var updatedKicked = new User[existing.Length + 1];
                    if (existing.Length > 0) Array.Copy(existing, updatedKicked, existing.Length);
                    updatedKicked[existing.Length] = player;
                    _cachedKickedUsers = updatedKicked;

                    // Drop the kicked user from the rejoin allowlist so the
                    // AllowRejoinAfterStart gate cannot bypass the kicked-set.
                    _everJoinedIds.Remove(player.Id);

                    // Build the new player array without the kicked entry, then republish.
                    var nextEntries = new PlayerEntry[current.Length - 1];
                    if (idx > 0) Array.Copy(current, 0, nextEntries, 0, idx);
                    if (idx < current.Length - 1) Array.Copy(current, idx + 1, nextEntries, idx, current.Length - idx - 1);
                    PublishPlayerEntries(nextEntries);
                }
            });

            if (!result.IsSuccess) return result;
            if (token is null) return Result.FromError("User is not in this game.");

            InvokePlayerUnregistered(player);
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
                await _executeLock.WaitWriteAsync(ct);
                var previous = s_executingState.Value;
                s_executingState.Value = this;

                try
                {
                    await action();
                    notify = true;
                    return Result.Success;
                }
                finally
                {
                    s_executingState.Value = previous;
                    _executeLock.ReleaseWrite();
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
                _executeLock.WaitWrite();
                var previous = s_executingState.Value;
                s_executingState.Value = this;

                try
                {
                    action();
                    notify = true;
                    return Result.Success;
                }
                finally
                {
                    s_executingState.Value = previous;
                    _executeLock.ReleaseWrite();
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
                _executeLock.WaitWrite();
                var previous = s_executingState.Value;
                s_executingState.Value = this;

                try
                {
                    var result = action();
                    notify = true;
                    return result;
                }
                finally
                {
                    s_executingState.Value = previous;
                    _executeLock.ReleaseWrite();
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
        /// Runs <paramref name="action"/> with a shared read lock held on
        /// the state — concurrent with any other readers, exclusive against
        /// writers. The lambda must not mutate state (use
        /// <see cref="ExecuteAsync(Func{ValueTask}, CancellationToken)"/>
        /// for that). Does not fire <see cref="StateChangedEventManager"/>.
        /// </summary>
        public async ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

            try
            {
                ct.ThrowIfCancellationRequested();
                await _executeLock.WaitReadAsync(ct);
                var previous = s_executingState.Value;
                s_executingState.Value = this;

                try
                {
                    await action();
                    return Result.Success;
                }
                finally
                {
                    s_executingState.Value = previous;
                    _executeLock.ReleaseRead();
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
        /// Runs <paramref name="action"/> with a shared read lock held on
        /// the state — concurrent with any other readers, exclusive against
        /// writers. The lambda must not mutate state (use
        /// <see cref="Execute(Action)"/> for that). Does not fire
        /// <see cref="StateChangedEventManager"/>.
        /// </summary>
        public Result WithExclusiveRead(Action action)
        {
            if (TryGetDisposeError(out var ode)) return Result.FromError("State was disposed.", ode.ToString());

            try
            {
                _executeLock.WaitRead();
                var previous = s_executingState.Value;
                s_executingState.Value = this;

                try
                {
                    action();
                    return Result.Success;
                }
                finally
                {
                    s_executingState.Value = previous;
                    _executeLock.ReleaseRead();
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

            InvokeStateDisposed();

            // Drop subscribers — they hold component / engine references via
            // captures; clear them promptly so the lobby release path doesn't
            // wait for GC to break the cycle.
            lock (_syncRoot)
            {
                _listeners = [];
                _stateDisposedListeners = [];
                _playerUnregisteredListeners = [];
            }

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
        /// Fires the "state disposed" signal. Reads a snapshot of the listener
        /// array (lock-free) and invokes each handler inside an independent
        /// try/catch so one throwing handler does not short-circuit the rest.
        /// </summary>
        private void InvokeStateDisposed()
        {
            var snapshot = _stateDisposedListeners;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i](); }
                catch (Exception ex) { _logger.LogError(ex, "Subscriber to [OnStateDisposed] threw."); }
            }
        }

        /// <summary>
        /// Fires the "player unregistered" signal with the given player. Same
        /// dispatch semantics as <see cref="InvokeStateDisposed"/>.
        /// </summary>
        private void InvokePlayerUnregistered(User player)
        {
            var snapshot = _playerUnregisteredListeners;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i](player); }
                catch (Exception ex) { _logger.LogError(ex, "Subscriber to [PlayerUnregistered] threw."); }
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
        /// thread; async handlers are awaited fire-and-forget. <b>Callers must only
        /// invoke this AFTER releasing <see cref="_executeLock"/></b> — see
        /// <see cref="Execute(Action)"/>'s finally block, which is the single
        /// in-class call site that satisfies that rule. Subscribers commonly call
        /// Blazor's <c>InvokeAsync</c> + <c>StateHasChanged</c>, and the
        /// resulting renderer work (including child-component disposal and JS
        /// interop teardown) runs synchronously on the calling dispatcher; doing
        /// that while holding the executeLock deadlocks.
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
