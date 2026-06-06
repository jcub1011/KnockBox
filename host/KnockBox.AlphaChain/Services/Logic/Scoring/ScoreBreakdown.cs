using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;

namespace KnockBox.AlphaChain.Services.Logic.Scoring
{
    /// <summary>
    /// One card's contribution as the word walks through the Engine Bay, captured so the
    /// score-replay overlay can animate the running total updating per card. Plain data (no
    /// delegates) so it is safe to stash on game state and render to every client.
    /// </summary>
    /// <param name="CardId">The card at this step — its <see cref="ModifierId"/>, which also keys its icon glyph.</param>
    /// <param name="Name">Card display name.</param>
    /// <param name="Accent">The card's standardized accent (family color), for tinting the step.</param>
    /// <param name="Triggered">Whether the card's trigger fired for this word.</param>
    /// <param name="ValueText">The score delta this card applied (e.g. "+12", "−4"), or "—" when skipped.</param>
    /// <param name="RunningScore">The running score (rounded, clamped) after this step.</param>
    public sealed record ScoreStep(
        ModifierId CardId,
        string Name,
        CardAccent Accent,
        bool Triggered,
        string ValueText,
        int RunningScore);

    /// <summary>
    /// The full per-step trace of a scored word: the seed (word length) and each Engine Bay
    /// card's contribution, ending in the final score (and whether the Zero-Point Tax zeroed it).
    /// </summary>
    /// <param name="Word">The scored word (normalized).</param>
    /// <param name="Seed">The starting score (word length).</param>
    /// <param name="Steps">
    /// Per-card steps, in pipeline order (left → right). Each <see cref="ScoreStep.RunningScore"/>
    /// reflects only the pure scoring fold; post-fold adjustments applied outside the pipeline
    /// (the IRS Agent's flat taxed score, Tax Write-Off's salvage) land on <paramref name="FinalScore"/>
    /// alone, so the last step's running total need not equal it.
    /// </param>
    /// <param name="FinalBeforeTax">The score after the full pipeline, before any tax.</param>
    /// <param name="Taxed">True when the Zero-Point Tax applied (the pure fold scored 0).</param>
    /// <param name="FinalScore">
    /// The points actually awarded. Normally 0 when <paramref name="Taxed"/>, but a self-tax salvage
    /// (Tax Write-Off) or override (IRS Agent) can make it non-zero even on a taxed word.
    /// </param>
    public sealed record ScoreBreakdown(
        string Word,
        int Seed,
        IReadOnlyList<ScoreStep> Steps,
        int FinalBeforeTax,
        bool Taxed,
        int FinalScore);
}
