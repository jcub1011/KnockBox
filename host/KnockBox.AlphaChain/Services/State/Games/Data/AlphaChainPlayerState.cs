using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;

namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// Mutable per-player state for an Alpha Chain game. Intentionally a plain class
    /// (not a record) because it grows in later milestones — keep the public surface
    /// minimal and document forward-looking intent inline.
    /// </summary>
    public class AlphaChainPlayerState
    {
        /// <summary>The player's authoritative <c>User.Id</c>.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Per-lobby display name (may differ from <c>User.Name</c> after disambiguation).</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>The player's score. Scoring rules land in M2.</summary>
        public int Score { get; set; } = 0;

        /// <summary>Whether the player has been eliminated. Elimination rules land in M2 (Survival Mode).</summary>
        public bool IsEliminated { get; set; } = false;

        /// <summary>
        /// 1-based order in which this player was eliminated (1 = first out), or null while still
        /// in play. Assigned by <c>AlphaChainGameState.MarkEliminated</c> and used by
        /// <c>GameOverState</c> to rank eliminated players: surviving longer (a higher order)
        /// ranks above being knocked out early.
        /// </summary>
        public int? EliminationOrder { get; set; } = null;

        /// <summary>Whether the player has disconnected/left. Their turns are skipped.</summary>
        public bool HasLeft { get; set; } = false;

        /// <summary>
        /// How many times this player ran out the shot clock. Tracked in non-survival
        /// mode (in survival mode a timeout eliminates instead). Surfaced for stats/UI.
        /// </summary>
        public int TurnTimeouts { get; set; } = 0;

        // ── Cards (M3) ──────────────────────────────────────────────────────

        /// <summary>
        /// How many modifier cards this player's Engine Bay can hold. Starts at 3; the
        /// Intermission Expansion grows it in M4.
        /// </summary>
        public int ModifierSlots { get; set; } = 3;

        /// <summary>
        /// The player's Engine Bay: an ordered list of modifier cards (left → right is the
        /// scoring pipeline order). Bounded by <see cref="ModifierSlots"/>.
        /// </summary>
        public List<ModifierCard> EngineBay { get; } = new();

        /// <summary>
        /// Ids of the modifier cards dealt to this player in the current Intermission, so the
        /// Optimization panel can flag them NEW and pop them in (the deal reveal now lives in
        /// Optimization instead of a dedicated sub-phase). Repopulated each deal, cleared when
        /// the Intermission completes.
        /// </summary>
        public HashSet<string> NewlyDealtModifierIds { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// A transient personal banned letter (lower-case) forced onto this player by an opponent's
        /// automated letter-hijack modifier (Tracer Round's end-letter, Bait &amp; Switch's taxed
        /// letter), or null when none is active. Like the match's banned letter it triggers the
        /// Zero-Point Tax, but it affects only this player and is consumed by their next accepted submission.
        /// </summary>
        public char? PersonalBannedLetter { get; set; } = null;

        /// <summary>
        /// Shot-clock seconds owed to this player from automated time-shave modifiers (Flak Cannon,
        /// Scattershot) that fired while they were not the active player. Applied (and cleared) the
        /// next time they take a turn — see <c>RoundState.ApplyQueuedTimePenalty</c>.
        /// </summary>
        public int QueuedTimePenaltySeconds { get; set; } = 0;

        // ── Card-capability state (feedback cards) ──────────────────────────────

        /// <summary>
        /// Personal banned letters (lower-case) rolled at era start by this player's
        /// <c>RollsPersonalBanAtEraStart</c> modifier cards (Roulette Wheel, Smuggler's Toll),
        /// keyed by the card id that rolled them. Like the match's banned letter they trigger the
        /// Zero-Point Tax for this player; re-rolled each era. Separate from
        /// <see cref="PersonalBannedLetter"/> (the transient Jinx/Bait &amp; Switch ban).
        /// </summary>
        public Dictionary<string, char> CardBannedLetters { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// True once Hyper-Drive has latched this era: the owner's shot clock is overridden short
        /// and every multiplicative card is scaled up for the rest of the era. Reset at each era
        /// boundary.
        /// </summary>
        public bool HyperDriveActive { get; set; } = false;

        /// <summary>
        /// The owner's live Titanium Mirror multiplier. Starts at 1.0 (a passive ×1.0 placeholder)
        /// and drops by the shield's decay per attack it blocks and reflects this era, possibly
        /// below 1.0 into a scoring burden. Feeds the Titanium Mirror card's scoring factor via
        /// <c>WordContext.ShieldMultiplier</c>. Reset to 1.0 at each era boundary.
        /// </summary>
        public double ShieldMultiplier { get; set; } = 1.0;

        /// <summary>
        /// True once this player has played a word containing a double letter this era — the target
        /// test for an opponent's Scattershot. Reset at each era boundary.
        /// </summary>
        public bool PlayedDoubleLetterWordThisEra { get; set; } = false;

        /// <summary>
        /// True once this player's The Prism has already refilled the clock on a failed submission
        /// this turn (the once-per-turn cap). Reset whenever a fresh turn arms for this player.
        /// </summary>
        public bool PrismUsedThisTurn { get; set; } = false;
    }
}
