using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Base for all player-issued commands processed by the Drawn To Dress FSM.
    /// Every command carries the ID of the player who issued it so that states can
    /// validate permissions (host-only commands, active-player restrictions, etc.).
    /// </summary>
    public abstract record DrawnToDressCommand(string PlayerId);

    // ── Lobby ─────────────────────────────────────────────────────────────────

    /// <summary>Host starts the game, triggering the transition out of the lobby.</summary>
    public record StartGameCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Host updates the game configuration while still in the lobby.
    /// The supplied <paramref name="Config"/> is normalized and validated before being applied.
    /// </summary>
    public record UpdateConfigCommand(
        string PlayerId,
        DrawnToDressConfig Config) : DrawnToDressCommand(PlayerId);

    // ── Theme selection ───────────────────────────────────────────────────────

    /// <summary>
    /// Host explicitly picks the theme (used when <c>ThemeSource.HostPick</c> is configured).
    /// </summary>
    public record SelectThemeCommand(string PlayerId, string ThemeId) : DrawnToDressCommand(PlayerId);

    // ── Drawing round ─────────────────────────────────────────────────────────

    /// <summary>
    /// Player submits their completed SVG drawing for a specific clothing type.
    /// </summary>
    public record SubmitDrawingCommand(
        string PlayerId,
        string ClothingTypeId,
        string SvgContent) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player signals they are done with the current phase and ready to advance.
    /// Used in timed phases to allow early progression when all players are ready.
    /// </summary>
    public record MarkReadyCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    // ── Outfit building ───────────────────────────────────────────────────────

    /// <summary>Player claims a clothing item from the communal pool.</summary>
    public record ClaimPoolItemCommand(string PlayerId, Guid ItemId) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player submits their assembled outfit, selecting one item per clothing type.
    /// </summary>
    public record SubmitOutfitCommand(
        string PlayerId,
        Dictionary<string, Guid> SelectedItemsByType) : DrawnToDressCommand(PlayerId);

    // ── Outfit customization ──────────────────────────────────────────────────

    /// <summary>Player finalizes the custom name (and any other overlay data) for their outfit.</summary>
    public record SubmitCustomizationCommand(
        string PlayerId,
        string? OutfitName) : DrawnToDressCommand(PlayerId);

    // ── Outfit distinctness resolution ────────────────────────────────────────

    /// <summary>
    /// Player resolves a distinctness conflict by swapping the contested item for a
    /// different one.
    /// </summary>
    public record ResolveDistinctnessCommand(
        string PlayerId,
        Guid ReplacementItemId) : DrawnToDressCommand(PlayerId);

    // ── Voting ────────────────────────────────────────────────────────────────

    /// <summary>Player casts a vote for one outfit in a head-to-head matchup.</summary>
    public record CastVoteCommand(
        string PlayerId,
        Guid MatchupId,
        string CriterionId,
        string ChosenPlayerId) : DrawnToDressCommand(PlayerId);

    // ── Coin flip ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Requests a coin-flip tie-break for the specified tied matchup.
    /// Typically issued by the game engine when a voting round ends with a tie.
    /// </summary>
    public record RequestCoinFlipCommand(
        string PlayerId,
        Guid MatchupId) : DrawnToDressCommand(PlayerId);

    // ── Game control ──────────────────────────────────────────────────────────

    /// <summary>Host pauses the game, saving the current state for later resumption.</summary>
    public record PauseGameCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    /// <summary>Host resumes a previously paused game.</summary>
    public record ResumeGameCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    /// <summary>Host (or server) permanently ends the session.</summary>
    public record AbandonGameCommand(string PlayerId) : DrawnToDressCommand(PlayerId);
}
