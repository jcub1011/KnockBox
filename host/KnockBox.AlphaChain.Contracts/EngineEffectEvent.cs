namespace KnockBox.AlphaChain.Contracts;

/// <summary>Broad role of an automated engine effect, used purely for UI tinting.</summary>
public enum EngineEffectClass
{
    /// <summary>Protects or benefits the owner only (e.g. a Faraday-style self-boost).</summary>
    Defensive,

    /// <summary>Acts against an opponent (Flak Cannon, Scattershot, Bounty Hunter, Tracer Round).</summary>
    Offensive,

    /// <summary>A block/reflect or board-wide effect (The Titanium Mirror).</summary>
    Special
}

/// <summary>
/// A record of an automated engine effect that fired, published so every client can animate it —
/// and so the affected player understands what hit them and why. Display names ride along (no
/// client lookups), mirroring <c>ScoreReplay.TaxCollectors</c>. Submission-time effects are carried
/// on the word's <c>ScoreReplay</c>; off-submission effects are surfaced via the state's notice channel.
/// </summary>
/// <param name="CardId">The modifier card whose capability fired — its <see cref="ModifierId"/>, which also keys its icon glyph.</param>
/// <param name="CardName">Display name of the card.</param>
/// <param name="Class">Defensive / Offensive / Special, for tinting.</param>
/// <param name="HolderUserId">The player who owns the firing card.</param>
/// <param name="HolderName">Display name of the holder.</param>
/// <param name="TargetUserId">The player the effect affected, or null for self/holder-only effects.</param>
/// <param name="TargetName">Display name of the target, or null.</param>
/// <param name="Reason">Short human-readable explanation of what happened ("−2s next clock").</param>
/// <param name="Negated">True when this fire was a Titanium Mirror blocking/reflecting an incoming attack.</param>
public sealed record EngineEffectEvent(
    ModifierId CardId,
    string CardName,
    EngineEffectClass Class,
    Guid HolderUserId,
    string HolderName,
    Guid? TargetUserId,
    string? TargetName,
    string Reason,
    bool Negated = false);
