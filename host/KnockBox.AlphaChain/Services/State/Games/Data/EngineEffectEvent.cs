namespace KnockBox.AlphaChain.Services.State.Games.Data
{
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
    /// client lookups), mirroring <see cref="ScoreReplay.TaxCollectors"/>. Submission-time effects
    /// are carried on the word's <see cref="ScoreReplay"/>; off-submission effects are surfaced via
    /// the state's notice channel.
    /// </summary>
    /// <param name="CardId">Stable id of the modifier card whose capability fired.</param>
    /// <param name="CardName">Display name of the card.</param>
    /// <param name="Icon">Icon key for <c>CardIcon</c>.</param>
    /// <param name="Class">Defensive / Offensive / Special, for tinting.</param>
    /// <param name="HolderUserId">The player who owns the firing card.</param>
    /// <param name="HolderName">Display name of the holder.</param>
    /// <param name="TargetUserId">The player the effect affected, or null for self/holder-only effects.</param>
    /// <param name="TargetName">Display name of the target, or null.</param>
    /// <param name="Reason">Short human-readable explanation of what happened ("−2s next clock").</param>
    /// <param name="Negated">True when this fire was a Titanium Mirror blocking/reflecting an incoming attack.</param>
    public sealed record EngineEffectEvent(
        string CardId,
        string CardName,
        string Icon,
        EngineEffectClass Class,
        string HolderUserId,
        string HolderName,
        string? TargetUserId,
        string? TargetName,
        string Reason,
        bool Negated = false);
}
