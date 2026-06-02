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

        /// <summary>The action cards currently in the player's hand.</summary>
        public List<ActionCard> ActionHand { get; } = new();

        /// <summary>
        /// Ids of the modifier cards dealt to this player in the current Intermission, so the
        /// Optimization panel can flag them NEW and pop them in (the deal reveal now lives in
        /// Optimization instead of a dedicated sub-phase). Repopulated each deal, cleared when
        /// the Intermission completes.
        /// </summary>
        public HashSet<string> NewlyDealtModifierIds { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The action cards dealt to this player in the current Intermission, shown as a reveal
        /// strip in the Optimization panel. Repopulated each deal, cleared when the Intermission
        /// completes. A list (not a set) because action hands may hold duplicates.
        /// </summary>
        public List<ActionCard> NewlyDealtActions { get; } = new();

        /// <summary>
        /// A Pivot/Amnesty queued for the player's next submission, or null when none is
        /// pending. Consumed by the next accepted submission (see <c>RoundState</c>).
        /// </summary>
        public ActionKind? PendingAction { get; set; } = null;

        /// <summary>
        /// Shot-clock seconds owed to this player from Time Thief plays made while they were
        /// not the active player. Applied (and cleared) the next time they take a turn.
        /// </summary>
        public int QueuedTimePenaltySeconds { get; set; } = 0;
    }
}
