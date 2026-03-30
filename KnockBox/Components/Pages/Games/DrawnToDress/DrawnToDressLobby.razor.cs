using KnockBox.Components.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Navigation;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class DrawnToDressLobby : DisposableComponent
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ITickService TickService { get; set; } = default!;

        [Inject] protected ILogger<DrawnToDressLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private DtdAriaAnnouncer? _announcer;
        private GamePhase? _lastAnnouncedPhase;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;

        protected override async Task OnInitializedAsync()
        {
            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                Logger.LogWarning("User [{userId}] attempted to enter room [{code}] without a session set.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode);
                NavigationService.ToHome();
                return;
            }

            if (!TryExtractObfuscatedRoomCode(session.LobbyRegistration.Uri, out var roomCode))
            {
                Logger.LogError("User [{userId}] attempted to enter room [{code}] but their session registration uri [{uri}] could not be parsed.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode, session.LobbyRegistration.Uri);
                NavigationService.ToHome();
                return;
            }

            if (roomCode.Trim() != ObfuscatedRoomCode)
            {
                Logger.LogError("User [{userId}] attempted to enter room [{code}] but their session registration uri [{uri}] does not match.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode, session.LobbyRegistration.Uri);
                NavigationService.ToHome();
                return;
            }

            GameState = (DrawnToDressGameState)session.LobbyRegistration.State;

            if (GameState.IsDisposed)
            {
                NavigationService.ToHome();
                return;
            }

            GameState.OnStateDisposed += HandleStateDisposed;
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
            {
                await InvokeAsync(() =>
                {
                    AnnouncePhaseChangeIfNeeded();
                    StateHasChanged();
                });
            });

            // Register with the server-side tick service. Only the host drives FSM ticks to
            // avoid redundant lock contention from every client. The tick service runs
            // independently of any Blazor circuit, so the game continues even if the host
            // disconnects and a new host is promoted.
            if (IsHost())
            {
                var tickResult = TickService.RegisterTickCallback(() =>
                {
                    if (GameState?.Context is not null)
                        GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
                }, tickInterval: TickService.TicksPerSecond); // once per second

                if (tickResult.TryGetSuccess(out var sub))
                    _tickSubscription = sub;
                else
                    Logger.LogError("Failed to register tick callback: {Error}", tickResult.Error);
            }

            await base.OnInitializedAsync();
        }

        private bool IsHost() => UserService.CurrentUser?.Id == GameState?.Host?.Id;

        protected override void OnAfterRender(bool firstRender)
        {
            if (GameState is not null && GameState.KickedPlayers.Contains(UserService.CurrentUser))
            {
                GameSessionService.LeaveCurrentSession(navigateHome: true);
            }

            base.OnAfterRender(firstRender);
        }

        private void HandleStateDisposed()
        {
            InvokeAsync(() =>
            {
                GameSessionService.LeaveCurrentSession(navigateHome: false);
                NavigationService.ToHome();
            });
        }

        private void AnnouncePhaseChangeIfNeeded()
        {
            if (GameState is null || _announcer is null) return;
            var currentPhase = GameState.Phase;
            if (_lastAnnouncedPhase != currentPhase)
            {
                _lastAnnouncedPhase = currentPhase;

                var phaseName = currentPhase switch
                {
                    GamePhase.ThemeSelection => "Theme Selection",
                    GamePhase.Drawing => "Drawing Round",
                    GamePhase.PoolReveal => "Pool Reveal",
                    GamePhase.OutfitBuilding => "Outfit Building",
                    GamePhase.OutfitCustomization => "Outfit Customization",
                    GamePhase.Voting => "Voting",
                    GamePhase.CoinFlip => "Coin Flip",
                    GamePhase.VotingRoundResults => "Voting Round Results",
                    GamePhase.Results => "Final Results",
                    _ => currentPhase.ToString()
                };
                _announcer.Announce($"Phase changed to {phaseName}");
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            _tickSubscription?.Dispose();
            if (GameState is not null)
            {
                GameState.OnStateDisposed -= HandleStateDisposed;
            }
            _stateSubscription?.Dispose();
        }

        private static bool TryExtractObfuscatedRoomCode(string uri, [NotNullWhen(true)] out string? obfuscatedRoomCode)
        {
            obfuscatedRoomCode = null;
            var split = uri.Trim().Trim('/').Split('/');
            if (split.Length <= 0) return false;
            obfuscatedRoomCode = split[^1];
            return true;
        }

        protected DrawnToDressGameState GameState { get; set; } = default!;
    }
}
