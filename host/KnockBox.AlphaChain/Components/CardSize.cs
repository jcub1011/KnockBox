namespace KnockBox.AlphaChain.Components
{
    /// <summary>
    /// The two purpose-built card footprints rendered by <c>GameCard</c>. <see cref="Small"/> is an
    /// icon-forward tile for recognising a card at a glance (Engine Bay during a round, opponent
    /// summaries, the discard bin); <see cref="Large"/> is a flippable card that teaches a card during
    /// the deal/optimization phase and the card library — title + chips on the front, the full
    /// description on the back. Both have fixed dimensions so cards always line up.
    /// </summary>
    public enum CardSize
    {
        /// <summary>Compact, icon + chips only — for in-round recognition.</summary>
        Small,

        /// <summary>Full flip card — front teaches identity, back holds the description.</summary>
        Large,
    }
}
