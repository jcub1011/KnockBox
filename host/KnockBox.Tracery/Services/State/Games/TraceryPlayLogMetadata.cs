using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.State.PlayLog;

namespace KnockBox.Tracery.Services.State.Games
{
    /// <summary>
    /// Builds the per-user play-log metadata for a finished Tracery match. Pure: derives every
    /// value from the supplied <see cref="TraceryGameState"/> snapshot so it can be unit-tested
    /// without a DI container or a live circuit.
    /// </summary>
    /// <remarks>
    /// The standings here mirror <c>TraceryFinalStandingsView</c>: the frozen start-of-match
    /// <see cref="TraceryGameState.Participants"/> roster (so disconnected players still count),
    /// scored by each participant's <c>CumulativeScore</c>, ordered by score descending. Personal
    /// keys ("My Score", "Placement") are only emitted when <paramref name="currentUserId"/> is a
    /// participant with a player state — an observing host gets only the match-level keys.
    /// </remarks>
    internal static class TraceryPlayLogMetadata
    {
        public static IReadOnlyDictionary<string, string> Build(TraceryGameState state, Guid? currentUserId)
        {
            // Same roster/scoring/order as the final-standings screen.
            var standings = state.Participants
                .Select(entry => new
                {
                    entry.User,
                    entry.DisplayName,
                    Score = state.PlayerStates.TryGetValue(entry.User.Id, out var ps) ? ps.CumulativeScore : 0
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            var metadata = new Dictionary<string, string>();
            metadata.Set(StandardMetadata.Winner, standings.Count > 0 ? standings[0].DisplayName : string.Empty);
            metadata.Set(StandardMetadata.Rounds, state.RoundResults.Count.ToString());
            metadata.Set(StandardMetadata.Players, standings.Count.ToString());

            // Personal keys only when the local user actually participated.
            if (currentUserId is { } id)
            {
                var index = standings.FindIndex(x => x.User.Id == id);
                if (index >= 0)
                {
                    metadata["My Score"] = standings[index].Score.ToString();
                    metadata.Set(StandardMetadata.Placement, $"{index + 1} / {standings.Count}");
                }
            }

            return metadata;
        }
    }
}
