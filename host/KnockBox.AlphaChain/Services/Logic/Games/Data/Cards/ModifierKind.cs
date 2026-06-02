namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// How a <see cref="ModifierCard"/> folds into the running score. Additive cards add
    /// their value; multiplicative cards multiply the running total. The scoring pipeline
    /// (<c>Score = (L + ΣA) × ΠM</c>) is realised by the left-to-right walk in
    /// <c>ScoreCalculator</c>, not by this enum alone — placement within the Engine Bay
    /// matters (see <c>ScoreCalculator</c> for the ordering intent).
    /// </summary>
    public enum ModifierKind
    {
        Additive,
        Multiplicative
    }
}
