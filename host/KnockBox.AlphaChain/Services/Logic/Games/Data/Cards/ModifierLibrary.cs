using System.Collections.Immutable;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The canonical, immutable catalogue of every modifier card. The <c>Trigger</c>/<c>Value</c>
    /// delegates live here (on this single static list) and are referenced elsewhere only by
    /// <see cref="ModifierCard.Id"/>. The initial set is the GDD's modifiers plus a little
    /// filler for variety; add new cards here and they become draftable everywhere.
    /// </summary>
    public static class ModifierLibrary
    {
        /// <summary>Always-true trigger for unconditional cards.</summary>
        private static readonly Func<WordContext, bool> Always = static _ => true;

        /// <summary>Always-false trigger for cards that never fold into the owner's own scoring
        /// pipeline (their effect is resolved elsewhere — e.g. Tax Collector's reactive bounty).</summary>
        private static readonly Func<WordContext, bool> Never = static _ => false;

        /// <summary>Stable id of the Tax Collector card (resolved by the round's bounty payout).</summary>
        public const string TaxCollectorId = "tax-collector";

        /// <summary>Fraction of an opponent's would-be (taxed-away) word score that each Tax
        /// Collector owner collects.</summary>
        public const double TaxCollectorRate = 0.5;

        /// <summary>Multiplier Sprinter grants per second left on the shot clock (blitz reward).</summary>
        public const double SprinterPerSecond = 0.1;

        /// <summary>Fraction of a taxed word's would-be score an Enforcer owner collects (a stronger
        /// Tax Collector). The highest matching rate among a collector's cards wins — they don't stack.</summary>
        public const double EnforcerRate = 0.75;

        /// <summary>Fraction of an opponent's earned score a Smuggler's Toll owner is minted when the
        /// opponent's word uses the owner's era-rolled personal banned letter.</summary>
        public const double SmugglersTollRate = 0.20;

        /// <summary>Every modifier card, in catalogue order.</summary>
        public static readonly ImmutableArray<ModifierCard> All =
        [
            new ModifierCard(
                "anchor", "The Anchor",
                "Adds a flat +12 to your word, always.",
                ModifierKind.Additive,
                Always,
                static _ => 12) { Icon = "anchor" },

            new ModifierCard(
                "consonant-crunch", "Consonant Crunch",
                "Adds +2 for every consonant in your word.",
                ModifierKind.Additive,
                Always,
                static ctx => 2 * ctx.Consonants) { Icon = "consonant" },

            new ModifierCard(
                "vowel-surge", "Vowel Surge",
                "×2 when your word has more vowels than consonants.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Vowels > ctx.Consonants,
                static _ => 2) { Icon = "wave" },

            new ModifierCard(
                "architect", "The Architect",
                "×2 when your word is 8 letters or longer.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Length >= 8,
                static _ => 2.0) { Icon = "architect" },

            new ModifierCard(
                "brick-layer", "Brick Layer",
                "Adds +1 per letter when your word is 6 letters or longer.",
                ModifierKind.Additive,
                static ctx => ctx.Length >= 6,
                static ctx => ctx.Length) { Icon = "brick" },

            new ModifierCard(
                "sprinter", "Sprinter",
                "When your word is 4 letters or shorter, ×(1 + 0.1 per second left on your clock) — blitz short words for a soaring multiplier.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Length <= 4,
                static ctx => 1.0 + SprinterPerSecond * ctx.RemainingSeconds) { Icon = "sprinter" },

            new ModifierCard(
                "letter-hoarder", "Letter Hoarder",
                "Adds +1 for every distinct letter in your word.",
                ModifierKind.Additive,
                Always,
                static ctx => ctx.Word.Distinct().Count()) { Icon = "hoarder" },

            // ── Big-word / linguistic niche cards (feedback §1, §2, §4) ──
            new ModifierCard(
                "sesquipedalian", "Sesquipedalian",
                "×3 when your word is 10 letters or longer. Clamped to the max word score — a guaranteed payout for the truly long.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Length >= 10,
                static _ => 3.0) { Icon = "tower" },

            new ModifierCard(
                "guttural-roar", "Guttural Roar",
                "×1.5 when your word's only vowels are 'A' or 'E' (no I, O or U) — the multiplicative answer to Vowel Surge.",
                ModifierKind.Multiplicative,
                static ctx => !ctx.Word.Contains('i') && !ctx.Word.Contains('o') && !ctx.Word.Contains('u'),
                static _ => 1.5) { Icon = "roar" },

            new ModifierCard(
                "high-roller", "High Roller",
                "Adds +20 when your word begins with a rare tile — Q, X, Z or J.",
                ModifierKind.Additive,
                static ctx => ctx.Length > 0 && ctx.Word[0] is 'q' or 'x' or 'z' or 'j',
                static _ => 20) { Icon = "dice" },

            new ModifierCard(
                "perfect-link", "Perfect Link",
                "×1.5 when your word ends in a vowel — hand the next player an easy letter, pad your own score.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Length > 0 && "aeiou".Contains(ctx.Word[^1]),
                static _ => 1.5) { Icon = "link" },

            // ── Glass-cannon clock cards (feedback §5): big multipliers paid for in clock time ──
            new ModifierCard(
                "the-vault", "The Vault",
                "×1.5 on every word, but permanently shaves 3 seconds off your shot clock while equipped.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.5)
            { Icon = "vault", Clock = new ClockEffect(DeltaSeconds: -3) },

            new ModifierCard(
                "redline", "Redline",
                "×1.5 on every word, but permanently shortens your shot clock by 10% while equipped.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.5)
            { Icon = "redline", Clock = new ClockEffect(DeltaFraction: -0.10) },

            new ModifierCard(
                "panic-button", "Panic Button",
                "Halves your shot clock. ×1.35 normally — but ×2.7 if you submit in the final 2 seconds.",
                ModifierKind.Multiplicative,
                Always,
                static ctx => ctx.RemainingSeconds <= 2 ? 2.7 : 1.35)
            { Icon = "panic", Clock = new ClockEffect(DeltaFraction: -0.50) },

            // Hyper-Drive is inert in the pipeline (Trigger Never); its power is the era-scoped
            // latch (short clock + doubled multipliers) resolved in RoundState when you submit fast.
            new ModifierCard(
                "hyper-drive", "Hyper-Drive",
                "Submit in under 3 seconds to overdrive: your shot clock drops to 5s for the rest of the era, but every multiplier you own is doubled.",
                ModifierKind.Multiplicative,
                Never,
                static _ => 1.0)
            { Icon = "hyper-drive", Hyperdrive = new HyperdriveRule(ThresholdSeconds: 3, ClockOverrideSeconds: 5, MultiplierScale: 2.0) },

            // Tax Collector does NOT fold into its owner's own scoring pipeline (Trigger is
            // Never). Instead it is a passive bounty: when an *opponent* plays a banned-letter
            // word (Zero-Point Tax), each owner collects half of the points it would have scored.
            // That reactive payout is resolved in RoundState (see PaySiphons); here the card is inert.
            new ModifierCard(
                TaxCollectorId, "Tax Collector",
                "When an opponent plays a banned-letter word, collect half the points it would have scored.",
                ModifierKind.Multiplicative,
                Never,
                static _ => TaxCollectorRate)
            { Icon = "tax", Siphon = new SiphonRule(SiphonTrigger.OpponentEraTaxed, TaxCollectorRate) },

            // ── Tax / ban economy cards (feedback §2, §6) ──
            // Enforcer: a stronger Tax Collector. Collectors take the single highest matching rate
            // among their own cards, so Tax Collector + Enforcer pays 75% (not 125%).
            new ModifierCard(
                "enforcer", "Enforcer",
                "When an opponent plays a banned-letter word, collect 75% of the points it would have scored.",
                ModifierKind.Multiplicative,
                Never,
                static _ => EnforcerRate)
            { Icon = "enforcer", Siphon = new SiphonRule(SiphonTrigger.OpponentEraTaxed, EnforcerRate) },

            // IRS: turns your own Zero-Point Tax into a flat salvage (+15) and denies the bounty to
            // any Tax Collector / Enforcer watching. Inert in the pipeline (Trigger Never).
            new ModifierCard(
                "irs", "IRS",
                "When YOUR word is hit by the Zero-Point Tax, score a flat +15 instead of 0 — and no Tax Collector profits from it.",
                ModifierKind.Multiplicative,
                Never,
                static _ => 0)
            { Icon = "irs", OwnTax = new OwnTaxRule(FlatPoints: 15, SuppressesBounty: true) },

            // Roulette Wheel: each era it rolls you a personal banned letter (taxes you like the era
            // ban). The payoff is a flat ×1.75 on every clean word — taxed words already score 0, so
            // the multiplier only ever lands when you successfully dodge both bans.
            new ModifierCard(
                "roulette-wheel", "The Roulette Wheel",
                "Each era, rolls you a personal banned letter (Zero-Point Tax if you use it). Reward: ×1.75 on every word you keep clean.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.75)
            { Icon = "roulette", RollsPersonalBanAtEraStart = true },

            // Smuggler's Toll: each era it rolls you a personal banned letter (taxes you), but
            // establishes a toll — whenever an opponent's word uses that letter, you are minted 20%
            // of what they scored (they keep their points). Inert in the pipeline (Trigger Never).
            new ModifierCard(
                "smugglers-toll", "Smuggler's Toll",
                "Each era, rolls you a personal banned letter (Zero-Point Tax if you use it). Toll: bank 20% of any opponent's score when their word uses that letter.",
                ModifierKind.Multiplicative,
                Never,
                static _ => 0)
            { Icon = "smuggler", RollsPersonalBanAtEraStart = true, Siphon = new SiphonRule(SiphonTrigger.OpponentUsedMyCardBan, SmugglersTollRate) },

            // Bait & Switch: when your own word is taxed, the offending banned letter is forced onto
            // the next player as a personal ban. Inert in the pipeline (Trigger Never).
            new ModifierCard(
                "bait-and-switch", "Bait & Switch",
                "When your word is hit by the Zero-Point Tax, curse the next player with that exact banned letter for their next turn.",
                ModifierKind.Multiplicative,
                Never,
                static _ => 0)
            { Icon = "bait-switch", ForcesNextPlayerBan = true },
        ];

        /// <summary>Fast id → card lookup for resolving network ids against the catalogue.</summary>
        private static readonly ImmutableDictionary<string, ModifierCard> ById =
            All.ToImmutableDictionary(c => c.Id, StringComparer.Ordinal);

        /// <summary>Resolves a card by its stable id, or null when the id is unknown.</summary>
        public static ModifierCard? FindById(string id) =>
            ById.TryGetValue(id, out var card) ? card : null;
    }
}
