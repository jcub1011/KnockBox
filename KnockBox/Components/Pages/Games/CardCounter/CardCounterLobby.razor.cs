using KnockBox.Components.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.Navigation;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.CardCounter
{
    public partial class CardCounterLobby : DisposableComponent
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ITickService TickService { get; set; } = default!;

        [Inject] protected ILogger<CardCounterLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;
        private bool _kickHandled;

        private const int ShoeAnimationDurationMs = 2500;
        private int _prevShoeIndex = -1;
        protected bool IsAnimatingShoe { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            // Ensure the user is initialized so GameSessionService can look up the
            // ID-backed session — required for page-refresh rejoins where the user
            // service starts uninitialized on the fresh circuit.
            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                ReturnToHome();
                return;
            }

            if (session.LobbyRegistration.State is not CardCounterGameState gameState)
            {
                Logger.LogError("Game state is not of type {Type}", nameof(CardCounterGameState));
                ReturnToHome();
                return;
            }

            GameState = gameState;

            _prevShoeIndex = GameState.ShoeIndex;

            GameState.OnStateDisposed += HandleGameStateDisposed;

            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
            {
                bool isNewShoe = false;

                if (GameState != null && GameState.ShoeIndex < _prevShoeIndex)
                {
                    // Game was restarted — ShoeIndex reset to 0; sync baseline so future increments are detected.
                    _prevShoeIndex = GameState.ShoeIndex;
                }

                if (GameState != null && GameState.ShoeIndex > _prevShoeIndex)
                {
                    isNewShoe = true;
                    _prevShoeIndex = GameState.ShoeIndex;
                    IsAnimatingShoe = true;
                }

                await InvokeAsync(StateHasChanged);

                if (isNewShoe)
                {
                    await Task.Delay(ShoeAnimationDurationMs);
                    IsAnimatingShoe = false;
                    await InvokeAsync(StateHasChanged);
                }
            });

            if (IsHost())
            {
                var tickResult = TickService.RegisterTickCallback(() =>
                {
                    if (GameState?.Context is not null)
                        GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
                }, tickInterval: TickService.TicksPerSecond); // once per second

                if (tickResult.TryGetSuccess(out var sub))
                    _tickSubscription = sub;
            }

            await base.OnInitializedAsync();
        }

        protected override void OnAfterRender(bool firstRender)
        {
            if (!_kickHandled && GameState?.KickedPlayers.Contains(UserService.CurrentUser) == true)
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
                    // Clear the player's session so the disposed game state is not retained
                    // in GameSessionState after the game ends.
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

        protected CardCounterGameState? GameState { get; set; }

        protected void ReturnToHome() => NavigationService.ToHome();

        protected bool IsHost()
        {
            if (GameState == null || UserService.CurrentUser == null) return false;
            return GameState.Host.Id == UserService.CurrentUser.Id;
        }
    }
}
