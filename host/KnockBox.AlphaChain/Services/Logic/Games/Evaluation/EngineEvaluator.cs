using System.Globalization;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Scoring;

namespace KnockBox.AlphaChain.Services.Logic.Games.Evaluation
{
    /// <summary>
    /// Sequential implementation of the Alpha Chain scoring pipeline. The running value is seeded with
    /// the word length and each triggered card folds itself in via
    /// <see cref="IModifierCard.ExecuteModifier"/> — additive cards add, multiplicative cards multiply
    /// (their factor scaled by <see cref="EngineEvaluationContext.MultiplierScale"/>, seeded from the
    /// bay's <see cref="IMultiplierScaleProvider"/> cards). Card placement is meaningful: the walk is
    /// strictly left → right, so a × placed before a + multiplies a smaller base. The final running
    /// total is rounded half-up and clamped to <see cref="ModifierMath.MaxWordScore"/>.
    /// </summary>
    public sealed class EngineEvaluator : IEngineEvaluator
    {
        public int Calculate(EngineEvaluationContext context) => CalculateSteps(context, taxed: false).FinalBeforeTax;

        public ScoreBreakdown CalculateSteps(EngineEvaluationContext context, bool taxed)
        {
            var bay = context.Bay;

            // Seed the running value (word length) and the Hyper-Drive multiplier scale once, and ensure
            // an effect magnifier exists so a scoring context built without one (the evaluator unit tests,
            // the Tax Write-Off re-entry) still resolves Magnifying Glass effects from the bay.
            var ctx = context with
            {
                ScoreContext = new ScoreContext(context.Word.Length),
                MultiplierScale = bay.MultiplierScale(context),
                EffectMagnifier = context.EffectMagnifier ?? EffectMagnifier.ForBay(bay),
            };

            var steps = new List<ScoreStep>(bay.Count);

            for (int i = 0; i < bay.Count; i++)
            {
                var card = bay[i];
                ctx = ctx with { ModifierCardIndex = i };

                bool triggered = card.CheckIfTriggered(ctx);
                string valueText = "—";

                if (triggered)
                {
                    double before = ctx.ScoreContext.CurrentScore;
                    ctx = card.ExecuteModifier(ctx, card);
                    double delta = ctx.ScoreContext.CurrentScore - before;

                    // A triggered card that moves the score shows its signed delta; one that fired but
                    // made no direct score change (an effect card — the Magnifying Glass, Flak Cannon, a
                    // ×1 capability card) reads "FX" (the effect-chip convention), distinct from the "—"
                    // of a card that never triggered. The FX decision is on the actual delta, not its
                    // rounded display string, so a genuine sub-rounding scoring delta is never mislabeled
                    // as inert.
                    bool noScoreChange = Math.Abs(delta) < 1e-9;
                    valueText = noScoreChange ? "FX" : (delta >= 0 ? "+" : "−") + FormatValue(Math.Abs(delta));
                }

                steps.Add(new ScoreStep(
                    card.GetId(), card.GetName(),
                    card.GetAccent(), triggered, valueText, ModifierMath.ClampScore(ctx.ScoreContext.CurrentScore)));
            }

            int finalBeforeTax = ModifierMath.ClampScore(ctx.ScoreContext.CurrentScore);
            return new ScoreBreakdown(
                context.Word, context.Word.Length, steps, finalBeforeTax, taxed, taxed ? 0 : finalBeforeTax);
        }

        /// <summary>Formats a value for display, dropping a trailing ".0" (2 → "2", 1.5 → "1.5").</summary>
        private static string FormatValue(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
