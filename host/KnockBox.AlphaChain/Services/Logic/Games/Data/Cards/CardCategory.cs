namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The two top-level kinds of card in Alpha Chain. Modifiers live in a player's
    /// Engine Bay and reshape the scoring pipeline; Actions are one-shot effects played
    /// from the hand.
    /// </summary>
    public enum CardCategory
    {
        Modifier,
        Action
    }
}
