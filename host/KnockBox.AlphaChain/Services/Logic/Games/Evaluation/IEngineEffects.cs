using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Evaluation
{
    /// <summary>
    /// The resolution-time engine facade a card's side-effect hooks use: it applies automated attacks
    /// (each routed through the victim's <see cref="IAttackInterceptor"/> for block-and-reflect),
    /// navigates the turn order, and collects the <see cref="EngineEffectEvent"/> notices the UI
    /// animates. Plugin-internal and per-room; resolved from
    /// <see cref="Data.EngineEvaluationContext.Services"/>. The attack/notice/navigation concerns are
    /// folded into one facade because an attacking card needs all three together.
    /// </summary>
    public interface IEngineEffects
    {
        /// <summary>
        /// Queues a shot-clock shave on <paramref name="victim"/>'s next turn — or, if their Titanium
        /// Mirror intercepts it, on <paramref name="caster"/> instead. Posts the resulting notice.
        /// </summary>
        void TimeShave(IModifierCard source, AlphaChainPlayerState caster, AlphaChainPlayerState victim, int seconds);

        /// <summary>
        /// Drains points from <paramref name="victim"/> — or, if their Titanium Mirror intercepts it,
        /// from <paramref name="caster"/> instead. Posts the resulting notice.
        /// </summary>
        void Drain(IModifierCard source, AlphaChainPlayerState caster, AlphaChainPlayerState victim, int points);

        /// <summary>
        /// Forces a personal banned letter onto <paramref name="victim"/>'s next word — or, if their
        /// Titanium Mirror intercepts it, onto <paramref name="caster"/> instead. A victim already
        /// carrying a personal ban is left as-is. Posts the resulting notice.
        /// </summary>
        void LetterHijack(IModifierCard source, AlphaChainPlayerState caster, AlphaChainPlayerState victim, char letter);

        /// <summary>The next active player after <paramref name="fromUserId"/> in turn order (skipping
        /// eliminated/left seats), without mutating the turn manager. Null when no other active player exists.</summary>
        AlphaChainPlayerState? PeekNextActivePlayer(Guid fromUserId);

        /// <summary>Active players (not eliminated/left) in turn order — deterministic iteration for fan-out effects.</summary>
        IEnumerable<AlphaChainPlayerState> OrderedActivePlayers();

        /// <summary>The id of the round's marked leader (for the Bounty Hunter), or null.</summary>
        Guid? RoundLeaderUserId { get; }

        /// <summary>Records an engine-effect notice for the UI to animate.</summary>
        void AddNotice(EngineEffectEvent notice);

        /// <summary>
        /// Records an era-tax siphon collection (Tax Collector) for the score-replay's "stolen by …"
        /// line — distinct from <see cref="AddNotice"/>, which drives the generic effect rows.
        /// </summary>
        void RecordEraTaxSiphon(string collectorDisplayName, int amount);
    }
}
