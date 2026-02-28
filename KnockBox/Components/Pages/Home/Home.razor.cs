using KnockBox.Components.Shared;
using KnockBox.Extensions.Exceptions;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Navigation;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Home
{
    public partial class Home : DisposableComponent
    {
        [Inject] INavigationService NavigationService { get; set; } = default!;
        [Inject] ILobbyService LobbyService { get; set; } = default!;
        [Inject] IUserService UserService { get; set; } = default!;
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
            if (joinResult.TryGetError(out var error))
            {
                // TODO: Notify user join failed
                return;
            }

            NavigationService.ToGame(joinResult.Value.LobbyRegistration);
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
            if (createResult.TryGetError(out var error))
            {
                // TODO: Notify user lobby creation failed
                return;
            }

            NavigationService.ToGame(createResult.Value);
        }
    }
}
