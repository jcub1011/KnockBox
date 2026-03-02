using KnockBox.Components.Shared;
using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Exceptions;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Home
{
    public partial class Home : DisposableComponent
    {
        [Inject] ILobbyService LobbyService { get; set; } = default!;
        [Inject] ILobbyCodeService LobbyCodeService { get; set; } = default!;
        [Inject] IUserService UserService { get; set; } = default!;
        [Inject] IGameSessionService GameSessionService { get; set; } = default!;
        [Inject] ILogger<Home> Logger { get; set; } = default!;

        private string? LobbyCode { get; set; }

        private string? _playerName;
        private string? PlayerName
        {
            get => _playerName ?? (UserService.CurrentUser?.Name == "Not Set" ? "" : UserService.CurrentUser?.Name);
            set
            {
                // Cap to 12 characters
                value = value?.Trim();

                if (value is not null && value.Length > 12)
                {
                    value = value[..12];
                }

                _playerName = value;
                UserService.CurrentUser?.Name = string.IsNullOrWhiteSpace(value) ? "Not Set" : value.Trim();
            }
        }

        private bool CanJoinOrCreate => UserService.CurrentUser is not null 
            && !string.IsNullOrWhiteSpace(UserService.CurrentUser.Name) 
            && UserService.CurrentUser.Name != "Not Set";

        protected override async Task OnInitializedAsync()
        {
            try
            {
                // Ensures the user isn't left in a session
                GameSessionService.LeaveCurrentSession(false);
                if (UserService.CurrentUser is null)
                    await UserService.InitializeCurrentUserAsync(ComponentDetached);
                await base.OnInitializedAsync();
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out _)) return;
                Logger.LogError(ex, "Error initializing home page.");
            }
        }

        private async Task JoinLobby(string lobbyCode)
        {
            if (!CanJoinOrCreate) return;

            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                // TODO: Notify user their code is invalid
                return;
            }

            var user = UserService.CurrentUser;
            if (user is null)
            {
                // TODO: Notify user they are null
                return;
            }

            var joinResult = await LobbyService.JoinLobbyAsync(user, lobbyCode, ComponentDetached);
            if (!joinResult.TryGetValue(out var registration))
            {
                // TODO: Notify user join failed
                return;
            }

            GameSessionService.SetCurrentSession(registration);
        }

        private async Task CreateLobby(GameType gameType)
        {
            if (!CanJoinOrCreate) return;

            var user = UserService.CurrentUser;
            if (user is null)
            {
                // TODO: Notify user they are null
                return;
            }

            var createResult = await LobbyService.CreateLobbyAsync(user, gameType, ComponentDetached);
            if (!createResult.TryGetValue(out var lobby))
            {
                // TODO: Notify user lobby creation failed
                return;
            }

            var disposeAction = new DisposableAction(() =>
            {
                // Close the lobby when the host leaves
                lobby.State.Dispose();
                _ = LobbyService.CloseLobbyAsync(user, lobby, CancellationToken.None);
            });

            GameSessionService.SetCurrentSession(new UserRegistration(user, disposeAction, createResult.Value));
        }
    }
}
