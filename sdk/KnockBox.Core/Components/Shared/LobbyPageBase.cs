using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
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
        [Inject] protected ILoggerFactory LoggerFactory { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        protected ILogger Logger { get; private set; } = default!;
        protected TGameState GameState { get; private set; } = default!;
        protected string RoomCode { get; private set; } = string.Empty;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;
        private bool _kickHandled;
        private bool _initialized;

        protected override async Task OnInitializedAsync()
        {
            Logger = LoggerFactory.CreateLogger(GetType());

            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                Logger.LogWarning("User [{userId}] attempted to enter room [{code}] without a session set.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode);
                ReturnToHome();
                return;
            }

            if (!LobbyUriHelper.TryExtractObfuscatedRoomCode(session.LobbyRegistration.Uri, out var roomCode)
                || roomCode.Trim() != ObfuscatedRoomCode)
            {
                Logger.LogError("User [{userId}] attempted to enter room [{code}] but their session registration uri [{uri}] does not match.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode, session.LobbyRegistration.Uri);
                ReturnToHome();
                return;
            }

            if (session.LobbyRegistration.State is not TGameState gameState)
            {
                Logger.LogError("Game state for user [{userId}] is not of type {Type}.",
                    UserService.CurrentUser?.Id ?? "Unknown", typeof(TGameState).Name);
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
            GameState.OnStateDisposed += HandleStateDisposed;
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

        public override void Dispose()
        {
            OnLobbyDisposing();
            _tickSubscription?.Dispose();
            if (GameState is not null)
                GameState.OnStateDisposed -= HandleStateDisposed;
            _stateSubscription?.Dispose();
            base.Dispose();
        }

        protected bool IsHost()
            => GameState is not null
            && UserService.CurrentUser is not null
            && GameState.Host.Id == UserService.CurrentUser.Id;

        protected void ReturnToHome() => NavigationService.ToHome();

        private void HandleStateDisposed()
        {
            try
            {
                _ = HandleStateDisposedAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling game state disposal in lobby.");
            }
        }

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
