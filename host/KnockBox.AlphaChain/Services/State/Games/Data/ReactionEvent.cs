using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;

namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// A record of a reaction that auto-fired, published so every client can animate it — and so
    /// the targeted player understands what hit them and why. Display names ride along (no client
    /// lookups), mirroring <see cref="ScoreReplay.TaxCollectors"/>. Submission-time reactions are
    /// carried on the word's <see cref="ScoreReplay"/>; off-submission reactions (Free Throw,
    /// Overtime) are surfaced via the state's notice channel.
    /// </summary>
    /// <param name="CardId">Stable id of the reaction that fired.</param>
    /// <param name="CardName">Display name of the reaction.</param>
    /// <param name="Icon">Icon key for <c>CardIcon</c>.</param>
    /// <param name="Class">Defensive / Offensive / Special, for tinting.</param>
    /// <param name="HolderUserId">The player who held (and fired) the reaction.</param>
    /// <param name="HolderName">Display name of the holder.</param>
    /// <param name="TargetUserId">The player the reaction affected, or null for self/holder-only effects.</param>
    /// <param name="TargetName">Display name of the target, or null.</param>
    /// <param name="Reason">Short human-readable explanation of what happened ("−5s next clock").</param>
    /// <param name="Negated">True when this fire was a Riposte negating/reflecting an incoming attack.</param>
    public sealed record ReactionEvent(
        string CardId,
        string CardName,
        string Icon,
        ReactionClass Class,
        string HolderUserId,
        string HolderName,
        string? TargetUserId,
        string? TargetName,
        string Reason,
        bool Negated = false);
}
