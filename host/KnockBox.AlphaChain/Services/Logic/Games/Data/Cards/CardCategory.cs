namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The two top-level kinds of card in Alpha Chain. Modifiers live in a player's
    /// Engine Bay and reshape the scoring pipeline; Reactions sit in the hand and auto-fire
    /// on game events.
    /// </summary>
    public enum CardCategory
    {
        Modifier,
        Reaction
    }
}
