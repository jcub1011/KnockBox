namespace KnockBox.AlphaChain.Contracts;

/// <summary>
/// A card's standardized accent — the family it belongs to, encoded as a fixed border color so a
/// player can recognize a card's purpose at a glance. The palette is small and standardized (one
/// color per family); cards expose their accent via <c>IModifierCard.GetAccent</c> (server-side)
/// rather than hard-coding a hex, and the client maps the accent to a CSS color token.
/// </summary>
public enum CardAccent
{
    /// <summary>Word/letter scoring cards.</summary>
    Letter,

    /// <summary>Shot-clock cards.</summary>
    Clock,

    /// <summary>Points/economy and aggression cards.</summary>
    Economy,

    /// <summary>Utility, defensive, and policy cards.</summary>
    Utility,

    /// <summary>Fallback for an unknown/inert card.</summary>
    Neutral,
}
