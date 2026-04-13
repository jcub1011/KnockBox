namespace KnockBox.DrawnToDress.Services.State.Games.Data
{
    /// <summary>
    /// Stores an absolute viewBox-coordinate position override for a single clothing item.
    /// Used when the player repositions items via drag or manual X/Y input during customization.
    /// </summary>
    public class ItemPositionOverride
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Overlay data applied on top of an outfit's selected items, such as a custom name
    /// chosen by the player. Kept separate from item selection so that UI overlays do not
    /// contaminate core outfit data.
    /// </summary>
    public class OutfitCustomization
    {
        /// <summary>Player-chosen name for the outfit, or <see langword="null"/> if not set.</summary>
        public string? OutfitName { get; set; }

        /// <summary>
        /// Optional SVG sketch drawn over the assembled outfit during the customization
        /// phase, or <see langword="null"/> if the player did not add a sketch.
        /// When <see cref="DrawnToDressConfig.SketchingRequired"/> is enabled this must
        /// be non-null for the submission to be accepted.
        /// </summary>
        public string? SketchSvgContent { get; set; }

        /// <summary>
        /// Per-item position overrides keyed by clothing type ID. Stores absolute viewBox
        /// coordinates. An empty dictionary means the default stacked layout is used.
        /// </summary>
        public Dictionary<string, ItemPositionOverride> ItemPositionOverrides { get; set; } = new();
    }

    /// <summary>
    /// Represents an outfit that a player has submitted during the Outfit Building phase.
    /// Selected clothing items are stored separately from customization to keep those
    /// concerns decoupled.
    /// </summary>
    public class OutfitSubmission
    {
        /// <summary>The player ID of the player who submitted this outfit.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>
        /// The selected clothing items keyed by clothing type ID. Each value is the
        /// <see cref="DrawnClothingItem.Id"/> of the chosen item for that type.
        /// </summary>
        public Dictionary<string, Guid> SelectedItemsByType { get; set; } = [];

        /// <summary>Player-supplied overlay data (name, etc.) for this outfit.</summary>
        public OutfitCustomization Customization { get; set; } = new();

        /// <summary>UTC timestamp when the outfit was submitted.</summary>
        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
