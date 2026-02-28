using KnockBox.Components.Shared;
using KnockBox.Services.Navigation;
using KnockBox.Services.Navigation.Games.DiceSimulator;
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
            RoomCode = session.LobbyRegistration.Code;
            _stateSubscription = GameState.SubscribeToStateChanged(async () => await InvokeAsync(StateHasChanged)).Value;

            await base.OnInitializedAsync();
        }

        public override void Dispose()
        {
            base.Dispose();
            _stateSubscription?.Dispose();
            GameSessionService.LeaveCurrentSession(false);
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
            GameState.RollDice(UserService.CurrentUser!, RollAction);
        }

        protected void ClearHistory()
        {
            GameState.ClearHistory(UserService.CurrentUser!);
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
