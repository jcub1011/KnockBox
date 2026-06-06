using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM
{
    /// <summary>
    /// Base for all player-issued commands processed by the Drawn To Dress FSM.
    /// Every command carries the ID of the player who issued it so that states can
    /// validate permissions (host-only commands, active-player restrictions, etc.).
    /// </summary>
    public abstract record DrawnToDressCommand(Guid PlayerId);

    // ── Lobby ─────────────────────────────────────────────────────────────────

    /// <summary>Host starts the game, triggering the transition out of the lobby.</summary>
    public record StartGameCommand(Guid PlayerId) : DrawnToDressCommand(PlayerId);

    // ── Theme selection ───────────────────────────────────────────────────────

    /// <summary>
    /// Host explicitly picks the theme (used when <c>ThemeSource.HostPick</c> is configured).
    /// </summary>
    public record SelectThemeCommand(Guid PlayerId, string ThemeId) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player submits a theme text during the theme-selection phase when
    /// <c>ThemeSource.PlayerWritten</c> is configured.
    /// </summary>
    public record SubmitPlayerThemeCommand(
        Guid PlayerId,
        string ThemeText) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player votes for one of the candidate themes during the theme-selection phase when
    /// <c>ThemeSource.RandomVoting</c> is configured.
    /// </summary>
    public record VoteForThemeCommand(
        Guid PlayerId,
        string ThemeId) : DrawnToDressCommand(PlayerId);

    // ── Drawing round ─────────────────────────────────────────────────────────

    /// <summary>
    /// Player submits their completed SVG drawing for a specific clothing type.
    /// </summary>
    public record SubmitDrawingCommand(
        Guid PlayerId,
        ClothingType ClothingTypeId,
        string SvgContent) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player signals they are done with the current phase and ready to advance.
    /// Used in timed phases to allow early progression when all players are ready.
    /// </summary>
    public record MarkReadyCommand(Guid PlayerId) : DrawnToDressCommand(PlayerId);

    // ── Outfit building ───────────────────────────────────────────────────────

    /// <summary>Player claims a clothing item from the communal pool.</summary>
    public record ClaimPoolItemCommand(Guid PlayerId, Guid ItemId) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player releases a previously claimed clothing item back to the communal pool.
    /// Only items claimed via <see cref="ClaimPoolItemCommand"/> may be unclaimed; a player
    /// cannot unclaim an item they drew themselves.
    /// </summary>
    public record UnclaimPoolItemCommand(Guid PlayerId, Guid ItemId) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player submits their assembled outfit, selecting one item per clothing type.
    /// </summary>
    public record SubmitOutfitCommand(
        Guid PlayerId,
        Dictionary<ClothingType, Guid> SelectedItemsByType) : DrawnToDressCommand(PlayerId);

    // ── Outfit customization ──────────────────────────────────────────────────

    /// <summary>
    /// Player finalizes the custom name and optional sketch overlay for their outfit.
    /// <paramref name="OutfitName"/> is required; <paramref name="SketchSvgContent"/> is
    /// optional unless <see cref="DrawnToDressSettings.SketchingRequired"/> is enabled.
    /// </summary>
    public record SubmitCustomizationCommand(
        Guid PlayerId,
        string? OutfitName,
        string? SketchSvgContent = null,
        Dictionary<ClothingType, ItemPositionOverride>? ItemPositionOverrides = null,
        FaceType SelectedFace = FaceType.Default,
        bool ShowMannequin = false) : DrawnToDressCommand(PlayerId);

    /// <summary>Updates the draft outfit name for the player while they are typing.</summary>
    public record UpdateDraftOutfitNameCommand(
        Guid PlayerId,
        string DraftName) : DrawnToDressCommand(PlayerId);

    // ── Outfit distinctness resolution ────────────────────────────────────────

    /// <summary>
    /// Player resolves a distinctness conflict by swapping the contested item for a
    /// different one.
    /// </summary>
    public record ResolveDistinctnessCommand(
        Guid PlayerId,
        Guid ReplacementItemId) : DrawnToDressCommand(PlayerId);

    // ── Voting ────────────────────────────────────────────────────────────────

    /// <summary>Player casts a vote for one entrant in a head-to-head matchup.</summary>
    public record CastVoteCommand(
        Guid PlayerId,
        Guid MatchupId,
        string CriterionId,
        EntrantId ChosenEntrantId) : DrawnToDressCommand(PlayerId)
    {
        /// <summary>Player ID of the chosen entrant.</summary>
        public Guid ChosenPlayerId => ChosenEntrantId.PlayerId;
    }

    // ── Coin flip ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Requests a coin-flip tie-break for the specified tied matchup.
    /// Typically issued by the game engine when a voting round ends with a tie.
    /// </summary>
    public record RequestCoinFlipCommand(
        Guid PlayerId,
        Guid MatchupId) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// The designated caller chooses heads or tails for the current coin flip.
    /// </summary>
    public record CoinFlipCallCommand(
        Guid PlayerId,
        bool ChoseHeads) : DrawnToDressCommand(PlayerId);

    // ── Final results ────────────────────────────────────────────────────────

    /// <summary>
    /// Host requests a new game with the same players (returns to lobby).
    /// </summary>
    public record PlayAgainCommand(Guid PlayerId) : DrawnToDressCommand(PlayerId);

    /// <summary>
    /// Player exits to the main menu.
    /// </summary>
    public record ReturnToMenuCommand(Guid PlayerId) : DrawnToDressCommand(PlayerId);

    // ── Game control ──────────────────────────────────────────────────────────

    /// <summary>Host pauses the game, saving the current state for later resumption.</summary>
    public record PauseGameCommand(Guid PlayerId) : DrawnToDressCommand(PlayerId);

    /// <summary>Host resumes a previously paused game.</summary>
    public record ResumeGameCommand(Guid PlayerId) : DrawnToDressCommand(PlayerId);
}
