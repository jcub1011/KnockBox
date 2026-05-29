using KnockBox.Core.Components.Shared;
using KnockBox.Tracery.Services.State.Games;

namespace KnockBox.Tracery.Pages
{
    public partial class TraceryRoom : LobbyPageBase<TraceryGameState>
    {
        /// <summary>
        /// True when the current user is the host and is sitting out as the shared display
        /// (not a participant). Drives the host-vs-player view split during play.
        /// </summary>
        protected bool IsHostObserver =>
            GameState is not null
            && !GameState.HostIsParticipant
            && UserService.CurrentUser?.Id == GameState.Host.Id;

        /// <summary>The frozen participant roster ordered by cumulative score, for standings.</summary>
        private IEnumerable<(string DisplayName, int Score)> Standings()
            => GameState.Participants
                .Select(entry => (
                    entry.DisplayName,
                    Score: GameState.PlayerStates.TryGetValue(entry.User.Id, out var ps) ? ps.CumulativeScore : 0))
                .OrderByDescending(x => x.Score);
    }
}
