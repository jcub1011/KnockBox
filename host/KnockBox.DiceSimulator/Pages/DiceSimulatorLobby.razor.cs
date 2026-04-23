using KnockBox.Core.Components.Shared;
using KnockBox.DiceSimulator.Services.Logic.Games;
using KnockBox.DiceSimulator.Services.State.Games;
using KnockBox.DiceSimulator.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.DiceSimulator.Pages
{
    public partial class DiceSimulatorLobby : LobbyPageBase<DiceSimulatorGameState>
    {
        [Inject] protected DiceSimulatorGameEngine GameEngine { get; set; } = default!;
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;

        protected DiceRollAction RollAction { get; set; } = new();
        protected bool IsRoomCodeVisible { get; set; } = false;

        private readonly HashSet<string> _expandedPlayerIds = [];

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
            if (UserService.CurrentUser is null) return;
            if (UserService.CurrentUser.Id != GameState.Host.Id) return;
            await GameEngine.StartAsync(GameState);
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
            var csvBytes = CsvExportService.GenerateCsv(GameState.RollHistory);
            var base64 = Convert.ToBase64String(csvBytes);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var filename = $"DnD-Rolls-{ObfuscatedRoomCode}-{timestamp}.csv";
            await JSRuntime.InvokeVoidAsync("downloadCsvFile", filename, base64);
        }
    }
}
