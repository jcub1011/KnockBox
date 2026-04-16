using KnockBox.Core.Components.Shared;
using KnockBox.Core.Extensions.Disposable;
using KnockBox.Core.Extensions.Exceptions;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.Games.Shared;
using Microsoft.AspNetCore.Components;
using KnockBox.Core.Plugins;
using System.Collections.Generic;
using System.Linq;

namespace KnockBox.Components.Pages.Home
{
    public partial class Home : DisposableComponent
    {
        [Inject] ILobbyService LobbyService { get; set; } = default!;
        [Inject] ILobbyCodeService LobbyCodeService { get; set; } = default!;
        [Inject] IUserService UserService { get; set; } = default!;
        [Inject] IGameSessionService GameSessionService { get; set; } = default!;
        [Inject] IRandomNumberService RandomNumberService { get; set; } = default!;
        [Inject] ILogger<Home> Logger { get; set; } = default!;
        [Inject] IEnumerable<IGameModule> GameModules { get; set; } = default!;
        [Inject] IGameAvailabilityService GameAvailability { get; set; } = default!;

        /// <summary>
        /// Filtered + sorted game list for the tile grid. Disabled games are
        /// hidden here; <see cref="LobbyService.CreateLobbyAsync"/> also
        /// rejects them server-side, so this filter is presentational only
        /// (an attacker cannot bypass the gate by keeping a stale tile open).
        /// </summary>
        private IEnumerable<IGameModule> VisibleGameModules =>
            GameModules
                .Where(m => GameAvailability.IsEnabled(m.RouteIdentifier))
                .OrderBy(m => m.Name);

        [Parameter]
        [SupplyParameterFromQuery(Name = "join")]
        public string? JoinCode { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "fresh")]
        public int? Fresh { get; set; }

        private string? LobbyCode { get; set; }
        private bool _isTransitioning;
        private bool _isReturning;
        private string? _errorMessage;
        private int _errorKey;

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
                GameAvailability.Changed += OnAvailabilityChanged;

                if (Fresh == 1)
                {
                    await UserService.ResetIdentityAsync(ComponentDetached);
                }
                else if (UserService.CurrentUser is null)
                {
                    await UserService.InitializeCurrentUserAsync(ComponentDetached);
                }

                await base.OnInitializedAsync();

                if (!string.IsNullOrWhiteSpace(JoinCode))
                {
                    // If the user has no name, give them a random one for testing convenience.
                    if (UserService.CurrentUser is not null && (string.IsNullOrWhiteSpace(UserService.CurrentUser.Name) || UserService.CurrentUser.Name == "Not Set"))
                    {
                        PlayerName = $"Tester {RandomNumberService.GetRandomInt(1000, 9999)}";
                    }

                    await JoinLobby(JoinCode, animate: false);
                }
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out _)) return;
                Logger.LogError(ex, "Error initializing home page.");
            }
        }

        private async Task JoinLobby(string lobbyCode, bool animate = true)
        {
            if (!CanJoinOrCreate || _isTransitioning) return;

            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                ShowError("Please enter a valid room code.");
                return;
            }

            var user = UserService.CurrentUser;
            if (user is null)
            {
                ShowError("Could not identify your session. Please refresh the page.");
                return;
            }

            if (animate) _isTransitioning = true;

            var animationDelay = animate ? Task.Delay(500) : Task.CompletedTask;
            var joinResult = await LobbyService.JoinLobbyAsync(user, lobbyCode, ComponentDetached);
            if (!joinResult.TryGetSuccess(out var registration))
            {
                _isTransitioning = false;
                _isReturning = animate;
                var errorMsg = joinResult.TryGetFailure(out var failure) ? failure.PublicMessage : "Failed to join lobby.";
                ShowError(errorMsg);
                return;
            }

            await animationDelay;

            // Leave any prior session before claiming the new slot.  If the player is
            // re-joining the same lobby, RegisterPlayer has already issued a fresh token;
            // this only clears GameSessionState so SetCurrentSession can succeed.
            GameSessionService.LeaveCurrentSession(navigateHome: false);
            GameSessionService.SetCurrentSession(registration);
        }

        private async Task CreateLobby(string routeIdentifier)
        {
            if (!CanJoinOrCreate || _isTransitioning) return;

            var user = UserService.CurrentUser;
            if (user is null)
            {
                ShowError("Could not identify your session. Please refresh the page.");
                return;
            }

            _isTransitioning = true;

            var animationDelay = Task.Delay(500);
            var createResult = await LobbyService.CreateLobbyAsync(user, routeIdentifier, ComponentDetached);
            if (!createResult.TryGetSuccess(out var lobby))
            {
                _isTransitioning = false;
                _isReturning = true;
                var errorMsg = createResult.TryGetFailure(out var failure) ? failure.PublicMessage : "Failed to create lobby.";
                ShowError(errorMsg);
                return;
            }

            await animationDelay;

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

        private void OnReturnAnimationEnd()
        {
            _isReturning = false;
        }

        private void OnAvailabilityChanged()
        {
            // Availability changes can arrive from a different circuit (the
            // admin's). Marshal to the Home page's sync context before
            // touching component state.
            _ = InvokeAsync(StateHasChanged);
        }

        public override void Dispose()
        {
            GameAvailability.Changed -= OnAvailabilityChanged;
            base.Dispose();
        }
    }
}
