using KnockBox.AlphaChain.Services.Logic.Scoring;

namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// One accepted submission in the match's chronological history, backing the UI's play
    /// feed, the prior-words snapshot handed to scoring, the game-over totals, and the post-game
    /// history screen. <see cref="AlphaChainGameState.PlayedWords"/> enforces uniqueness; this
    /// record carries the per-submission metadata the feed renders — including the
    /// <see cref="Engine"/> trace so players can review which engine design produced each score.
    /// </summary>
    /// <param name="PlayedAt">When the word was accepted (server clock).</param>
    /// <param name="UserId">The submitting player's <c>User.Id</c>.</param>
    /// <param name="DisplayName">The submitting player's display name at play time.</param>
    /// <param name="Word">The normalized (trimmed, lower-case) word.</param>
    /// <param name="Score">Points awarded (0 when the Zero-Point Tax applied).</param>
    /// <param name="ZeroPointTax">True when the word contained the banned letter.</param>
    /// <param name="TaxBounty">
    /// Points each Tax Collector owner collected from this (taxed) word, or 0 when none applied.
    /// Non-zero only on a Zero-Point Tax play that one or more opponents taxed.
    /// </param>
    /// <param name="Engine">
    /// The per-card scoring trace for this word — the walk through the submitter's Engine Bay,
    /// each card's contribution and the running total. The "engine that produced this score."
    /// </param>
    public record AlphaChainSubmission(
        DateTimeOffset PlayedAt,
        Guid UserId,
        string DisplayName,
        string Word,
        int Score,
        bool ZeroPointTax,
        int TaxBounty,
        ScoreBreakdown Engine);
}
