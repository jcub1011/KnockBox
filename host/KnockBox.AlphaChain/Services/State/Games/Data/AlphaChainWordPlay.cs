namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// One accepted entry in the match's submitted-words log, backing the UI's play
    /// feed. <see cref="AlphaChainGameState.PlayedWords"/> enforces uniqueness; this
    /// list preserves chronological order with the metadata the feed renders.
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
    public record AlphaChainWordPlay(
        DateTimeOffset PlayedAt,
        Guid UserId,
        string DisplayName,
        string Word,
        int Score,
        bool ZeroPointTax,
        int TaxBounty = 0);
}
