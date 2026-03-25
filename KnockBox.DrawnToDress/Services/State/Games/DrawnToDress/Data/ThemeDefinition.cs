namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>Determines where the round theme originates.</summary>
    public enum ThemeSource
    {
        /// <summary>A theme is chosen at random from a pre-defined list.</summary>
        Random,

        /// <summary>The host selects the theme before each drawing phase.</summary>
        HostPick,
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
