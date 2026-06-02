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

        /// <summary>Fraction of an opponent's earned score a Toll Booth owner is minted when the
        /// opponent's word uses the owner's era-rolled personal banned letter.</summary>
        public const double TollBoothRate = 0.20;

        /// <summary>The final seconds of the shot clock that count as the "danger zone" — the window
        /// Panic Button and Adrenaline Spike must submit inside to earn their big multipliers.</summary>
        public const double DangerZoneSeconds = 2;

        /// <summary>Every modifier card, in catalogue order.</summary>
        public static readonly ImmutableArray<ModifierCard> All =
        [
            new ModifierCard(
                "anchor", "The Anchor",
                "Adds a flat +6 to your word, always.",
                ModifierKind.Additive,
                Always,
                static _ => 6) { Icon = "anchor" },

            new ModifierCard(
                "consonant-crunch", "Consonant Crunch",
                "Adds +2 for every consonant in your word.",
                ModifierKind.Additive,
                Always,
                static ctx => 2 * ctx.Word.Count(ctx.IsConsonant)) { Icon = "consonant" },

            new ModifierCard(
                "vowel-surge", "Vowel Surge",
                "×2 when your word has more vowels than consonants.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Word.Count(ctx.IsVowel) > ctx.Word.Count(ctx.IsConsonant),
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
                "When your word is 4 letters or shorter, ×(1 + 0.1 per second left on your clock).",
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
                "×3 when your word is 10 letters or longer. Clamped to the max word score.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Length >= 10,
                static _ => 3.0) { Icon = "tower" },

            new ModifierCard(
                "guttural-roar", "Guttural Roar",
                "×1.5 when your word's only vowels are 'A' or 'E'.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Word.Where(c => ctx.IsVowel(c)).All(c => c is 'a' or 'e'),
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
                static ctx => ctx.Length > 0 && ctx.IsVowel(ctx.Word[^1]),
                static _ => 1.5) { Icon = "link" },

            // ── Glass-cannon clock cards (feedback §5): big multipliers paid for in clock time ──
            new ModifierCard(
                "the-vault", "The Vault",
                "×1.5 on every word, but shortens your shot clock by 10% while equipped.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.5)
            { Icon = "vault", Clock = new ClockEffect(DeltaFraction: -0.10) },

            new ModifierCard(
                "redline", "Redline",
                "×2 on every word, but shortens your shot clock by 20% while equipped.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 2)
            { Icon = "redline", Clock = new ClockEffect(DeltaFraction: -0.20) },

            new ModifierCard(
                "panic-button", "Panic Button",
                "Halves your shot clock. ×1.35 normally — but ×2.7 if you submit before the final 2 seconds.",
                ModifierKind.Multiplicative,
                Always,
                static ctx => ctx.RemainingSeconds >= DangerZoneSeconds ? 2.7 : 1.35)
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

            // The IRS Agent: a 0-point utility slot. It can't salvage your own taxed word into points
            // any more, but while equipped no Tax Collector profits from your Zero-Point Tax. Inert
            // in the pipeline (Trigger Never); FlatPoints 0 = still a clean 0 on a tax.
            new ModifierCard(
                "irs", "The IRS Agent",
                "Grants 0 points. When YOUR word is hit by the Zero-Point Tax, no opponent's Tax Collector collects a thing.",
                ModifierKind.Multiplicative,
                Never,
                static _ => 0)
            { Icon = "irs", OwnTax = new OwnTaxRule(FlatPoints: 0, SuppressesBounty: true) },

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

            // The Toll Booth (Smuggler archetype, §2.2): each era it rolls you a personal banned
            // letter (taxes you), but establishes a toll — whenever an opponent's word uses that
            // letter, you are minted 20% of what they scored (they keep their points). Inert in the
            // pipeline (Trigger Never). Reworked from the old hand-played Toll Booth reaction.
            new ModifierCard(
                "toll-booth", "The Toll Booth",
                "Each era, rolls you a personal banned letter (Zero-Point Tax if you use it). Toll: bank 20% of any opponent's score when their word uses that letter.",
                ModifierKind.Multiplicative,
                Never,
                static _ => 0)
            { Icon = "smuggler", RollsPersonalBanAtEraStart = true, Siphon = new SiphonRule(SiphonTrigger.OpponentUsedMyCardBan, TollBoothRate) },

            // Bait & Switch: when your own word is taxed, the offending banned letter is forced onto
            // the next player as a personal ban. Inert in the pipeline (Trigger Never).
            new ModifierCard(
                "bait-and-switch", "Bait & Switch",
                "When your word is hit by the Zero-Point Tax, curse the next player with that exact banned letter for their next turn.",
                ModifierKind.Multiplicative,
                Never,
                static _ => 0)
            { Icon = "bait-switch", ForcesNextPlayerBan = true },

            // ── Glass Cannon & Chain Gambler (§2.1): big multipliers paid for in self-inflicted
            //    UI / timing / rules pain. Never disrupt an opponent — only the owner's own turn. ──

            // The Blindfold: hides your own input box while you type (a self-UI penalty). The reward
            // is a flat ×1.8 — pair with The Prism so a blind typo doesn't burn your whole turn.
            new ModifierCard(
                "blindfold", "The Blindfold",
                "Hides your own word-input box while you type — no peeking at typos. Reward: ×1.8 on every valid word.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.8)
            { Icon = "blindfold", HidesOwnInput = true },

            // Adrenaline Spike: shaves a flat 4s off your clock and only pays out (×2.5) if you submit
            // inside the danger zone (final 2s). Submitting early scores 0 — the ×0 zeroes the word.
            // Too much overlap with the existing glass cannon cards.
            //new ModifierCard(
            //    "adrenaline-spike", "Adrenaline Spike",
            //    "Shaves 4 seconds off your shot clock. ×2.5 — but ONLY if you submit in the final 2 seconds. Submit early and the word scores 0.",
            //    ModifierKind.Multiplicative,
            //    Always,
            //    static ctx => ctx.RemainingSeconds <= DangerZoneSeconds ? 2.5 : 0.0)
            //{ Icon = "adrenaline", Clock = new ClockEffect(DeltaSeconds: -4) },

            // The Double Down: rewards double letters (the 'ff' in coffin) with ×2, punishes their
            // absence with ×0.5. A swingy multiplier that rewards a specific word shape.
            new ModifierCard(
                "double-down", "The Double Down",
                "×2 when your word has a double letter (the 'ff' in coffin). No double letter? Your score is reduced (×0.75).",
                ModifierKind.Multiplicative,
                Always,
                static ctx => ctx.HasDoubleLetter ? 2.0 : 0.5)
            { Icon = "double-down" },

            // The Anchor Chain: pins your clock to a strict, unmodifiable 5s for the whole era (no
            // Heat Sink rescue). In exchange, a colossal +0.5× per letter of the played word.
            new ModifierCard(
                "anchor-chain", "The Anchor Chain",
                "Locks your shot clock to a strict, unmodifiable 5 seconds for the era. In exchange: ×(0.5 per letter) of your word",
                ModifierKind.Multiplicative,
                Always,
                static ctx => 0.5 * ctx.Length)
            { Icon = "anchor-chain", ClockOverride = new ClockOverride(Seconds: 5) },

            // ── Automated Aggro (§2.3): hands-free PvP. No targeting, no opponent-UI disruption —
            //    everything fires from leaderboard / linguistic rules. ──

            // Flak Cannon: a left-side additive (+5) that, at the end of your turn, pings ~2s off the
            // next clock of every player currently ahead of you on cumulative score.
            new ModifierCard(
                "flak-cannon", "Flak Cannon",
                "Grants 0 points. Takes 2 seconds off the next shot clock of every player scoring higher than you.",
                ModifierKind.Additive,
                Always,
                static _ => 0)
            { Icon = "flak-cannon", AutoTimeShave = new AutoTimeShaveRule(Seconds: 2, Target: AutoTimeShaveTarget.HigherScore) },

            // Scattershot: a minor ×1.15 that, on every submission, fires a 3s time-shave at any
            // opponent who has played a double-letter word this era.
            // Target is too niche and easy to avoid.
            //new ModifierCard(
            //    "scattershot", "Scattershot",
            //    "×1.15 on your words. On every submission, shaves 3 seconds off the next clock of any opponent who has played a double-letter word this era.",
            //    ModifierKind.Multiplicative,
            //    Always,
            //    static _ => 1.15)
            //{ Icon = "scattershot", AutoTimeShave = new AutoTimeShaveRule(Seconds: 3, Target: AutoTimeShaveTarget.PlayedDoubleLetterThisEra) },

            // The Bounty Hunter: 0 points (×1.0). Marks the round's leader; if that leader plays a
            // word shorter than 6 letters on their turn, they are docked a flat 30 points.
            new ModifierCard(
                "bounty-hunter", "The Bounty Hunter",
                "Grants 0 points. Marks the leader each round — if they play a word shorter than 6 letters, they lose 15 points.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.0)
            { Icon = "bounty-hunter", LeaderPenalty = new LeaderPenaltyRule(MinLength: 6, Penalty: 15) },

            // Tracer Round: 0 points (×1.0). At the end of your turn, the letter your word ends on is
            // forced onto the next player as a one-turn personal banned letter.
            // Completely broken whith shintori rules.
            //new ModifierCard(
            //    "tracer-round", "Tracer Round",
            //    "Grants 0 points. At the end of your turn, the letter your word ends on becomes a personal banned letter for the next player's turn.",
            //    ModifierKind.Multiplicative,
            //    Always,
            //    static _ => 1.0)
            //{ Icon = "tracer-round", HijacksEndLetter = true },

            // ── Shield (§2.4): The Titanium Mirror. Starts as a passive ×1.0 placeholder; auto-blocks
            //    and reflects incoming automated attacks, decaying −0.1× per block into a burden. ──
            new ModifierCard(
                "titanium-mirror", "The Titanium Mirror",
                $"Passive ×1.0. Automatically blocks and reflects incoming attacks (time shaves, point drains, letter hijacks) back at their source — but loses 0.1× per block, carrying its decay across eras until discarded.",
                ModifierKind.Multiplicative,
                Always,
                static ctx => ctx.ShieldMultiplier)
            { Icon = "titanium-mirror", Shield = new ShieldRule(Start: 1.0, DecayPerBlock: 0.1) },

            // ── Utility (§2.5): 0-point lifesavers that pair with the high-risk cards above. ──

            // The Heat Sink: 0 points (×1.0), +5s shot clock — neutralises Redline / Adrenaline Spike
            // (but NOT The Anchor Chain, whose clock override is unmodifiable).
            new ModifierCard(
                "heat-sink", "The Heat Sink",
                "Grants 0 points but adds a flat +5 seconds to your shot clock.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.0)
            { Icon = "heat-sink", Clock = new ClockEffect(DeltaSeconds: +5) },

            // The Faraday Cage: 0 points (×1.0). Immune to your OWN era-rolled personal card-bans, so
            // Roulette Wheel / The Toll Booth boost you without limiting your vocabulary.
            // Simply negates the effects of your choices. Boring.
            //new ModifierCard(
            //    "faraday-cage", "The Faraday Cage",
            //    "Grants 0 points. You are immune to personal banned letters generated by your own cards (Roulette Wheel, The Toll Booth) — keep the boosts, lose the vocabulary tax.",
            //    ModifierKind.Multiplicative,
            //    Always,
            //    static _ => 1.0)
            //{ Icon = "faraday-cage", ImmuneToOwnCardBans = true },

            // The Prism: 0 points (×1.0). A failed/typo submission refills your clock to full once per
            // turn instead of running it down — the essential pairing for The Blindfold.
            new ModifierCard(
                "prism", "The Prism",
                "Grants 0 points. If your word is a typo or fails validation, your shot clock resets to full — once per turn — instead of ticking away.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.0)
            { Icon = "prism", RefillsClockOnFailedValidation = true },

            // The Wildcard: 0 points (×1.0). Ignore the Succession rule — your word need not start
            // with the previous word's last letter.
            new ModifierCard(
                "wildcard", "The Wildcard",
                "Grants 0 points. Your words ignore the Succession rule — they need not begin with the last letter of the previous word.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.0)
            { Icon = "wildcard", IgnoresSuccessionRule = true },

            // The Catalyst: 0 points (×1.0). Y, W and H count as BOTH vowel and consonant when
            // evaluating every other card's trigger (forces tricky conditionals like Vowel Surge).
            new ModifierCard(
                "catalyst", "The Catalyst",
                "Grants 0 points. For every other card in your bay, the letters Y, W and H count as both a vowel AND a consonant.",
                ModifierKind.Multiplicative,
                Always,
                static _ => 1.0)
            { Icon = "catalyst", CatalystAmbiguousLetters = true },
        ];

        /// <summary>Fast id → card lookup for resolving network ids against the catalogue.</summary>
        private static readonly ImmutableDictionary<string, ModifierCard> ById =
            All.ToImmutableDictionary(c => c.Id, StringComparer.Ordinal);

        /// <summary>Resolves a card by its stable id, or null when the id is unknown.</summary>
        public static ModifierCard? FindById(string id) =>
            ById.TryGetValue(id, out var card) ? card : null;
    }
}
