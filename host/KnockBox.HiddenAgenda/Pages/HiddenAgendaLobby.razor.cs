using KnockBox.Core.Components.Shared;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.Core.Services.Navigation;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.State.Shared;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class HiddenAgendaLobby : DisposableComponent
    {
        [Inject] protected HiddenAgendaGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ITickService TickService { get; set; } = default!;

        [Inject] protected ILogger<HiddenAgendaLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;

        protected override async Task OnInitializedAsync()
        {
            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                Logger.LogWarning("User [{userId}] attempted to enter room [{code}] without a session set.", UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode);
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

            GameState = (HiddenAgendaGameState)session.LobbyRegistration.State;

            if (GameState.IsDisposed)
            {
                NavigationService.ToHome();
                return;
            }

            GameState.OnStateDisposed += HandleStateDisposed;

            RoomCode = session.LobbyRegistration.Code;
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));

            if (IsHost())
            {
                var tickResult = TickService.RegisterTickCallback(() =>
                {
                    if (GameState?.Context is not null)
                        GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
                }, tickInterval: TickService.TicksPerSecond);

                if (tickResult.TryGetSuccess(out var sub))
                    _tickSubscription = sub;
            }

            await base.OnInitializedAsync();
        }

        protected bool IsHost() => GameState?.Host?.Id == UserService.CurrentUser?.Id;

        protected override void OnAfterRender(bool firstRender)
        {
            if (GameState.IsKicked(UserService.CurrentUser!))
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

        public override void Dispose()
        {
            if (GameState is not null)
            {
                GameState.OnStateDisposed -= HandleStateDisposed;
            }
            _stateSubscription?.Dispose();
            _tickSubscription?.Dispose();
            base.Dispose();
        }

        private static bool TryExtractObfuscatedRoomCode(string uri, [NotNullWhen(true)] out string? obfuscatedRoomCode)
        {
            obfuscatedRoomCode = null;
            var split = uri.Trim().Trim('/').Split('/');
            if (split.Length <= 0) return false;
            else
            {
                obfuscatedRoomCode = split[^1];
                return true;
            }
        }

        protected HiddenAgendaGameState GameState { get; set; } = default!;
        protected string RoomCode { get; set; } = string.Empty;

        protected async Task StartGame()
        {
            await GameEngine.StartAsync(UserService.CurrentUser!, GameState);
        }
    }
}
