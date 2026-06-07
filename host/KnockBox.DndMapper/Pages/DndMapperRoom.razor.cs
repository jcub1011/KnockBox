using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.PlayLog;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages
{
    public partial class DndMapperRoom : LobbyPageBase<DndMapperGameState>
    {
        /// <summary>Stable game id for the play log; must match the plugin's route identifier.</summary>
        private const string RouteIdentifier = "dnd-mapper";

        [Inject] protected DndMapperGameEngine GameEngine { get; set; } = default!;

        /// <summary>
        /// DnD Mapper is an open-ended sandbox tool with no terminal phase, so it logs on
        /// leave rather than on game-over. Returns <c>null</c> when the session was
        /// essentially untouched — no maps drawn, no character sheets, and no dice rolled —
        /// so an idle visit doesn't clutter the user's play log. Otherwise records one entry
        /// with the session-summary metadata.
        /// </summary>
        protected override GameLog? BuildOnLeavePlayLog()
        {
            var state = GameState;
            if (state.Maps.Length == 0 && state.Sheets.Count == 0 && state.RollLog.Count == 0)
                return null;

            return GameLog.Create(
                RouteIdentifier,
                DndMapperPlayLogMetadata.Build(state, UserService.CurrentUser?.Id));
        }
    }
}
