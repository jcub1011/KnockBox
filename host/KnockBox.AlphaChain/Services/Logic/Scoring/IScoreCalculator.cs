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
    }
}
