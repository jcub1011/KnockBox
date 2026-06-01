using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;

namespace KnockBox.AlphaChain.Services.Logic.Scoring
{
    /// <summary>
    /// Deterministic implementation of the Alpha Chain scoring pipeline
    /// <c>Score = (L + ΣA) × ΠM</c>.
    /// </summary>
    /// <remarks>
    /// The pipeline is realised by a <b>left-to-right walk</b> of the Engine Bay, seeded with
    /// the word length. Each triggered card folds in immediately: an additive card adds its
    /// value to the running total, a multiplicative card multiplies it. This makes card
    /// <i>placement</i> meaningful and is deliberate (GDD): additives stack first (place them
    /// on the left) and multiplicatives explode last (place them on the right). A player who
    /// places a × before a + multiplies a smaller base and loses value — that is by design.
    /// <para>
    /// Conditional cards (<see cref="ModifierCard.Trigger"/>) contribute only when their
    /// trigger fires for the word; otherwise they are skipped entirely. The final running
    /// total is rounded half-up to an int and capped at <see cref="MaxWordScore"/> so a stack
    /// of multiplicative conditionals can't blow the UI out (GDD risk note).
    /// </para>
    /// </remarks>
    public sealed class ScoreCalculator : IScoreCalculator
    {
        /// <summary>Upper bound on a single word's score, keeping the UI sane (GDD risk note).</summary>
        public const int MaxWordScore = 10_000;

        public int Calculate(WordContext word, IReadOnlyList<ModifierCard> orderedBay)
        {
            double current = word.Length;

            foreach (var card in orderedBay)
            {
                if (!card.Trigger(word)) continue;

                if (card.Kind == ModifierKind.Additive)
                    current += card.Value(word);
                else
                    current *= card.Value(word);
            }

            // Round half-up (away from zero) at the very end, then clamp.
            int rounded = (int)Math.Round(current, MidpointRounding.AwayFromZero);
            return Math.Clamp(rounded, 0, MaxWordScore);
        }
    }
}
