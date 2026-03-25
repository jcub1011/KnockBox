namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>
    /// Defines a category of clothing that players can draw (e.g. Hat, Top, Shoes).
    /// These definitions are part of <see cref="DrawnToDressConfig"/> and drive which
    /// drawing slots are available and how outfits are assembled.
    /// </summary>
    public class ClothingTypeDefinition
    {
        /// <summary>Unique identifier for this clothing type (e.g. "hat", "top").</summary>
        public string Id { get; set; } = string.Empty;

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
    }
}
