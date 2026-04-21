namespace KnockBox.DrawnToDress.Services.State.Games.Data
{
    /// <summary>
    /// Defines a category of clothing that players can draw (e.g. Hat, Top, Shoes).
    /// These definitions are part of <see cref="DrawnToDressConfig"/> and drive which
    /// drawing slots are available and how outfits are assembled.
    /// </summary>
    public record ClothingTypeDefinition
    {
        /// <summary>Identifies which clothing category this definition represents.</summary>
        public ClothingType Id { get; set; }

        /// <summary>Human-readable name shown in the UI (e.g. "Hat", "Top").</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// When <see langword="true"/>, an outfit may include more than one item of this
        /// type (e.g. multiple accessories). When <see langword="false"/>, at most one item
        /// of this type is allowed per outfit.
        /// </summary>
        public bool AllowMultiple { get; set; }

        /// <summary>
        /// Maximum number of drawings a player may submit for this clothing type during its
        /// drawing round.  A value of <c>0</c> means no limit.
        /// GDD default: 3.
        /// </summary>
        public int MaxItemsPerRound { get; set; } = 3;

        /// <summary>Pixel width of the drawing canvas for this clothing type.</summary>
        public int CanvasWidth { get; set; } = 600;

        /// <summary>Pixel height of the drawing canvas for this clothing type.</summary>
        public int CanvasHeight { get; set; } = 450;

        /// <summary>
        /// The Y-coordinate center of this body part on the mannequin overlay (in scaled 2x coordinates).
        /// Used by the drawing phase to position the mannequin reference for each clothing type.
        /// </summary>
        public int MannequinAnchorY { get; set; }

        /// <summary>
        /// Optional path to a version of the mannequin reference image that focuses on this specific body part.
        /// </summary>
        public string? MannequinFocusImagePath { get; set; }
    }
}
