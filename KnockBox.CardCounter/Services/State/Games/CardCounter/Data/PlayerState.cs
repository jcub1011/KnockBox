namespace KnockBox.Services.State.Games.CardCounter.Data
{
    /// <summary>
    /// Mutable per-player state for a Card Counter game.
    /// </summary>
    public class PlayerState
    {
        /// <summary>Unique player identifier.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>Human-readable display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Current balance (can be negative).</summary>
        public double Balance { get; set; }

        /// <summary>
        /// Ordered digit list that forms the pot through concatenation.
        /// Leading zeros are preserved but ignored when computing <see cref="PotValue"/>.
        /// </summary>
        public List<int> Pot { get; set; } = [];

        /// <summary>
        /// Concatenated numeric value of <see cref="Pot"/>, ignoring leading zeros.
        /// Returns 0 when the pot is empty or when the concatenated value overflows
        /// <see cref="double.MaxValue"/> (too many digits).
        /// </summary>
        public double PotValue
        {
            get
            {
                if (Pot.Count == 0) return 0;
                string concatenated = string.Join("", Pot);
                return double.TryParse(concatenated, out double val) ? val : 0;
            }
        }

        /// <summary>Number of passes this player has remaining for the whole game.</summary>
        public int PassesRemaining { get; set; }

        /// <summary>
        /// Number of additional turns this player has queued up (from Let It Ride cards).
        /// After their current turn ends, they will take this many extra turns before play
        /// advances to the next player. Stacks: each Let It Ride card adds 1.
        /// </summary>
        public int ExtraTurns { get; set; }

        /// <summary>True once the player has chosen positive or negative for their buy-in balance.</summary>
        public bool HasSetBuyIn { get; set; }

        /// <summary>Server-generated die roll (1–6) used to compute the initial balance.</summary>
        public int BuyInRoll { get; set; }

        /// <summary>Action cards currently held in the player's hidden hand.</summary>
        public List<ActionCard> ActionHand { get; set; } = [];

        /// <summary>
        /// Top-3 shoe cards revealed to this player by Make My Luck.
        /// Non-null only while the player is choosing a reorder.
        /// </summary>
        public List<BaseCard>? PrivateReveal { get; set; }

        /// <summary>
        /// In Active Operator Mode, the operator the player currently uses to apply number cards
        /// to their balance. Starts as <see cref="Operator.Add"/> and is replaced when the player
        /// draws an operator card. Null when Active Operator Mode is not in effect.
        /// </summary>
        public Operator? ActiveOperator { get; set; }
    }
}
