namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// A modifier card that lives in a player's Engine Bay and folds into the scoring
    /// pipeline. Cards are identified across the network by their stable <see cref="Id"/>
    /// — never by index. The <see cref="Trigger"/> and <see cref="Value"/> delegates are
    /// <b>not</b> serialisable; they live on the singleton <see cref="ModifierLibrary"/>
    /// and are referenced by id, so per-player state stores the immutable record
    /// reference (resolved against the library) rather than the lambdas themselves.
    /// </summary>
    /// <param name="Id">Stable identifier used to reference the card across the network.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Description">Human-readable rules text for the tooltip.</param>
    /// <param name="Kind">Whether the card adds to or multiplies the running score.</param>
    /// <param name="Trigger">
    /// Returns true when the card contributes for the given word. Unconditional cards
    /// return true for every word.
    /// </param>
    /// <param name="Value">
    /// The additive bonus or multiplicative factor for the given word. Evaluated only when
    /// <see cref="Trigger"/> returns true.
    /// </param>
    public sealed record ModifierCard(
        string Id,
        string Name,
        string Description,
        ModifierKind Kind,
        Func<WordContext, bool> Trigger,
        Func<WordContext, double> Value);
}
