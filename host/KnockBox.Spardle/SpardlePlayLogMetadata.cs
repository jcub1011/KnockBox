using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Spardle.Models;

namespace KnockBox.Spardle;

/// <summary>
/// Builds the metadata dictionary for a Spardle match-level play-log entry. Pure
/// and side-effect free so it can be unit-tested against a seeded
/// <see cref="SpardleState"/> without a Blazor circuit. All values are strings,
/// as required by <c>GameLog.Metadata</c>.
/// </summary>
internal static class SpardlePlayLogMetadata
{
    /// <summary>
    /// Produces the Title-Case-keyed metadata for the finished match. Match-level
    /// keys ("Rounds Played", "Players") are always present; personal keys
    /// ("My Score", "Rounds Won", "Placement") are added only when
    /// <paramref name="currentUserId"/> is one of the match's participants.
    /// Placement is computed by ranking participants on total score (highest
    /// first), tiebroken by rounds won — mirroring the final-standings screen.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Build(SpardleState state, Guid? currentUserId)
    {
        var metadata = new Dictionary<string, string>
        {
            ["Rounds Played"] = state.RoundHistory.Count.ToString(),
        };
        metadata.Set(StandardMetadata.Players, state.MatchParticipants.Length.ToString());

        if (currentUserId is { } userId && state.MatchParticipants.Any(p => p.User.Id == userId))
        {
            int myScore = state.PlayerStates.TryGetValue(userId, out var ps) ? ps.TotalScore : 0;
            int myRoundsWon = RoundsWon(state, userId);

            metadata["My Score"] = myScore.ToString();
            metadata["Rounds Won"] = myRoundsWon.ToString();
            metadata.Set(StandardMetadata.Placement, $"{PlacementOf(state, userId)} / {state.MatchParticipants.Length}");
        }

        return metadata;
    }

    /// <summary>
    /// Count of rounds in which <paramref name="userId"/> placed first
    /// (<c>Placement == 1</c>).
    /// </summary>
    private static int RoundsWon(SpardleState state, Guid userId) =>
        state.RoundHistory.Count(r => r.Outcomes.Any(o => o.UserId == userId && o.Placement == 1));

    /// <summary>
    /// 1-based placement of <paramref name="userId"/> when participants are ranked
    /// by total score (highest first), tiebroken by rounds won — the same ordering
    /// the final-standings screen uses. Players strictly ahead on this ordering
    /// determine the placement.
    /// </summary>
    private static int PlacementOf(SpardleState state, Guid userId)
    {
        int myScore = state.PlayerStates.TryGetValue(userId, out var ps) ? ps.TotalScore : 0;
        int myRoundsWon = RoundsWon(state, userId);

        int ahead = state.MatchParticipants.Count(p =>
        {
            if (p.User.Id == userId)
                return false;

            int otherScore = state.PlayerStates.TryGetValue(p.User.Id, out var ops) ? ops.TotalScore : 0;
            if (otherScore != myScore)
                return otherScore > myScore;

            return RoundsWon(state, p.User.Id) > myRoundsWon;
        });

        return ahead + 1;
    }
}
