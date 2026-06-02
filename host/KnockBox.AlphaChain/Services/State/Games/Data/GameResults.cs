namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// Final standings populated by <c>GameOverState</c> and consumed by the results
    /// screen. <see cref="Rankings"/> is ordered best-first: active players by score
    /// (descending), then eliminated players in reverse-elimination order (the last one
    /// out ranks above those knocked out earlier). <see cref="WinnerUserId"/> is the
    /// id of the rank-1 player — in Survival's last-player-standing finish that is the
    /// lone survivor, regardless of score.
    /// </summary>
    /// <param name="Rankings">All players, best placement first.</param>
    /// <param name="WinnerUserId">The winning player's <c>User.Id</c> (empty when there were no players).</param>
    /// <param name="TotalWordsPlayed">Total accepted plays across the whole match.</param>
    /// <param name="Duration">Wall-clock time from game start to game over.</param>
    public record GameResults(
        IReadOnlyList<PlayerResult> Rankings,
        string WinnerUserId,
        int TotalWordsPlayed,
        TimeSpan Duration);

    /// <summary>A single row of the final standings.</summary>
    /// <param name="UserId">The player's <c>User.Id</c>.</param>
    /// <param name="DisplayName">The player's per-lobby display name.</param>
    /// <param name="Score">The player's final score.</param>
    /// <param name="Eliminated">Whether the player was eliminated (Survival mode).</param>
    /// <param name="WordsPlayed">How many accepted plays this player contributed.</param>
    public record PlayerResult(
        string UserId,
        string DisplayName,
        int Score,
        bool Eliminated,
        int WordsPlayed);
}
