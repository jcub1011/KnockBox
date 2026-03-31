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

        [Parameter]
        [SupplyParameterFromQuery(Name = "join")]
        public string? JoinCode { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "fresh")]
        public int? Fresh { get; set; }

        private static readonly Random _random = new();

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
                if (UserService.CurrentUser is null)
                    await UserService.InitializeCurrentUserAsync(ComponentDetached);

                if (Fresh == 1)
                {
                    await UserService.ResetIdentityAsync(ComponentDetached);
                }
                
                await base.OnInitializedAsync();

                if (!string.IsNullOrWhiteSpace(JoinCode))
                {
                    // If the user has no name, give them a random one for testing convenience.
                    if (UserService.CurrentUser is not null && (string.IsNullOrWhiteSpace(UserService.CurrentUser.Name) || UserService.CurrentUser.Name == "Not Set"))
                    {
                        PlayerName = $"Tester {_random.Next(1000, 9999)}";
                    }

                    await JoinLobby(JoinCode);
                }
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
            if (!joinResult.TryGetSuccess(out var registration))
            {
                // TODO: Notify user join failed
                return;
            }

            // Leave any prior session before claiming the new slot.  If the player is
            // re-joining the same lobby, RegisterPlayer has already issued a fresh token;
            // this only clears GameSessionState so SetCurrentSession can succeed.
            GameSessionService.LeaveCurrentSession(navigateHome: false);
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
            if (!createResult.TryGetSuccess(out var lobby))
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

            // Leave any prior session before claiming the new slot.
            GameSessionService.LeaveCurrentSession(navigateHome: false);
            GameSessionService.SetCurrentSession(new UserRegistration(user, disposeAction, createResult.Value));
        }
    }
}
