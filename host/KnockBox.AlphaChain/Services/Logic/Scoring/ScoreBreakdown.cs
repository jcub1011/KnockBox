using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;

namespace KnockBox.AlphaChain.Services.Logic.Scoring
{
    /// <summary>
    /// One card's contribution as the word walks through the Engine Bay, captured so the
    /// score-replay overlay can animate the running total updating per card. Plain data (no
    /// delegates) so it is safe to stash on game state and render to every client.
    /// </summary>
    /// <param name="CardId">Stable id of the card at this step (the card's <see cref="ModifierId"/> token).</param>
    /// <param name="Name">Card display name.</param>
    /// <param name="Icon">Card icon key.</param>
    /// <param name="Kind">Additive or multiplicative.</param>
    /// <param name="Triggered">Whether the card's trigger fired for this word.</param>
    /// <param name="ValueText">The applied operator/value (e.g. "+12", "×1.5"), or "—" when skipped.</param>
    /// <param name="RunningScore">The running score (rounded, clamped) after this step.</param>
    public sealed record ScoreStep(
        string CardId,
        string Name,
        string Icon,
        ModifierType Kind,
        bool Triggered,
        string ValueText,
        int RunningScore);

    /// <summary>
    /// The full per-step trace of a scored word: the seed (word length) and each Engine Bay
    /// card's contribution, ending in the final score (and whether the Zero-Point Tax zeroed it).
    /// </summary>
    /// <param name="Word">The scored word (normalized).</param>
    /// <param name="Seed">The starting score (word length).</param>
    /// <param name="Steps">Per-card steps, in pipeline order (left → right).</param>
    /// <param name="FinalBeforeTax">The score after the full pipeline, before any tax.</param>
    /// <param name="Taxed">True when the Zero-Point Tax applied (final score is 0).</param>
    /// <param name="FinalScore">The points actually awarded (0 when <paramref name="Taxed"/>).</param>
    public sealed record ScoreBreakdown(
        string Word,
        int Seed,
        IReadOnlyList<ScoreStep> Steps,
        int FinalBeforeTax,
        bool Taxed,
        int FinalScore);
}
