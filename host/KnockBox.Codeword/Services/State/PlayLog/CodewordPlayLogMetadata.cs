using KnockBox.Codeword.Services.State.Games;
using KnockBox.Core.Services.State.PlayLog;

namespace KnockBox.Codeword.Services.State.PlayLog
{
    /// <summary>
    /// Builds the metadata dictionary for a Codeword match-level play-log entry.
    /// Pure and side-effect free so it can be unit-tested against a seeded
    /// <see cref="CodewordGameState"/> without a Blazor circuit. All values are
    /// strings, as required by <c>GameLog.Metadata</c>.
    /// </summary>
    internal static class CodewordPlayLogMetadata
    {
        /// <summary>
        /// Produces the Title-Case-keyed metadata for the finished match. Match-level
        /// keys are always present; personal keys ("My Score", "Placement", "My Role")
        /// are added only when <paramref name="currentUserId"/> is one of the match's
        /// participants.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Build(CodewordGameState state, Guid? currentUserId)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Games Played"] = state.Settings.TotalGames.ToString(),
                ["Outcome"] = DescribeOutcome(state.WinResult),
            };
            metadata.Set(StandardMetadata.Players, state.GamePlayers.Count.ToString());

            if (currentUserId is { } userId && state.GamePlayers.TryGetValue(userId, out var me))
            {
                int myScore = state.GameScores.TryGetValue(userId, out var s) ? s : 0;
                metadata["My Score"] = myScore.ToString();
                metadata.Set(StandardMetadata.Placement, $"{PlacementOf(state, userId)} / {state.GamePlayers.Count}");
                metadata["My Role"] = me.Role.ToString();
            }

            return metadata;
        }

        /// <summary>
        /// 1-based placement of <paramref name="userId"/> when participants are ranked
        /// by cumulative score (highest first). Ties share the lower placement number.
        /// </summary>
        private static int PlacementOf(CodewordGameState state, Guid userId)
        {
            int myScore = state.GameScores.TryGetValue(userId, out var s) ? s : 0;
            int ahead = state.GamePlayers.Keys
                .Count(id => id != userId
                    && (state.GameScores.TryGetValue(id, out var other) ? other : 0) > myScore);
            return ahead + 1;
        }

        private static string DescribeOutcome(WinConditionResult? winResult)
        {
            if (winResult?.WinningTeam is not { } team)
                return "Undecided";

            return team switch
            {
                Role.Agent => "Agents",
                Role.Insider => "Insiders",
                Role.Informant => "Informant",
                _ => "Unknown",
            };
        }
    }
}
