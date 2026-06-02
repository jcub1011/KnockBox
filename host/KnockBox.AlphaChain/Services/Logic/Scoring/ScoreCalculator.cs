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
                    current *= MultiplicativeFactor(card, word);
            }

            return RoundClamp(current);
        }

        public ScoreBreakdown CalculateSteps(WordContext word, IReadOnlyList<ModifierCard> orderedBay, bool taxed)
        {
            double current = word.Length;
            var steps = new List<ScoreStep>(orderedBay.Count);

            foreach (var card in orderedBay)
            {
                bool triggered = card.Trigger(word);
                string valueText = "—";

                if (triggered)
                {
                    if (card.Kind == ModifierKind.Additive)
                    {
                        double value = card.Value(word);
                        current += value;
                        valueText = "+" + FormatValue(value);
                    }
                    else
                    {
                        double factor = MultiplicativeFactor(card, word);
                        current *= factor;
                        valueText = "×" + FormatValue(factor);
                    }
                }

                steps.Add(new ScoreStep(
                    card.Id, card.Name, card.Icon, card.Kind, triggered, valueText, RoundClamp(current)));
            }

            int finalBeforeTax = RoundClamp(current);
            return new ScoreBreakdown(
                word.Word, word.Length, steps, finalBeforeTax, taxed, taxed ? 0 : finalBeforeTax);
        }

        /// <summary>
        /// The effective multiplicative factor for a triggered card: its raw <see cref="ModifierCard.Value"/>
        /// scaled by <see cref="WordContext.MultiplierScale"/> (1.0 normally). Hyper-Drive raises the
        /// scale for an era so "all multipliers are doubled" without editing any individual card.
        /// </summary>
        private static double MultiplicativeFactor(ModifierCard card, WordContext word)
            => card.Value(word) * word.MultiplierScale;

        /// <summary>Round half-up (away from zero) then clamp to the legal score range.</summary>
        private static int RoundClamp(double value) =>
            Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, MaxWordScore);

        /// <summary>Formats a card value for display, dropping a trailing ".0" (e.g. 2 → "2", 1.5 → "1.5").</summary>
        private static string FormatValue(double value) =>
            value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
