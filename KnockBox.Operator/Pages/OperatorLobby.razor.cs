using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Operator.Pages
{
    public partial class OperatorLobby : DisposableComponent
    {
        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ITickService TickService { get; set; } = default!;

        [Inject] protected ILogger<OperatorLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;
        private bool _kickHandled;

        // ── Error toast state ─────────────────────────────────────────────────
        private string? _errorMessage;
        private int _errorKey;

        protected OperatorGameState? GameState { get; set; }

        protected override async Task OnInitializedAsync()
        {
            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                ReturnToHome();
                return;
            }

            if (session.LobbyRegistration.State is not OperatorGameState gameState)
            {
                Logger.LogError("Game state is not of type {Type}", nameof(OperatorGameState));
                ReturnToHome();
                return;
            }

            GameState = gameState;

            GameState.OnStateDisposed += HandleGameStateDisposed;

            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
            {
                await InvokeAsync(StateHasChanged);
            });

            if (IsHost())
            {
                var tickResult = TickService.RegisterTickCallback(() =>
                {
                    if (GameState?.Context is not null)
                        GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
                }, tickInterval: TickService.TicksPerSecond);

                if (tickResult.TryGetSuccess(out var sub))
                    _tickSubscription = sub;
                else
                    Logger.LogError("Failed to register tick callback: {Error}", tickResult.Error);
            }

            await base.OnInitializedAsync();
        }

        protected override void OnAfterRender(bool firstRender)
        {
            if (!_kickHandled && GameState?.IsKicked(UserService.CurrentUser!) == true)
            {
                _kickHandled = true;
                GameSessionService.LeaveCurrentSession(navigateHome: true);
            }

            base.OnAfterRender(firstRender);
        }

        public override void Dispose()
        {
            _tickSubscription?.Dispose();
            if (GameState != null)
                GameState.OnStateDisposed -= HandleGameStateDisposed;
            _stateSubscription?.Dispose();
            base.Dispose();
        }

        private void HandleGameStateDisposed()
        {
            try
            {
                _ = InvokeAsync(() =>
                {
                    GameSessionService.LeaveCurrentSession(navigateHome: false);
                    ReturnToHome();
                }).ContinueWith(
                    t => Logger.LogError(t.Exception, "Error navigating home after game state was disposed."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling game state disposal in lobby.");
            }
        }

        private void ReturnToHome() => NavigationService.ToHome();

        private bool IsHost()
        {
            if (GameState == null || UserService.CurrentUser == null) return false;
            return GameState.Host.Id == UserService.CurrentUser.Id;
        }

        // ── Error toast ───────────────────────────────────────────────────────

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            StateHasChanged();
        }

        private void DismissError()
        {
            _errorMessage = null;
        }
    }
}

