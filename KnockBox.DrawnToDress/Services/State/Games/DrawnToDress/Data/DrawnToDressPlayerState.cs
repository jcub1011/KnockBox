namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>
    /// Mutable per-player state for a Drawn To Dress game.
    /// </summary>
    public class DrawnToDressPlayerState
    {
        /// <summary>The player's unique identifier.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>Human-readable display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// <see langword="true"/> when the player has signalled they are ready to advance
        /// to the next phase (e.g. finished drawing ahead of the timer).
        /// </summary>
        public bool IsReady { get; set; }

        /// <summary>
        /// IDs of <see cref="DrawnClothingItem"/>s that this player currently owns
        /// (either drawn by them or claimed from the pool).
        /// </summary>
        public List<Guid> OwnedClothingItemIds { get; set; } = [];

        /// <summary>
        /// The outfit this player has submitted, or <see langword="null"/> if they have not
        /// yet submitted one.
        /// </summary>
        public OutfitSubmission? SubmittedOutfit { get; set; }

        /// <summary>
        /// Bonus points earned by this player through achievements during the game
        /// (e.g. submitting an outfit before the deadline).
        /// </summary>
        public int BonusPoints { get; set; }
    }
}
