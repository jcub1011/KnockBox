using KnockBox.Components.Shared;
using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Exceptions;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Navigation;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Home
{
    public partial class Home : DisposableComponent
    {
        [Inject] ILobbyService LobbyService { get; set; } = default!;
        [Inject] IUserService UserService { get; set; } = default!;
        [Inject] IGameSessionService GameSessionService { get; set; } = default!;
        [Inject] ILogger<Home> Logger { get; set; } = default!;

        private string? LobbyCode { get; set; }

        protected override async Task OnInitializedAsync()
        {
            try
            {
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
            });

            GameSessionService.SetCurrentSession(new UserRegistration(user, disposeAction, createResult.Value));
        }
    }
}
