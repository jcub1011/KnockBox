using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Services.State.PlayLog;

namespace KnockBox.AlphaChain.Services.State.Games.PlayLog
{
    /// <summary>
    /// Builds the per-user play-log metadata for a finished Alpha Chain match from the
    /// terminal <see cref="GameResults"/>. Pure and DI-free so it can be unit-tested directly:
    /// it reads <see cref="AlphaChainGameState.Results"/> and emits a string→string table the
    /// home page renders verbatim. Match-level keys are always present; personal keys
    /// (Result, Placement, Score, Words Played) are added only when <paramref name="currentUserId"/>
    /// is one of the ranked players.
    /// </summary>
    internal static class AlphaChainPlayLogMetadata
    {
        public static IReadOnlyDictionary<string, string> Build(AlphaChainGameState state, Guid? currentUserId)
        {
            var metadata = new Dictionary<string, string>();

            var results = state.Results;
            if (results is null)
                return metadata;

            var rankings = results.Rankings;

            // Match-level keys (always present).
            var winner = rankings.FirstOrDefault(r => r.UserId == results.WinnerUserId);
            metadata.Set(StandardMetadata.Winner, winner?.DisplayName ?? string.Empty);
            metadata.Set(StandardMetadata.Players, rankings.Count.ToString());
            metadata["Total Words"] = results.TotalWordsPlayed.ToString();
            metadata.Set(StandardMetadata.Duration, results.Duration.ToString(@"mm\:ss"));

            // Personal keys — only when the local user actually played this match.
            if (currentUserId is { } userId)
            {
                int index = -1;
                for (int i = 0; i < rankings.Count; i++)
                {
                    if (rankings[i].UserId == userId)
                    {
                        index = i;
                        break;
                    }
                }

                if (index >= 0)
                {
                    var mine = rankings[index];
                    metadata.Set(StandardMetadata.Result, userId == results.WinnerUserId
                        ? "Won"
                        : mine.Eliminated ? "Eliminated" : "Survived");
                    metadata.Set(StandardMetadata.Placement, $"{index + 1} / {rankings.Count}");
                    metadata.Set(StandardMetadata.Score, mine.Score.ToString());
                    metadata["Words Played"] = mine.WordsPlayed.ToString();
                }
            }

            return metadata;
        }
    }
}
