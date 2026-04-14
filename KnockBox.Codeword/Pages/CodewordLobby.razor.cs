using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Core.Services.Navigation;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Codeword.Pages
{
    public partial class CodewordLobby : DisposableComponent
    {
        [Inject] protected CodewordGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ITickService TickService { get; set; } = default!;

        [Inject] protected ILogger<CodewordLobby> Logger { get; set; } = default!;


        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;
        private bool _kickHandled;

        // ── Error toast state ─────────────────────────────────────────────────
        private string? _errorMessage;
        private int _errorKey;

        protected CodewordGameState? GameState { get; set; }

        protected override async Task OnInitializedAsync()
        {
            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                ReturnToHome();
                return;
            }

            if (session.LobbyRegistration.State is not CodewordGameState gameState)
            {
                Logger.LogError("Game state is not of type {Type}", nameof(CodewordGameState));
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

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!_kickHandled && GameState?.IsKicked(UserService.CurrentUser!) == true)
            {
                _kickHandled = true;
                GameSessionService.LeaveCurrentSession(navigateHome: true);
            }


            await base.OnAfterRenderAsync(firstRender);
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

        // ── Timer helpers ─────────────────────────────────────────────────────

        protected int GetPhaseTotalMs()
        {
            if (GameState?.Config is not { } config) return 1;
            return GameState.Phase switch
            {
                CodewordGamePhase.Setup => config.SetupPhaseTimeoutMs,
                CodewordGamePhase.CluePhase => config.CluePhaseTimeoutMs,
                CodewordGamePhase.Discussion => config.DiscussionPhaseTimeoutMs,
                CodewordGamePhase.Voting => config.VotePhaseTimeoutMs,
                CodewordGamePhase.Reveal => config.RevealPhaseTimeoutMs,
                _ => 1
            };
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

