namespace KnockBox.DrawnToDress.Services.State.Games.Data
{
    /// <summary>
    /// Represents a single clothing item that a player has drawn.
    /// Preserves the clothing type, creator identity, drawing asset, and
    /// pool/claim usage metadata as separate concerns on one model.
    /// </summary>
    public class DrawnClothingItem
    {
        /// <summary>Unique identifier for this clothing item.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The clothing type this item belongs to (matches
        /// <see cref="ClothingTypeDefinition.Id"/>).
        /// </summary>
        public ClothingType ClothingTypeId { get; set; }

        /// <summary>The player ID of the player who drew this item.</summary>
        public string CreatorPlayerId { get; set; } = string.Empty;

        /// <summary>
        /// The serialized SVG content of the drawing, or <see langword="null"/> if the
        /// item has not yet been submitted.
        /// </summary>
        public string? SvgContent { get; set; }

        // ── Pool / claim metadata ─────────────────────────────────────────────

        /// <summary>
        /// <see langword="true"/> when this item has been placed into the communal pool and
        /// is available for other players to claim.
        /// </summary>
        public bool IsInPool { get; set; }

        /// <summary>
        /// The player ID of the player who has claimed this item from the pool, or
        /// <see langword="null"/> when the item is unclaimed.
        /// </summary>
        public string? ClaimedByPlayerId { get; set; }
    }
}
