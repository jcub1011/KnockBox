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

        /// <summary>Whether the player has disconnected/left. Their turns are skipped.</summary>
        public bool HasLeft { get; set; } = false;

        // Reserved for M3: modifier-card and action-card hand collections are added here.
        // Kept off the public surface for now so M1 stays minimal.
    }
}
