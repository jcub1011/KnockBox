using KnockBox.Core.Components.Shared;
using KnockBox.TaskMaster.Services.Logic.Games;
using KnockBox.Core.Services.Navigation;
using KnockBox.TaskMaster.Services.State.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.TaskMaster.Pages
{
    public partial class TaskMasterLobby : DisposableComponent
    {
        [Inject] protected TaskMasterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<TaskMasterLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;

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

            GameState = (TaskMasterGameState)session.LobbyRegistration.State;

            if (GameState.IsDisposed)
            {
                NavigationService.ToHome();
                return;
            }

            GameState.OnStateDisposed += HandleStateDisposed;

            RoomCode = session.LobbyRegistration.Code;
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));

            await base.OnInitializedAsync();
        }

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

        protected TaskMasterGameState GameState { get; set; } = default!;
        protected string RoomCode { get; set; } = string.Empty;

        protected async Task StartGame()
        {
            await GameEngine.StartAsync(UserService.CurrentUser!, GameState);
        }
    }
}
