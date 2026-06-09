namespace KnockBox.AlphaChain.Contracts;

/// <summary>
/// Identifies which scripted tutorial is showing. Each tutorial is a self-contained,
/// non-interactive animation that explains one system; gameplay never runs during one.
/// They auto-advance after a fixed dwell and the host can skip ahead.
/// </summary>
public enum TutorialKind
{
    /// <summary>Plays right as the game starts — word entry and the shiritori chain rule.</summary>
    Shiritori,

    /// <summary>Plays once the first era ends — the Engine Bay (modifiers) and reaction cards.</summary>
    Engine,

    /// <summary>Plays before the first Sniper Ban — the Zero-Point Tax and banned-letter words.</summary>
    Tax
}
