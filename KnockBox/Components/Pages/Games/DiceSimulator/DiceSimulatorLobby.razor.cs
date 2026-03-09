using KnockBox.Components.Shared;
using KnockBox.Services.Logic.Games.DiceSimulator;
using KnockBox.Services.Navigation;
using KnockBox.Services.State.Games.DiceSimulator;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Components.Pages.Games.DiceSimulator
{
    public partial class DiceSimulatorLobby : DisposableComponent
    {
        [Inject] protected DiceSimulatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<DiceSimulatorLobby> Logger { get; set; } = default!;

        [Inject] protected Microsoft.JSInterop.IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;

        protected override async Task OnInitializedAsync()
        {
            // Ensure the user is initialized so GameSessionService can look up the
            // ID-backed session — required for page-refresh rejoins where the user
            // service starts uninitialized on the fresh circuit.
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

            GameState = (DiceSimulatorGameState)session.LobbyRegistration.State;

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
            if (GameState.KickedPlayers.Contains(UserService.CurrentUser))
            {
                GameSessionService.LeaveCurrentSession(navigateHome: true);
            }

            base.OnAfterRender(firstRender);
        }

        private void HandleStateDisposed()
        {
            InvokeAsync(() =>
            {
                NavigationService.ToHome();
            });
        }

        public override void Dispose()
        {
            base.Dispose();
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
            else
            {
                obfuscatedRoomCode = split[^1];
                return true;
            }
        }

        protected DiceSimulatorGameState GameState { get; set; } = default!;
        protected DiceRollAction RollAction { get; set; } = new();
        protected string RoomCode { get; set; } = string.Empty;
        protected bool IsRoomCodeVisible { get; set; } = false;

        private HashSet<string> _expandedPlayerIds = new();

        protected void TogglePlayerHistory(string playerId)
        {
            if (!_expandedPlayerIds.Add(playerId))
                _expandedPlayerIds.Remove(playerId);
        }

        protected bool IsPlayerExpanded(string playerId) => _expandedPlayerIds.Contains(playerId);

        protected void ToggleRoomCode()
        {
            IsRoomCodeVisible = !IsRoomCodeVisible;
        }

        protected async Task StartGame()
        {
            await GameEngine.StartAsync(UserService.CurrentUser!, GameState);
        }

        protected void RollDice()
        {
            GameEngine.RollDice(UserService.CurrentUser!, GameState, RollAction);
        }

        protected void ClearHistory()
        {
            GameEngine.ClearHistory(UserService.CurrentUser!, GameState);
        }

        protected async Task ExportCsv()
        {
            var csvBytes = KnockBox.Services.Logic.Games.DiceSimulator.CsvExportService.GenerateCsv(GameState.RollHistory);
            var base64 = Convert.ToBase64String(csvBytes);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var filename = $"DnD-Rolls-{ObfuscatedRoomCode}-{timestamp}.csv";
            await JSRuntime.InvokeVoidAsync("downloadCsvFile", filename, base64);
        }
    }
}
