using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Services.Logic.Games
{
    /// <summary>
    /// Builds the per-user play-log metadata for a finished Operator game.
    /// Pure (no DI, no I/O) so it can be unit-tested directly.
    /// </summary>
    internal static class OperatorPlayLogMetadata
    {
        /// <summary>
        /// Produces the play-log metadata for a completed game. Match-level keys are always
        /// present; personal keys ("My Points", "Placement") are added only when
        /// <paramref name="currentUserId"/> identifies one of the game's players. Ranking
        /// mirrors the game-over leaderboard: the score closest to zero wins, ties broken by
        /// the earlier <see cref="Models.OperatorPlayerState.ScoreTimestamp"/>.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Build(
            OperatorGameState state,
            Guid? currentUserId)
        {
            var metadata = new Dictionary<string, string>();

            // Rank the game's players by closeness to zero (winner first), matching
            // GameOverState's ordering. Placement is the 1-based index in this ordering.
            var ranked = state.GamePlayers.Values
                .OrderBy(p => Math.Abs(p.CurrentPoints))
                .ThenBy(p => p.ScoreTimestamp)
                .ToList();

            int total = ranked.Count;

            metadata.Set(StandardMetadata.Players, total.ToString());
            metadata.Set(StandardMetadata.Rounds, state.TurnCount.ToString());

            if (state.WinnerPlayerId is { } winnerId)
            {
                metadata.Set(StandardMetadata.Winner, DisplayNameFor(state, winnerId));
            }

            if (currentUserId is { } id)
            {
                int index = ranked.FindIndex(p => p.UserId == id);
                if (index >= 0)
                {
                    var me = ranked[index];
                    metadata["My Points"] = me.CurrentPoints.ToString("0.#");
                    metadata.Set(StandardMetadata.Placement, $"{index + 1} / {total}");
                }
            }

            return metadata;
        }

        // Display names live on the participant roster, not on per-player game state.
        private static string DisplayNameFor(OperatorGameState state, Guid userId)
        {
            foreach (var participant in state.Participants)
            {
                if (participant.User.Id == userId)
                    return participant.DisplayName;
            }

            return "Unknown";
        }
    }
}
