namespace KnockBox.DrawnToDress.Services.State.Games.Data
{
    /// <summary>Determines where the round theme originates.</summary>
    public enum ThemeSource
    {
        /// <summary>A theme is chosen at random from a pre-defined list.</summary>
        Random,

        /// <summary>The host selects the theme before each drawing phase.</summary>
        HostPick,

        /// <summary>
        /// Each player writes a theme; one of the submitted themes is selected at random
        /// and used for the session after all players have submitted.
        /// </summary>
        PlayerWritten,

        /// <summary>
        /// A random subset of candidate themes is presented to all players, who vote on
        /// which theme to use. The candidate with the most votes wins.
        /// </summary>
        RandomVoting,
    }

    /// <summary>Controls when the selected theme is revealed to players.</summary>
    public enum ThemeAnnouncement
    {
        /// <summary>
        /// The theme is revealed before the drawing phase begins.
        /// Players know the theme while they draw.
        /// </summary>
        BeforeDrawing,

        /// <summary>
        /// The theme is selected and persisted internally but kept hidden from players
        /// until after the drawing phase completes.  Players draw without knowing the theme.
        /// </summary>
        AfterDrawing,
    }

    /// <summary>
    /// Describes a theme that is announced to players at the start of the drawing phase.
    /// Themes guide what outfits the players should draw and construct.
    /// </summary>
    public record ThemeDefinition(
        string Id,
        string DisplayName,
        string? Description = null);
}
