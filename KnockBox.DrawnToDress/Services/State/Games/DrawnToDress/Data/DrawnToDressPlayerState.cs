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
        /// All outfits submitted by this player, keyed by 1-based outfit round number.
        /// </summary>
        public Dictionary<int, OutfitSubmission> SubmittedOutfits { get; set; } = new();

        /// <summary>Returns the outfit for the given round, or null.</summary>
        public OutfitSubmission? GetOutfit(int outfitRound) => SubmittedOutfits.GetValueOrDefault(outfitRound);

        /// <summary>Sets the outfit for the given round.</summary>
        public void SetOutfit(int outfitRound, OutfitSubmission submission) => SubmittedOutfits[outfitRound] = submission;

        /// <summary>
        /// The outfit this player has submitted during Outfit 1 Building, or
        /// <see langword="null"/> if they have not yet submitted one.
        /// Convenience property delegating to <see cref="SubmittedOutfits"/>.
        /// </summary>
        public OutfitSubmission? SubmittedOutfit
        {
            get => GetOutfit(1);
            set { if (value is not null) SetOutfit(1, value); else SubmittedOutfits.Remove(1); }
        }

        /// <summary>
        /// The outfit this player has submitted during Outfit 2 Building, or
        /// <see langword="null"/> if they have not yet submitted one.
        /// Convenience property delegating to <see cref="SubmittedOutfits"/>.
        /// </summary>
        public OutfitSubmission? SubmittedOutfit2
        {
            get => GetOutfit(2);
            set { if (value is not null) SetOutfit(2, value); else SubmittedOutfits.Remove(2); }
        }

        /// <summary>
        /// The name the player has typed for their outfit, but not yet finalized.
        /// Used to recover the name if the timer expires before manual submission.
        /// </summary>
        public string? DraftOutfitName { get; set; }

        /// <summary>
        /// Bonus points earned by this player through achievements during the game
        /// (e.g. submitting an outfit before the deadline).
        /// </summary>
        public int BonusPoints { get; set; }
    }
}
