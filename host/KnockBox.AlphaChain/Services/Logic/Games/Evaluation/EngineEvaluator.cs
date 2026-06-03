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

            // Seed the running value (word length) and the Hyper-Drive multiplier scale once.
            var ctx = context with
            {
                ValueToAdd = context.Word.Length,
                MultiplierScale = bay.MultiplierScale(context),
            };

            var steps = new List<ScoreStep>(bay.Count);

            for (int i = 0; i < bay.Count; i++)
            {
                var card = bay[i];
                ctx = ctx with { ModifierCardIndex = i };

                bool triggered = card.CheckIfTriggered(ctx);
                var type = card.GetModifierType(ctx);
                string valueText = "—";

                if (triggered)
                {
                    double before = ctx.ValueToAdd;
                    ctx = card.ExecuteModifier(ctx, card);
                    double after = ctx.ValueToAdd;

                    valueText = type == ModifierType.Additive
                        ? "+" + FormatValue(after - before)
                        : "×" + FormatValue(before == 0 ? 0 : after / before);
                }

                steps.Add(new ScoreStep(
                    card.GetId(), card.GetName(),
                    type, triggered, valueText, ModifierMath.ClampScore(ctx.ValueToAdd)));
            }

            int finalBeforeTax = ModifierMath.ClampScore(ctx.ValueToAdd);
            return new ScoreBreakdown(
                context.Word, context.Word.Length, steps, finalBeforeTax, taxed, taxed ? 0 : finalBeforeTax);
        }

        /// <summary>Formats a value for display, dropping a trailing ".0" (2 → "2", 1.5 → "1.5").</summary>
        private static string FormatValue(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
