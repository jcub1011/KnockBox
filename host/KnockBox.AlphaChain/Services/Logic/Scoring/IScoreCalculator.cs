using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;

namespace KnockBox.AlphaChain.Services.Logic.Scoring
{
    /// <summary>
    /// Computes a word's score by running it through a player's ordered Engine Bay.
    /// Deterministic: the same <see cref="WordContext"/> plus the same ordered bay always
    /// yields the same score (no randomness, no captured state).
    /// </summary>
    public interface IScoreCalculator
    {
        /// <summary>
        /// Scores <paramref name="word"/> against <paramref name="orderedBay"/>, walking the
        /// bay left → right and applying each card whose trigger fires.
        /// </summary>
        int Calculate(WordContext word, IReadOnlyList<ModifierCard> orderedBay);

        /// <summary>
        /// Same left → right walk as <see cref="Calculate"/>, but captures the per-step running
        /// total and each card's contribution for the score-replay animation. The breakdown's
        /// <see cref="ScoreBreakdown.FinalBeforeTax"/> equals <see cref="Calculate"/>'s result.
        /// </summary>
        /// <param name="taxed">When true, the Zero-Point Tax applied — the final score is 0.</param>
        ScoreBreakdown CalculateSteps(WordContext word, IReadOnlyList<ModifierCard> orderedBay, bool taxed);
    }
}
