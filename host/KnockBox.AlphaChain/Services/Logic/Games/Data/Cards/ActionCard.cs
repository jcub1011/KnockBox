namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The one-shot effects an Action card can perform. <see cref="Pivot"/> clears the
    /// required start letter for the player's next submission; <see cref="Amnesty"/>
    /// suppresses the Zero-Point Tax for the next submission; <see cref="TimeThief"/>
    /// shaves time off an opponent's shot clock.
    /// </summary>
    public enum ActionKind
    {
        Pivot,
        Amnesty,
        TimeThief
    }

    /// <summary>
    /// An action card held in a player's hand and played for a one-shot effect. Like
    /// <see cref="ModifierCard"/>, it is identified across the network by its stable
    /// <see cref="Id"/>. Actions carry no delegates — the FSM interprets <see cref="Kind"/>
    /// directly when the card is played.
    /// </summary>
    /// <param name="Id">Stable identifier used to reference the card across the network.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Description">Human-readable rules text for the tooltip.</param>
    /// <param name="Kind">Which effect the card performs when played.</param>
    public sealed record ActionCard(
        string Id,
        string Name,
        string Description,
        ActionKind Kind)
    {
        /// <summary>
        /// Stable icon key resolved by <c>CardIcon</c> to an inline SVG glyph, so players
        /// associate a distinct symbol with each card's function. Set via object initializer
        /// in <see cref="ActionLibrary"/>; defaults to empty for ad-hoc/test cards.
        /// </summary>
        public string Icon { get; init; } = string.Empty;
    }
}
