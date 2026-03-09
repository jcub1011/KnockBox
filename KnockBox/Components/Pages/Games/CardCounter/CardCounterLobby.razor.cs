using KnockBox.Components.Shared;
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

        [Inject] protected ILogger<CardCounterLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private PeriodicTimer? _timer;

        private const int ShoeAnimationDurationMs = 2500;
        private int _prevShoeIndex = -1;
        protected bool IsAnimatingShoe { get; private set; }

        protected override async Task OnInitializedAsync()
        {
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

            _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = StartTimerAsync();

            await base.OnInitializedAsync();
        }

        private async Task StartTimerAsync()
        {
            try
            {
                while (await _timer!.WaitForNextTickAsync(ComponentDetached))
                {
                    try
                    {
                        if (GameState?.Context != null && IsHost())
                            GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);

                        await InvokeAsync(StateHasChanged);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error handling state tick.");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling state tick.");
            }
        }

        public override void Dispose()
        {
            _timer?.Dispose();
            if (GameState != null)
                GameState.OnStateDisposed -= HandleGameStateDisposed;
            _stateSubscription?.Dispose();
            base.Dispose();
        }

        private void HandleGameStateDisposed()
        {
            try
            {
                // The host left and the game state was torn down. Navigate remaining players home.
                _ = InvokeAsync(ReturnToHome).ContinueWith(
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
