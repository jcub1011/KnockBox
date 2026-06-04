using System.Collections.Immutable;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// The single context threaded through every <see cref="IModifierCard"/> hook — scoring and
    /// side-effecting alike. It is a <see langword="readonly"/> value: scoring/clock/running fields
    /// are rebuilt with <c>with</c> as the engine walks the bay (<see cref="ExecuteModifierLoop"/>
    /// in the evaluator), while cross-player state is mutated on the live
    /// <see cref="AlphaChainPlayerState"/> references reachable through <see cref="Players"/>.
    /// <para>
    /// Engine-level operations a card can't get from <see cref="Players"/> alone (rolling a personal
    /// ban, firing a shield-routed attack, refilling the shot clock) are resolved from
    /// <see cref="Services"/> — a hand-rolled, plugin-internal <see cref="IServiceProvider"/>. The
    /// context shape therefore stays stable as new capabilities are added: a new behavior is a new
    /// service, never a new field.
    /// </para>
    /// </summary>
    /// <param name="Word">The normalized (trimmed, lower-case) word being evaluated.</param>
    /// <param name="BannedLetters">Every banned letter in effect for the current player (era + personal + card bans).</param>
    /// <param name="Players">The live per-player states, in turn order, for cross-player reads/writes.</param>
    public readonly record struct EngineEvaluationContext(
        string Word,
        IEnumerable<char> BannedLetters,
        IEnumerable<AlphaChainPlayerState> Players)
    {
        /// <summary>
        /// The current player's ordered Engine Bay (left → right pipeline order) — the cards being
        /// evaluated this submission. Capability walks (consonant/vowel checkers, multiplier-scale
        /// providers, …) traverse this for the current player. Empty for an ad-hoc/display context.
        /// </summary>
        public IReadOnlyList<IModifierCard> Bay { get; init; } = [];

        /// <summary>
        /// Plugin-internal service locator for engine-level operations cards need during their hooks
        /// (<see cref="IBanLetterService"/>, <see cref="IEngineEffects"/>, <see cref="IShotClockService"/>).
        /// Null for a pure-scoring or UI-display context (simple scoring cards never resolve a service).
        /// </summary>
        public IServiceProvider? Services { get; init; }

        /// <summary>
        /// Per-evaluation effect-magnification registry, derived from <see cref="Bay"/>. A card pulls the
        /// magnification that applies to itself (a Magnifying Glass on its immediate left) and decides
        /// how to scale its own numbers. Null for an ad-hoc/display context, where it reads as no
        /// magnification (1.0); the evaluator builds one from the bay if a scoring context lacks it.
        /// </summary>
        public Cards.Library.IEffectMagnifier? EffectMagnifier { get; init; }

        /// <summary>The shot clock duration without any modifiers.</summary>
        public double ShotClockDuration { get; init; }

        /// <summary>The shot clock duration with modifiers.</summary>
        public double ModifiedShotClockDuration { get; init; }

        /// <summary>The remaining time in the shot clock when the engine began evaluation.</summary>
        public double RemainingShotClockDuration { get; init; }

        /// <summary>The score of the player when the engine began evaluation.</summary>
        public double Score { get; init; }

        /// <summary>The running value to add to the score; folded through each card and read back when evaluation completes.</summary>
        public double ValueToAdd { get; init; }

        /// <summary>The index (into <see cref="Players"/>) of the current player.</summary>
        public int PlayerIndex { get; init; }

        /// <summary>The index (into <see cref="Bay"/>) of the card currently evaluating.</summary>
        public int ModifierCardIndex { get; init; }

        /// <summary>
        /// Scale applied to every multiplicative card's factor (1.0 normally). Hyper-Drive raises it
        /// for an era so "all multipliers are doubled" without touching any individual card; seeded by
        /// the evaluator from the bay's <see cref="IMultiplierScaleProvider"/> cards.
        /// </summary>
        public double MultiplierScale { get; init; } = 1.0;

        /// <summary>
        /// The just-resolved submission relevant to the current hook: the owner's own word for
        /// <see cref="IModifierCard.OnWordAccepted"/>/<see cref="IModifierCard.OnTurnEnded"/>, or the
        /// opponent's for <see cref="IModifierCard.OnOpponentWordResolved"/>. Null outside those hooks.
        /// </summary>
        public WordResolution? Resolution { get; init; }

        /// <summary>
        /// Every submission accepted this match before the current one, in chronological order
        /// (any player), excluding the word being evaluated. Read by history-aware cards via each
        /// entry's <see cref="AlphaChainSubmission.Word"/> (The Blueprint compares the previous
        /// word's length; Scavenger counts a starting letter across all prior words). Empty for
        /// ad-hoc/display contexts that don't snapshot the match feed. An
        /// <see cref="ImmutableList{T}"/> so the long, frequently-appended match feed shares
        /// structure cheaply (passed by reference, no projection).
        /// </summary>
        public ImmutableList<AlphaChainSubmission> SubmissionHistory { get; init; } = ImmutableList<AlphaChainSubmission>.Empty;

        /// <summary>
        /// Attaches a bay and the effect-magnifier derived from it together, so the two can never
        /// diverge. Every site that puts a bay on the context must go through here — the scoring
        /// evaluator rebuilds a missing magnifier defensively, but the lifecycle-hook paths do not,
        /// so a bay set without its matching magnifier would silently read ×1.0 magnification.
        /// </summary>
        public EngineEvaluationContext WithBay(IReadOnlyList<IModifierCard> bay)
            => this with { Bay = bay, EffectMagnifier = Cards.Library.EffectMagnifier.ForBay(bay) };

        /// <summary>
        /// The ordered Engine Bay for the player at <paramref name="playerIndex"/>. For the current
        /// player this is <see cref="Bay"/>; for others it reads their live bay. Returns empty when the
        /// index is out of range.
        /// </summary>
        public IReadOnlyList<IModifierCard> GetModifierCards(int playerIndex)
        {
            if (playerIndex == PlayerIndex)
                return Bay;

            return GetPlayer(playerIndex)?.EngineBay ?? [];
        }

        /// <summary>Resolves a plugin-internal service from <see cref="Services"/>, or null when unavailable
        /// (e.g. a pure-scoring/display context). Side-effect hooks use this to reach engine operations.</summary>
        public T? Service<T>() where T : class => Services?.GetService(typeof(T)) as T;

        /// <summary>The live player state at <paramref name="playerIndex"/> (turn order), or null when out of range.</summary>
        public AlphaChainPlayerState? GetPlayer(int playerIndex)
        {
            if (playerIndex < 0) return null;
            int index = 0;
            foreach (var player in Players)
                if (index++ == playerIndex)
                    return player;
            return null;
        }

        /// <summary>The turn-order index of <paramref name="userId"/>, or -1 when not present.</summary>
        public int GetPlayerIndex(Guid userId)
        {
            int index = 0;
            foreach (var player in Players)
            {
                if (player.UserId == userId) return index;
                index++;
            }
            return -1;
        }

        /// <summary>The current player's score, or 0 when the index is out of range.</summary>
        public int GetScore(int playerIndex) => GetPlayer(playerIndex)?.Score ?? 0;
    }

    /// <summary>
    /// An immutable snapshot of a just-resolved submission, handed to
    /// <see cref="IModifierCard.OnOpponentWordResolved"/> so reactive cards can pay out against it.
    /// </summary>
    /// <param name="SubmitterUserId">The submitting player's id.</param>
    /// <param name="Word">The normalized submitted word.</param>
    /// <param name="Taxed">True when the Zero-Point Tax zeroed the submitter's word.</param>
    /// <param name="WouldBeScore">The score the word would have earned before any tax (the taxed-away value).</param>
    /// <param name="EarnedScore">The points the submitter actually kept (0 when taxed, unless salvaged).</param>
    /// <param name="OffendingLetter">The banned letter the word used (when taxed), else null.</param>
    public sealed record WordResolution(
        Guid SubmitterUserId,
        string Word,
        bool Taxed,
        int WouldBeScore,
        int EarnedScore,
        char? OffendingLetter)
    {
        /// <summary>When true (the submitter holds an <see cref="Cards.Library.IOwnTaxPolicy"/> that
        /// suppresses bounties), no opponent's era-tax siphon collects from this taxed word.</summary>
        public bool SiphonSuppressed { get; init; }

        /// <summary>Whole seconds left on the submitter's shot clock when they submitted. Read by
        /// Chrono Syphon (awards the owner a point per second remaining in opponents' submissions).</summary>
        public int RemainingSeconds { get; init; }
    }
}
