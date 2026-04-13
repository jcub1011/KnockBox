namespace KnockBox.ConsultTheCard.Services.State.Games.Data
{
    /// <summary>
    /// Mutable per-player state for a Consult the Card game.
    /// </summary>
    public class ConsultTheCardPlayerState
    {
        /// <summary>Unique player identifier.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>Human-readable display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>The role assigned to this player for the current game.</summary>
        public Role Role { get; set; }

        /// <summary>
        /// The secret word assigned to this player. Null for the Informant role.
        /// </summary>
        public string? SecretWord { get; set; }

        /// <summary>Whether this player has been eliminated in the current game.</summary>
        public bool IsEliminated { get; set; }

        /// <summary>
        /// The text currently typed in the clue input, used for auto-submit on timeout.
        /// Updated by the UI on every keystroke so the server can submit it if the timer expires.
        /// </summary>
        public string? PendingClue { get; set; }

        /// <summary>Whether this player has submitted a clue for the current round.</summary>
        public bool HasSubmittedClue { get; set; }

        /// <summary>The clue submitted by this player for the current round.</summary>
        public string? CurrentClue { get; set; }

        /// <summary>A complete history of all clues submitted by this player in the current game.</summary>
        public List<string> ClueHistory { get; set; } = [];

        /// <summary>The player ID this player has voted to eliminate.</summary>
        public string? VoteTargetId { get; set; }

        /// <summary>Whether this player has cast their vote for the current round.</summary>
        public bool HasVoted { get; set; }

        /// <summary>
        /// Whether the player has voted to end the game early.
        /// One vote per elimination cycle per player.
        /// </summary>
        public bool HasVotedToEndGame { get; set; }

        /// <summary>
        /// Whether the player has voted to skip the remaining discussion time.
        /// One vote per elimination cycle per player.
        /// </summary>
        public bool HasVotedToSkipTime { get; set; }

        /// <summary>The player's score for the current game.</summary>
        public int Score { get; set; }
    }
}
