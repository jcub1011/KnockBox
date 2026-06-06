namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Submits a word for the active player's turn. The FSM validates the chain
    /// rule, uniqueness, and dictionary membership, applies the Zero-Point Tax,
    /// scores the word, and advances the turn. The typed outcome is surfaced to the
    /// UI via <see cref="SubmitWordResult"/> (see <c>AlphaChainGameContext.LastSubmitResult</c>).
    /// </summary>
    /// <param name="ActorUserId">The id of the player issuing the submission.</param>
    /// <param name="WordRaw">The raw, un-normalized word as typed by the player.</param>
    /// <param name="Now">
    /// The submission timestamp, captured at the engine boundary. Threaded in (rather than read
    /// from the wall clock inside the FSM) so time-aware scoring — remaining shot-clock seconds for
    /// Sprinter/Panic Button and the Hyper-Drive elapsed check — is deterministic under test.
    /// </param>
    public record SubmitWordCommand(Guid ActorUserId, string WordRaw, DateTimeOffset Now)
        : AlphaChainCommand(ActorUserId);
}
