using KnockBox.CardCounter.Services.Logic.Formatting;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.Core.Services.State.PlayLog;

namespace KnockBox.CardCounter.Services.Logic.Games
{
    /// <summary>
    /// Builds the per-user play-log metadata for a finished Card Counter game.
    /// Pure (no DI, no I/O) so it can be unit-tested directly.
    /// </summary>
    internal static class CardCounterPlayLogMetadata
    {
        /// <summary>
        /// Produces the play-log metadata for a completed game. Match-level keys are always
        /// present; personal keys ("My Balance", "Placement") are added only when
        /// <paramref name="currentUserId"/> identifies one of the game's players.
        /// Ranking mirrors the game-over leaderboard: when
        /// <see cref="CardCounterSettings.FlipWinCondition"/> is set, the highest balance
        /// magnitude wins; otherwise the balance closest to zero wins.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Build(
            CardCounterGameState state,
            Guid? currentUserId)
        {
            var metadata = new Dictionary<string, string>();

            // Rank players by the active win condition. Ties keep their input order; placement
            // is the 1-based index in this ordering.
            bool flip = state.Settings.FlipWinCondition;
            var ranked = (flip
                    ? state.GamePlayers.Values.OrderByDescending(p => Math.Abs(p.Balance))
                    : state.GamePlayers.Values.OrderBy(p => Math.Abs(p.Balance)))
                .ToList();

            int total = ranked.Count;

            metadata.Set(StandardMetadata.Players, total.ToString());
            metadata["Win Condition"] = flip ? "Highest magnitude" : "Closest to zero";

            if (total > 0)
            {
                metadata.Set(StandardMetadata.Winner, ranked[0].DisplayName);
            }

            if (currentUserId is { } id)
            {
                int index = ranked.FindIndex(p => p.PlayerId == id);
                if (index >= 0)
                {
                    var me = ranked[index];
                    metadata["My Balance"] = me.Balance.FormatBalance();
                    metadata.Set(StandardMetadata.Placement, $"{index + 1} / {total}");
                }
            }

            return metadata;
        }
    }
}
