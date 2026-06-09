using KnockBox.Core.Components.Shared;
using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Exceptions;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Games;
using Microsoft.AspNetCore.Components;
using KnockBox.Core.Plugins;
using KnockBox.Platform;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;

namespace KnockBox.Platform.Components.Pages.Home
{
    public partial class Home : DisposableComponent
    {
        [Inject] IOptions<KnockBoxPlatformOptions> PlatformOptions { get; set; } = default!;
        [Inject] ILobbyService LobbyService { get; set; } = default!;
        [Inject] ILobbyCodeService LobbyCodeService { get; set; } = default!;
        [Inject] IUserService UserService { get; set; } = default!;
        [Inject] IGameSessionService GameSessionService { get; set; } = default!;
        [Inject] IRandomNumberService RandomNumberService { get; set; } = default!;
        [Inject] ILogger<Home> Logger { get; set; } = default!;
        [Inject] IEnumerable<IGameModule> GameModules { get; set; } = default!;
        [Inject] IGameAvailabilityService GameAvailability { get; set; } = default!;
        [Inject] NavigationManager Navigation { get; set; } = default!;

        /// <summary>
        /// Filtered + sorted game list for the tile grid. Disabled games are
        /// hidden here; <see cref="LobbyService.CreateLobbyAsync"/> also
        /// rejects them server-side, so this filter is presentational only
        /// (an attacker cannot bypass the gate by keeping a stale tile open).
        /// </summary>
        /// <remarks>
        /// The full module list is fixed at startup so it's sorted once into
        /// <see cref="_sortedModules"/>; the visible subset is recomputed only
        /// when <see cref="IGameAvailabilityService.Changed"/> fires.
        /// </remarks>
        private IGameModule[] _sortedModules = [];
        private IReadOnlyList<IGameModule> _visibleModules = Array.Empty<IGameModule>();
        private IReadOnlyList<IGameModule> VisibleGameModules => _visibleModules;

        private void RebuildVisibleModules()
        {
            var visible = new List<IGameModule>(_sortedModules.Length);
            foreach (var m in _sortedModules)
            {
                if (GameAvailability.IsEnabled(m.Manifest.RouteIdentifier))
                    visible.Add(m);
            }
            _visibleModules = visible;
        }

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
                _playerName = value?.Trim();
                // IUserService owns trim + 12-char cap + event + persistence. The
                // "Not Set" sentinel is preserved when the field is cleared so
                // the CanJoinOrCreate gate still fails for an empty name.
                UserService.SetCurrentUserName(string.IsNullOrWhiteSpace(value) ? "Not Set" : value);
            }
        }

        private bool CanJoinOrCreate => UserService.CurrentUser is not null
            && !string.IsNullOrWhiteSpace(UserService.CurrentUser.Name)
            && UserService.CurrentUser.Name != "Not Set";

        protected override async Task OnInitializedAsync()
        {
            try
            {
                _sortedModules = GameModules.OrderBy(m => m.Manifest.Name).ToArray();
                RebuildVisibleModules();
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
                if (animate) StartReturnAnimation();
                var errorMsg = joinResult.TryGetFailure(out var failure) ? failure.PublicMessage : "Failed to join lobby.";
                ShowError(errorMsg);
                return;
            }

            await animationDelay;

            // Migrated WASM games are driven entirely over the hub: the WASM host page
            // (RuntimeGameLobby) re-joins via the hub, which owns registration + session.
            // Release the circuit-side registration and hand off with a full-page load.
            var lobby = registration.LobbyRegistration;
            if (IsClientGame(lobby.RouteIdentifier))
            {
                registration.Dispose();
                NavigateToWasm($"/{lobby.Uri}", user.Name);
                return;
            }

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

            // Migrated WASM games create their lobby over the hub from the WASM host
            // page (create mode); the circuit does not create a server lobby for them.
            if (IsClientGame(routeIdentifier))
            {
                NavigateToWasm($"/room/{routeIdentifier}", user.Name);
                return;
            }

            _isTransitioning = true;

            var animationDelay = Task.Delay(500);
            var createResult = await LobbyService.CreateLobbyAsync(user, routeIdentifier, ComponentDetached);
            if (!createResult.TryGetSuccess(out var lobby))
            {
                _isTransitioning = false;
                StartReturnAnimation();
                var errorMsg = createResult.TryGetFailure(out var failure) ? failure.PublicMessage : "Failed to create lobby.";
                ShowError(errorMsg);
                return;
            }

            await animationDelay;

            // When the host leaves — either by navigation (LeaveCurrentSession) or by
            // letting the post-disconnect grace period lapse (GameSessionState.Dispose →
            // TakeCurrentSession().Dispose()) — this closure runs. CloseLobbyAsync
            // itself disposes the state; don't call State.Dispose() here or we
            // double-dispose.
            var disposeAction = new DisposableAction(() =>
            {
                _ = LobbyService.CloseLobbyAsync(user, lobby, CancellationToken.None);
            });

            // Leave any prior session before claiming the new slot.
            GameSessionService.LeaveCurrentSession(navigateHome: false);
            GameSessionService.SetCurrentSession(new UserRegistration(user, disposeAction, createResult.Value));
        }

        // ── Error toast ───────────────────────────────────────────────────────

        // Mirror the CSS .home-error-toast animation duration in Home.razor.css.
        private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(3);

        // Mirror the longest .home-container.returning animation in Home.razor.css
        // (`fade-in-up 0.7s ease-out 0.15s` ≈ 0.85s), padded so we clear after it ends.
        private static readonly TimeSpan ReturnAnimationDuration = TimeSpan.FromMilliseconds(900);

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            int capturedKey = _errorKey;
            StateHasChanged();

            // WebKit rejects setAttribute('@onanimationend', ...) and tears the
            // circuit on iPhone Safari, so we drive dismissal from a server-side
            // timer instead. Guard against rapid successive errors: only clear
            // if no later ShowError has bumped the key.
            ScheduleClear(ErrorToastDuration, () =>
            {
                if (_errorKey == capturedKey) _errorMessage = null;
            });
        }

        private void StartReturnAnimation()
        {
            _isReturning = true;
            ScheduleClear(ReturnAnimationDuration, () => _isReturning = false);
        }

        private void OnAvailabilityChanged()
        {
            RebuildVisibleModules();
            // Availability changes can arrive from a different circuit (the
            // admin's). Marshal to the Home page's sync context before
            // touching component state.
            _ = InvokeAsync(StateHasChanged);
        }

        // ── WASM (tri-split) game launch ──────────────────────────────────────

        /// <summary>
        /// True when the route belongs to a migrated game that ships a browser-side
        /// client UI (declares a <c>clientAssembly</c>). Such games render in the
        /// WASM client and are launched/joined over the hub, not the circuit.
        /// </summary>
        private bool IsClientGame(string routeIdentifier)
            => _sortedModules.Any(m =>
                string.Equals(m.Manifest.RouteIdentifier, routeIdentifier, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(m.Manifest.ClientAssembly));

        /// <summary>
        /// Full-page navigation to a WASM route, carrying the chosen display name so
        /// the game's hub connection presents it. forceLoad crosses the
        /// InteractiveServer → InteractiveWebAssembly render-mode boundary cleanly.
        /// </summary>
        /// <remarks>
        /// Builds an ABSOLUTE target URI. A root-relative path (e.g. "/room/...")
        /// makes registered LocationChanging handlers (MainLayout) call
        /// <c>ToBaseRelativePath</c> on a non-absolute URI, which throws — this bites
        /// in the join-by-link flow, where the new tab opens on "/?join=" (not
        /// detected as the home page, so the handler doesn't early-return).
        /// </remarks>
        private void NavigateToWasm(string relativePath, string? name)
        {
            var target = $"{Navigation.BaseUri}{relativePath.TrimStart('/')}";
            if (!string.IsNullOrWhiteSpace(name))
                target += $"?name={Uri.EscapeDataString(name)}";
            Navigation.NavigateTo(target, forceLoad: true);
        }

        public override void Dispose()
        {
            GameAvailability.Changed -= OnAvailabilityChanged;
            base.Dispose();
        }
    }
}
