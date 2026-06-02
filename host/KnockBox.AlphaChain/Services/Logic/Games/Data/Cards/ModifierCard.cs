namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// A modifier card that lives in a player's Engine Bay and folds into the scoring
    /// pipeline. Cards are identified across the network by their stable <see cref="Id"/>
    /// — never by index. The <see cref="Trigger"/> and <see cref="Value"/> delegates are
    /// <b>not</b> serialisable; they live on the singleton <see cref="ModifierLibrary"/>
    /// and are referenced by id, so per-player state stores the immutable record
    /// reference (resolved against the library) rather than the lambdas themselves.
    /// </summary>
    /// <param name="Id">Stable identifier used to reference the card across the network.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Description">Human-readable rules text for the tooltip.</param>
    /// <param name="Kind">Whether the card adds to or multiplies the running score.</param>
    /// <param name="Trigger">
    /// Returns true when the card contributes for the given word. Unconditional cards
    /// return true for every word.
    /// </param>
    /// <param name="Value">
    /// The additive bonus or multiplicative factor for the given word. Evaluated only when
    /// <see cref="Trigger"/> returns true.
    /// </param>
    public sealed record ModifierCard(
        string Id,
        string Name,
        string Description,
        ModifierKind Kind,
        Func<WordContext, bool> Trigger,
        Func<WordContext, double> Value)
    {
        /// <summary>
        /// Stable icon key resolved by <c>CardIcon</c> to an inline SVG glyph, so players
        /// associate a distinct symbol with each card's function. Set via object initializer
        /// in <see cref="ModifierLibrary"/>; defaults to empty for ad-hoc/test cards.
        /// </summary>
        public string Icon { get; init; } = string.Empty;

        // ── Optional capability descriptors (see CardCapabilities.cs) ────────────
        // All null/false by default: a plain scoring card declares none, and the engine's
        // lifecycle hooks no-op for it. Cards opt in to richer behavior by setting one or more.

        /// <summary>Permanent per-owner shot-clock change applied while this card is in the active
        /// player's bay (Vault, Redline, Panic Button). Null = no clock effect.</summary>
        public ClockEffect? Clock { get; init; }

        /// <summary>Reactive payout from opponents' submissions (Tax Collector, The Toll Booth).
        /// Null = none. A siphon card's <see cref="Trigger"/> is typically <c>Never</c> so it never
        /// folds into its owner's own pipeline.</summary>
        public SiphonRule? Siphon { get; init; }

        /// <summary>When true, the owner rolls a fresh random personal banned letter at each era
        /// start (Roulette Wheel, Smuggler's Toll). That letter triggers the Zero-Point Tax for the
        /// owner like the era ban.</summary>
        public bool RollsPersonalBanAtEraStart { get; init; }

        /// <summary>Overrides the owner's own Zero-Point Tax outcome (IRS). Null = standard tax (0).</summary>
        public OwnTaxRule? OwnTax { get; init; }

        /// <summary>When true, a taxed word forces the next player's personal banned letter to the
        /// banned letter just used (Bait &amp; Switch).</summary>
        public bool ForcesNextPlayerBan { get; init; }

        /// <summary>Latches an era-scoped clock/multiplier boost when the owner submits fast
        /// (Hyper-Drive). Null = none.</summary>
        public HyperdriveRule? Hyperdrive { get; init; }

        /// <summary>When true, this card masks the previous word's first &amp; last letters in its
        /// owner's UI only (Tunnel Vision). Presentational; the chain rule is still server-enforced.</summary>
        public bool MasksPreviousWord { get; init; }

        /// <summary>Pins the owner's shot clock to a fixed, unmodifiable length for the era
        /// (The Anchor Chain). Null = the clock follows the base + <see cref="Clock"/> effects.</summary>
        public ClockOverride? ClockOverride { get; init; }

        /// <summary>Hands-free time-shave fired at matching opponents at the end of the owner's turn
        /// (Flak Cannon, Scattershot). Null = none.</summary>
        public AutoTimeShaveRule? AutoTimeShave { get; init; }

        /// <summary>Drains a flat penalty from the round's marked leader on a too-short word
        /// (The Bounty Hunter). Null = none.</summary>
        public LeaderPenaltyRule? LeaderPenalty { get; init; }

        /// <summary>Marks this card as the Titanium Mirror: blocks &amp; reflects incoming automated
        /// attacks, decaying its scoring multiplier per block. Null = not a shield.</summary>
        public ShieldRule? Shield { get; init; }

        /// <summary>When true, hides the owner's own word-input text while typing (The Blindfold) —
        /// a self-inflicted UI penalty traded for a multiplier. Presentational; input still works.</summary>
        public bool HidesOwnInput { get; init; }

        /// <summary>When true, the owner's word-ending letter is forced onto the next player as a
        /// personal banned letter at the end of the owner's turn (Tracer Round).</summary>
        public bool HijacksEndLetter { get; init; }

        /// <summary>When true, the owner is immune to their own era-rolled personal card-bans
        /// (The Faraday Cage) — Roulette Wheel / Toll Booth self-boosts without the vocabulary tax.</summary>
        public bool ImmuneToOwnCardBans { get; init; }

        /// <summary>When true, a failed dictionary/typo submission refills the owner's shot clock to
        /// full once per turn instead of running it down (The Prism). Pairs with The Blindfold.</summary>
        public bool RefillsClockOnFailedValidation { get; init; }

        /// <summary>When true, the owner may ignore the Succession (chain) rule — their word need not
        /// begin with the previous word's last letter (The Wildcard).</summary>
        public bool IgnoresSuccessionRule { get; init; }

        /// <summary>When true, the letters Y, W and H count as <i>both</i> vowel and consonant when
        /// evaluating every other card's trigger (The Catalyst).</summary>
        public bool CatalystAmbiguousLetters { get; init; }
    }
}
