using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Components
{
    public partial class BoardView : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter] public Action<int>? OnSpaceClicked { get; set; }

        private static readonly string[] Wings = ["GrandHall", "ModernWing", "SculptureGarden", "RestorationRoom", "Corridor"];

        private IEnumerable<BoardSpace> GetSpacesForWing(string wingName)
        {
            if (!Enum.TryParse<Wing>(wingName, out var wing)) return [];
            return GameState.BoardGraph.Spaces.Values.Where(s => s.Wing == wing).OrderBy(s => s.Id);
        }

        private IEnumerable<HiddenAgendaPlayerState> GetPlayersAtSpace(int spaceId)
        {
            return GameState.GamePlayers.Values.Where(p => p.CurrentSpaceId == spaceId);
        }

        private bool IsHighlighted(int spaceId)
        {
            if (GameState.Phase != GamePhase.MovePhase || GameState.ReachableSpaces is null) return false;
            return GameState.ReachableSpaces.Any(s => s.Id == spaceId);
        }

        private string GetPlayerColor(string playerId)
        {
            var players = GameState.Players.ToList();
            var index = players.FindIndex(p => p.Id == playerId);
            return index switch
            {
                0 => "red",
                1 => "blue",
                2 => "green",
                3 => "yellow",
                4 => "purple",
                5 => "orange",
                _ => "gray"
            };
        }
    }
}
