using KnockBox.Core.Services.State.PlayLog;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.State.Games.PlayLog
{
    /// <summary>
    /// Builds the per-user play-log metadata for a finished Drawn To Dress match from the
    /// terminal <see cref="DrawnToDressGameState.Leaderboard"/>. Pure and DI-free so it can be
    /// unit-tested directly: it reads the ranked leaderboard and emits a string→string table the
    /// home page renders verbatim. Match-level keys (Winner, Players, Rounds) are always present;
    /// personal keys (Placement, Score) are added only when <paramref name="currentUserId"/> is one
    /// of the ranked players.
    /// </summary>
    internal static class DrawnToDressPlayLogMetadata
    {
        public static IReadOnlyDictionary<string, string> Build(DrawnToDressGameState state, Guid? currentUserId)
        {
            var metadata = new Dictionary<string, string>();

            var leaderboard = state.Leaderboard;
            if (leaderboard is null || leaderboard.Count == 0)
                return metadata;

            // The leaderboard is stored in final rank order, so the first entry is the winner.
            metadata.Set(StandardMetadata.Winner, leaderboard[0].DisplayName);
            metadata.Set(StandardMetadata.Players, leaderboard.Count.ToString());
            metadata.Set(StandardMetadata.Rounds, state.VotingRounds.Count.ToString());

            // Personal keys — only when the local user actually played this match.
            if (currentUserId is { } userId)
            {
                int index = -1;
                for (int i = 0; i < leaderboard.Count; i++)
                {
                    if (leaderboard[i].PlayerId == userId)
                    {
                        index = i;
                        break;
                    }
                }

                if (index >= 0)
                {
                    var mine = leaderboard[index];
                    metadata.Set(StandardMetadata.Placement, $"{index + 1} / {leaderboard.Count}");
                    metadata.Set(StandardMetadata.Score, mine.TotalScore.ToString("0.#"));
                }
            }

            return metadata;
        }
    }
}
