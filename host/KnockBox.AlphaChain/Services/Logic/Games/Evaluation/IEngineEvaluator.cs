using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Scoring;

namespace KnockBox.AlphaChain.Services.Logic.Games.Evaluation
{
    /// <summary>
    /// Runs a word through a player's ordered Engine Bay (<see cref="EngineEvaluationContext.Bay"/>),
    /// folding each triggered card's <see cref="Data.Cards.Library.IModifierCard.ExecuteModifier"/>
    /// into the running value strictly left → right. Deterministic for a given context + bay.
    /// Replaces the legacy <c>IScoreCalculator</c>.
    /// </summary>
    public interface IEngineEvaluator
    {
        /// <summary>The word's final (rounded, clamped) score, before any Zero-Point Tax.</summary>
        int Calculate(EngineEvaluationContext context);

        /// <summary>
        /// The same left → right walk as <see cref="Calculate"/>, capturing each card's per-step
        /// contribution for the score-replay animation. <paramref name="taxed"/> zeroes the final score.
        /// </summary>
        ScoreBreakdown CalculateSteps(EngineEvaluationContext context, bool taxed);
    }
}
