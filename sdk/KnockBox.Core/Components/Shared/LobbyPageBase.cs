using KnockBox.Core.Services.Browser;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Core.Components.Shared
{
    /// <summary>
    /// Base for game-lobby Razor pages. Centralizes the per-circuit lifecycle:
    /// user initialization, session lookup, URI/state validation, kick detection,
    /// and state-change subscription. Plugins extend by overriding the virtual
    /// hooks below; any lifecycle method may be further overridden when a plugin
    /// genuinely needs to bypass the default behavior.
    /// </summary>
    /// <typeparam name="TGameState">Plugin-specific state type.</typeparam>
    public abstract class LobbyPageBase<TGameState> : DisposableComponent
        where TGameState : AbstractGameState
    {
        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
        [Inject] protected INavigationService NavigationService { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ITickService TickService { get; set; } = default!;
        [Inject] protected IWakeLockService WakeLockService { get; set; } = default!;
        [Inject] protected IPlayLogService PlayLog { get; set; } = default!;
        [Inject] protected ILoggerFactory LoggerFactory { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        protected ILogger Logger { get; private set; } = default!;
        protected TGameState GameState { get; private set; } = default!;
        protected string RoomCode { get; private set; } = string.Empty;

        private IDisposable? _stateSubscription;
        private IDisposable? _stateDisposedSubscription;
        private IDisposable? _tickSubscription;
        private bool _kickHandled;
        private bool _initialized;
        private bool _wakeLockAcquired;
        private bool _playLogged;
        private bool _sawPreLog;

        protected override async Task OnInitializedAsync()
        {
            Logger = LoggerFactory.CreateLogger(GetType());

            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                Logger.LogWarning("User [{userId}] attempted to enter room [{code}] without a session set.",
                    UserService.CurrentUser?.Id.ToString() ?? "Unknown", ObfuscatedRoomCode);
                ReturnToHome();
                return;
            }

            if (!LobbyUriHelper.TryExtractObfuscatedRoomCode(session.LobbyRegistration.Uri, out var roomCode)
                || roomCode.Trim() != ObfuscatedRoomCode)
            {
                Logger.LogError("User [{userId}] attempted to enter room [{code}] but their session registration uri [{uri}] does not match.",
                    UserService.CurrentUser?.Id.ToString() ?? "Unknown", ObfuscatedRoomCode, session.LobbyRegistration.Uri);
                ReturnToHome();
                return;
            }

            if (session.LobbyRegistration.State is not TGameState gameState)
            {
                Logger.LogError("Game state for user [{userId}] is not of type {Type}.",
                    UserService.CurrentUser?.Id.ToString() ?? "Unknown", typeof(TGameState).Name);
                ReturnToHome();
                return;
            }

            if (gameState.IsDisposed)
            {
                ReturnToHome();
                return;
            }

            GameState = gameState;
            RoomCode = session.LobbyRegistration.Code;
            _stateDisposedSubscription = GameState.SubscribeStateDisposed(HandleStateDisposed);
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(OnStateChangedAsync);

            if (IsHost() && TryGetHostTick(out var tickAction, out var tickInterval))
            {
                var tickResult = TickService.RegisterTickCallback(tickAction, tickInterval);
                if (tickResult.TryGetSuccess(out var sub))
                    _tickSubscription = sub;
                else
                    Logger.LogError("Failed to register tick callback: {Error}", tickResult.Error);
            }

            _initialized = true;
            await OnLobbyInitializedAsync();
            await base.OnInitializedAsync();
        }

        /// <summary>
        /// Default state-change handler — invokes <c>StateHasChanged</c>. Override to
        /// add animation tracking, error-toast updates, etc.
        /// </summary>
        protected virtual async ValueTask OnStateChangedAsync()
            => await InvokeAsync(StateHasChanged);

        /// <summary>
        /// Override to opt into the host-only tick callback. Return <see langword="true"/>
        /// and provide the action + interval; return <see langword="false"/> for no tick.
        /// </summary>
        protected virtual bool TryGetHostTick(out Action action, out int tickInterval)
        {
            action = null!;
            tickInterval = 0;
            return false;
        }

        /// <summary>
        /// Override for plugin-specific init that runs after state subscription is wired.
        /// </summary>
        protected virtual Task OnLobbyInitializedAsync() => Task.CompletedTask;

        /// <summary>
        /// Override for plugin-specific cleanup. Runs before the base disposes the
        /// state subscription, tick subscription, and disposed handler.
        /// </summary>
        protected virtual void OnLobbyDisposing() { }

        protected override void OnAfterRender(bool firstRender)
        {
            if (!_kickHandled
                && _initialized
                && UserService.CurrentUser is not null
                && GameState.IsKicked(UserService.CurrentUser))
            {
                _kickHandled = true;
                GameSessionService.LeaveCurrentSession(navigateHome: true);
            }
            base.OnAfterRender(firstRender);
        }

        /// <remarks>
        /// Acquires the wake lock on the first render *after* <see cref="OnInitializedAsync"/>
        /// completes — not necessarily <paramref name="firstRender"/>, since async init can
        /// finish later. <c>_wakeLockAcquired</c> makes this idempotent across subsequent
        /// renders; <c>_kickHandled</c> excludes pages that are about to redirect home.
        /// The flag is set *before* the await to block re-entry from concurrent renders
        /// while the JS round-trip is in flight, and cleared on failure so a transient
        /// JSDisconnect/cancel doesn't permanently disable the lock for this page.
        /// </remarks>
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_initialized && !_kickHandled && !_wakeLockAcquired)
            {
                _wakeLockAcquired = true;
                var ok = await WakeLockService.AcquireAsync(ComponentDetached);
                if (!ok) _wakeLockAcquired = false;
            }

            await TryLogEndOfGameAsync();

            await base.OnAfterRenderAsync(firstRender);
        }

        /// <summary>
        /// Override to record a play-log entry when the game reaches its terminal
        /// phase. Return <see langword="null"/> while the game is still in progress;
        /// return a <see cref="GameLog"/> once it is over. The base logs the first
        /// non-null result exactly once, after a render in which the component was
        /// already interactive (so JS interop / localStorage are available). Use
        /// <see cref="IsHost"/> for the role and <c>GameState</c> for the results.
        /// </summary>
        protected virtual GameLog? BuildEndOfGamePlayLog() => null;

        /// <summary>
        /// Override for open-ended games that have no terminal phase, to record a
        /// play-log entry when the player leaves the room. Return <see langword="null"/>
        /// when the session had no activity worth logging. Called best-effort during
        /// disposal; on a hard disconnect the write may be dropped.
        /// </summary>
        protected virtual GameLog? BuildOnLeavePlayLog() => null;

        /// <remarks>
        /// Runs after every render. <c>_sawPreLog</c> is set on any render where the
        /// game is not yet over, so a player who reconnects within the grace window
        /// *after* the game already ended (and never observed the in-progress game)
        /// does not write a duplicate entry. <c>_playLogged</c> is set before the
        /// await to block re-entry from a concurrent render while the JS round-trip
        /// is in flight.
        /// </remarks>
        private async Task TryLogEndOfGameAsync()
        {
            if (_playLogged || !_initialized || _kickHandled) return;

            // Read the terminal state under the state read lock so the build can't
            // observe a torn/concurrently-mutated snapshot (honors AbstractGameState's
            // "all reads go through WithExclusiveRead" invariant). WithExclusiveReadAsync
            // no-ops gracefully on a disposed state, leaving log null.
            GameLog? log = null;
            await GameState.WithExclusiveReadAsync(() =>
            {
                log = BuildEndOfGamePlayLog();
                return ValueTask.CompletedTask;
            });

            if (log is null)
            {
                _sawPreLog = true;
                return;
            }

            if (!_sawPreLog) return;

            _playLogged = true;
            await PlayLog.StoreLogAsync(StampRole(log), ComponentDetached);
        }

        public override void Dispose()
        {
            OnLobbyDisposing();
            TryLogOnLeave();
            _tickSubscription?.Dispose();
            _stateDisposedSubscription?.Dispose();
            _stateSubscription?.Dispose();
            // Fire-and-forget is safe: ReleaseAsync logs and swallows all exceptions.
            _ = WakeLockService.ReleaseAsync();
            base.Dispose();
        }

        /// <remarks>
        /// Best-effort: on in-app navigation the circuit is still alive so the
        /// localStorage write lands; on a hard disconnect the JS interop fails and
        /// the service swallows it. Gated to players who actually entered the room
        /// (not those redirected by a failed session/URI check) and who weren't
        /// kicked, and only when an end-of-game entry wasn't already written.
        /// </remarks>
        private void TryLogOnLeave()
        {
            if (_playLogged || !_initialized || _kickHandled) return;

            // Read under the state read lock, matching TryLogEndOfGameAsync. The
            // synchronous WithExclusiveRead is used here because Dispose is sync;
            // it no-ops gracefully if the state is already disposed, leaving leaveLog null.
            GameLog? leaveLog = null;
            GameState.WithExclusiveRead(() => leaveLog = BuildOnLeavePlayLog());
            if (leaveLog is null) return;

            _playLogged = true;
            _ = PlayLog.StoreLogAsync(StampRole(leaveLog));
        }

        /// <summary>
        /// Returns <paramref name="log"/> with the <see cref="StandardMetadata.Role"/>
        /// entry stamped from <see cref="IsHost"/> (placed first so it leads the
        /// panel's capped preview). Games therefore never record their own role.
        /// </summary>
        private GameLog StampRole(GameLog log)
        {
            var role = IsHost() ? PlayLogRoles.Host : PlayLogRoles.Player;
            var metadata = new Dictionary<string, string>(log.Metadata.Count + 1, StringComparer.Ordinal);
            metadata.Set(StandardMetadata.Role, role);
            foreach (var kv in log.Metadata)
                metadata[kv.Key] = kv.Value;
            return log with { Metadata = metadata };
        }

        protected bool IsHost()
            => GameState is not null
            && UserService.CurrentUser is not null
            && GameState.Host.Id == UserService.CurrentUser.Id;

        protected void ReturnToHome() => NavigationService.ToHome();

        private void HandleStateDisposed() => _ = HandleStateDisposedAsync();

        private async Task HandleStateDisposedAsync()
        {
            try
            {
                await InvokeAsync(() =>
                {
                    // Clear the player's session so the disposed game state is not retained
                    // in GameSessionState after the game ends.
                    GameSessionService.LeaveCurrentSession(navigateHome: false);
                    NavigationService.ToHome();
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error navigating home after game state was disposed.");
            }
        }
    }
}
