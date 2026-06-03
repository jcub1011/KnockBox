using System.Buffers;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>Whether a modifier adds to, or multiplies against, the running score. Informational
    /// only — the actual fold lives in <see cref="IModifierCard.ExecuteModifier"/>; this drives the
    /// UI operator glyph (+ vs ×) and tinting.</summary>
    public enum ModifierType
    {
        /// <summary>A modifier that adds to a score.</summary>
        Additive,

        /// <summary>A modifier that multiplies against a score.</summary>
        Multiplier,
    }

    /// <summary>Strongly-typed identity for every modifier card, replacing the legacy string ids.
    /// The factory creates a card from one of these; the network/UI id surface is <c>ToString()</c>.</summary>
    public enum ModifierId
    {
        Unknown = 0,
        TheAnchor,
        Vanilla,
        ConsonantCrunch,
        VocalVowels,
        VowelSurge,
        TheArchitect,
        BrickLayer,
        Speedracer,
        LetterHoarder,
        Sesquipedalian,
        GutturalRoar,
        HighRoller,
        PerfectLink,
        TheVault,
        Redline,
        PanicButton,
        HyperDrive,
        TaxCollector,
        IrsAgent,
        RouletteWheel,
        TollBooth,
        BaitAndSwitch,
        Blindfold,
        DoubleDown,
        AnchorChain,
        FlakCannon,
        BountyHunter,
        TitaniumMirror,
        HeatSink,
        Prism,
        Wildcard,
        Catalyst,
    }

    /// <summary>
    /// A self-contained modifier card: it owns its identity, presentation, scoring, and every
    /// side-effecting behavior. Scoring folds through <see cref="ExecuteModifier"/>; the lifecycle
    /// hooks each take and return an <see cref="EngineEvaluationContext"/> and default to no-ops, so a
    /// simple scoring card overrides nothing. Cross-card and engine-policy behavior is expressed via
    /// the capability interfaces below (discovered by walking the bay), never by referencing another
    /// card's concrete type.
    /// </summary>
    public interface IModifierCard
    {
        /// <summary>The card's stable identity.</summary>
        ModifierId GetId();

        /// <summary>The card's display name.</summary>
        string GetName();

        /// <summary>The card's stable icon key (resolved by <c>CardIcon</c> to an inline SVG glyph).</summary>
        string GetIcon();

        /// <summary>The card's static rules text, for display outside an evaluation (bay/optimization UI).</summary>
        string GetDescription();

        /// <summary>The card's rules text given a live evaluation context. Defaults to <see cref="GetDescription()"/>.</summary>
        string GetDescription(EngineEvaluationContext context) => GetDescription();

        /// <summary>Whether the card adds or multiplies for this context (UI glyph/tinting only).</summary>
        ModifierType GetModifierType(EngineEvaluationContext context);

        /// <summary>Whether the card contributes for the current word. Unconditional cards return true.</summary>
        bool CheckIfTriggered(EngineEvaluationContext context);

        /// <summary>
        /// Folds this card into the scoring pipeline, returning the context with an updated
        /// <see cref="EngineEvaluationContext.ValueToAdd"/>. Called only when
        /// <see cref="CheckIfTriggered"/> returned true. <paramref name="self"/> is this card, so
        /// capability helpers (e.g. <c>self.GetConsonantIndicies(ctx)</c>) can walk the bay up to it.
        /// </summary>
        EngineEvaluationContext ExecuteModifier(EngineEvaluationContext context, IModifierCard self);

        // ── Lifecycle hooks (default no-ops; override only what a card needs) ────

        /// <summary>Fired once on the owner at era start, after the bay is final and the era ban is set
        /// (Roulette Wheel / Toll Booth roll a personal ban).</summary>
        EngineEvaluationContext OnEraStart(EngineEvaluationContext context, IModifierCard self) => context;

        /// <summary>Fired on the owner after their word is scored and credited (Hyper-Drive latch).</summary>
        EngineEvaluationContext OnWordAccepted(EngineEvaluationContext context, IModifierCard self) => context;

        /// <summary>Fired on the owner at the end of their turn — automated aggression and letter hijacks
        /// (Flak Cannon, Bait &amp; Switch).</summary>
        EngineEvaluationContext OnTurnEnded(EngineEvaluationContext context, IModifierCard self) => context;

        /// <summary>Fired on every other active player's cards after a word resolves
        /// (Tax Collector, Toll Booth, Bounty Hunter). The resolved word is on
        /// <see cref="EngineEvaluationContext.OpponentResolution"/>.</summary>
        EngineEvaluationContext OnOpponentWordResolved(EngineEvaluationContext context, IModifierCard self) => context;

        /// <summary>Fired on the owner when their submission fails validation (a typo) — The Prism refills the clock.</summary>
        EngineEvaluationContext OnValidationFailed(EngineEvaluationContext context, IModifierCard self) => context;
    }

    // ── Capability interfaces (discovered by walking the bay) ───────────────────

    /// <summary>Overrides consonant classification for cards evaluated after this one (The Catalyst).</summary>
    public interface IConsonantChecker
    {
        /// <summary>Checks if the given character is a consonant.</summary>
        bool IsConsonant(char character);
    }

    /// <summary>Overrides vowel classification for cards evaluated after this one (The Catalyst).</summary>
    public interface IVowelChecker
    {
        /// <summary>Checks if the given character is a vowel.</summary>
        bool IsVowel(char character);
    }

    /// <summary>Pins the owner's shot clock to a fixed, unmodifiable length for the era (The Anchor
    /// Chain; Hyper-Drive while latched). The smallest override among the bay wins.</summary>
    public interface IShotClockOverride
    {
        /// <summary>The fixed clock length in seconds, or null when this card isn't overriding right now.</summary>
        int? GetFixedShotClockSeconds(EngineEvaluationContext context);
    }

    /// <summary>Replaces the owner's <i>base</i> shot clock for the era while active (Hyper-Drive,
    /// when latched). Unlike <see cref="IShotClockOverride"/> the per-owner clock effects still fold in
    /// on top. The smallest base among the bay wins.</summary>
    public interface IBaseShotClockProvider
    {
        /// <summary>The replacement base clock in seconds, or null when not currently active.</summary>
        int? GetBaseShotClockSeconds(EngineEvaluationContext context);
    }

    /// <summary>A permanent per-owner shot-clock change folded in at clock-arm time (Vault, Redline,
    /// Panic Button, Heat Sink). Fractions are summed then applied, then flat seconds added.</summary>
    public interface IShotClockModifier
    {
        /// <summary>Fractional clock delta (e.g. -0.10 shortens by 10%).</summary>
        double FractionDelta { get; }

        /// <summary>Flat clock delta in seconds.</summary>
        int FlatDelta { get; }
    }

    /// <summary>Scales every multiplicative card's factor for the owner (Hyper-Drive doubling).
    /// Returns 1.0 when not currently active.</summary>
    public interface IMultiplierScaleProvider
    {
        /// <summary>The active multiplier scale (1.0 when inactive).</summary>
        double GetMultiplierScale(EngineEvaluationContext context);
    }

    /// <summary>Overrides the owner's own Zero-Point Tax outcome (The IRS Agent).</summary>
    public interface IOwnTaxPolicy
    {
        /// <summary>The score the owner keeps when their own word is taxed (instead of 0).</summary>
        int GetTaxedScore(EngineEvaluationContext context, int wouldBeScore);

        /// <summary>When true, no opponent's siphon collects from the owner's taxed word.</summary>
        bool SuppressesSiphonBounty { get; }
    }

    /// <summary>Lets the owner ignore the Succession (chain) rule (The Wildcard).</summary>
    public interface ISuccessionExemption
    {
        /// <summary>Whether the owner's word may ignore the required start letter.</summary>
        bool IgnoresSuccession(EngineEvaluationContext context);
    }

    /// <summary>Grants the owner immunity to their own era-rolled personal card-bans (The Faraday Cage).</summary>
    public interface IOwnCardBanImmunity
    {
        /// <summary>Whether the owner is immune to their own card-bans for the Zero-Point Tax.</summary>
        bool IsImmuneToOwnCardBans(EngineEvaluationContext context);
    }

    /// <summary>Blocks and reflects an incoming automated attack back at its caster (The Titanium
    /// Mirror). Implementations decay their own shield state on the owner.</summary>
    public interface IAttackInterceptor
    {
        /// <summary>Attempts to intercept an attack targeting <paramref name="owner"/>; returns true when
        /// it blocked (and reflected) the hit, having applied its own decay. <paramref name="self"/> is the
        /// intercepting card.</summary>
        bool TryIntercept(AlphaChainPlayerState owner, IModifierCard self);
    }

    /// <summary>Hides the owner's own word-input box while typing (The Blindfold). Presentational.</summary>
    public interface IInputMask
    {
        /// <summary>Whether the owner's input is hidden.</summary>
        bool HidesOwnInput(EngineEvaluationContext context);
    }

    /// <summary>Masks the previous word's first/last letters in the owner's UI only (Tunnel Vision). Presentational.</summary>
    public interface IPreviousWordMask
    {
        /// <summary>Whether the previous word is masked for the owner.</summary>
        bool MasksPreviousWord(EngineEvaluationContext context);
    }

    /// <summary>
    /// Capability discovery helpers. The letter-classification walks pick the <i>last</i> provider
    /// before <c>self</c> (a later card in the pipeline overrides an earlier one). The bay-wide policy
    /// helpers scan the whole bay (order-independent engine policy).
    /// </summary>
    public static class ModifierCapabilityExtensions
    {
        private static readonly SearchValues<char> VowelSet = SearchValues.Create("aeiou");

        /// <summary>The indices of consonants in the word, honoring the most recent
        /// <see cref="IConsonantChecker"/> before <paramref name="currentCard"/> in the bay.</summary>
        public static IEnumerable<int> GetConsonantIndicies(this IModifierCard currentCard, EngineEvaluationContext context)
        {
            Func<char, bool> consonantChecker = static c => !VowelSet.Contains(c);

            foreach (var card in context.GetModifierCards(context.PlayerIndex))
            {
                if (card is IConsonantChecker cc) consonantChecker = cc.IsConsonant;
                if (card == currentCard) break;
            }

            for (int i = 0; i < context.Word.Length; i++)
                if (consonantChecker.Invoke(context.Word[i]))
                    yield return i;
        }

        /// <summary>The indices of vowels in the word, honoring the most recent
        /// <see cref="IVowelChecker"/> before <paramref name="currentCard"/> in the bay.</summary>
        public static IEnumerable<int> GetVowelIndicies(this IModifierCard currentCard, EngineEvaluationContext context)
        {
            Func<char, bool> vowelChecker = VowelSet.Contains;

            foreach (var card in context.GetModifierCards(context.PlayerIndex))
            {
                if (card is IVowelChecker vc) vowelChecker = vc.IsVowel;
                if (card == currentCard) break;
            }

            for (int i = 0; i < context.Word.Length; i++)
                if (vowelChecker.Invoke(context.Word[i]))
                    yield return i;
        }

        /// <summary>The product of every <see cref="IMultiplierScaleProvider"/>'s active scale in the bay (1.0 when none).</summary>
        public static double MultiplierScale(this IReadOnlyList<IModifierCard> bay, EngineEvaluationContext context)
        {
            double scale = 1.0;
            foreach (var card in bay)
                if (card is IMultiplierScaleProvider p)
                    scale *= p.GetMultiplierScale(context);
            return scale;
        }

        /// <summary>The smallest fixed shot-clock override among the bay, or null when none is active.</summary>
        public static int? FixedShotClockSeconds(this IReadOnlyList<IModifierCard> bay, EngineEvaluationContext context)
        {
            int? fixedSeconds = null;
            foreach (var card in bay)
                if (card is IShotClockOverride o && o.GetFixedShotClockSeconds(context) is { } s
                    && (fixedSeconds is null || s < fixedSeconds))
                    fixedSeconds = s;
            return fixedSeconds;
        }

        /// <summary>The smallest active base-clock replacement among the bay (Hyper-Drive), or null when none.</summary>
        public static int? BaseShotClockSeconds(this IReadOnlyList<IModifierCard> bay, EngineEvaluationContext context)
        {
            int? baseSeconds = null;
            foreach (var card in bay)
                if (card is IBaseShotClockProvider p && p.GetBaseShotClockSeconds(context) is { } s
                    && (baseSeconds is null || s < baseSeconds))
                    baseSeconds = s;
            return baseSeconds;
        }

        /// <summary>The summed fractional and flat shot-clock deltas across the bay's <see cref="IShotClockModifier"/> cards.</summary>
        public static (double Fraction, int Flat) ShotClockEffect(this IReadOnlyList<IModifierCard> bay)
        {
            double fraction = 0;
            int flat = 0;
            foreach (var card in bay)
                if (card is IShotClockModifier m)
                {
                    fraction += m.FractionDelta;
                    flat += m.FlatDelta;
                }
            return (fraction, flat);
        }

        /// <summary>Whether any card in the bay grants a Succession exemption (The Wildcard).</summary>
        public static bool IgnoresSuccession(this IReadOnlyList<IModifierCard> bay, EngineEvaluationContext context)
        {
            foreach (var card in bay)
                if (card is ISuccessionExemption e && e.IgnoresSuccession(context))
                    return true;
            return false;
        }

        /// <summary>The first <see cref="IOwnTaxPolicy"/> in the bay (The IRS Agent), or null.</summary>
        public static IOwnTaxPolicy? OwnTaxPolicy(this IReadOnlyList<IModifierCard> bay)
        {
            foreach (var card in bay)
                if (card is IOwnTaxPolicy p)
                    return p;
            return null;
        }

        /// <summary>Whether any card grants immunity to the owner's own card-bans (The Faraday Cage).</summary>
        public static bool ImmuneToOwnCardBans(this IReadOnlyList<IModifierCard> bay, EngineEvaluationContext context)
        {
            foreach (var card in bay)
                if (card is IOwnCardBanImmunity i && i.IsImmuneToOwnCardBans(context))
                    return true;
            return false;
        }

        /// <summary>Whether any card hides the owner's input (The Blindfold).</summary>
        public static bool HidesOwnInput(this IReadOnlyList<IModifierCard> bay, EngineEvaluationContext context)
        {
            foreach (var card in bay)
                if (card is IInputMask m && m.HidesOwnInput(context))
                    return true;
            return false;
        }

        /// <summary>Whether any card masks the previous word for the owner (Tunnel Vision).</summary>
        public static bool MasksPreviousWord(this IReadOnlyList<IModifierCard> bay, EngineEvaluationContext context)
        {
            foreach (var card in bay)
                if (card is IPreviousWordMask m && m.MasksPreviousWord(context))
                    return true;
            return false;
        }

        /// <summary>The first <see cref="IAttackInterceptor"/> in the bay (The Titanium Mirror), or null.</summary>
        public static IAttackInterceptor? AttackInterceptor(this IReadOnlyList<IModifierCard> bay)
        {
            foreach (var card in bay)
                if (card is IAttackInterceptor i)
                    return i;
            return null;
        }
    }
}
