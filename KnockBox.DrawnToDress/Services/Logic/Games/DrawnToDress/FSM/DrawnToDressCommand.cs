using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM
{
    /// <summary>Base for all player-issued commands processed by the DrawnToDress FSM.</summary>
    public abstract record DrawnToDressCommand(string PlayerId);

    // ── Drawing phase ─────────────────────────────────────────────────────────

    /// <summary>Host advances to the next clothing type (or ends the drawing phase).</summary>
    public record AdvanceDrawingRoundCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    /// <summary>A player submits a drawing for the current clothing type.</summary>
    public record SubmitDrawingCommand(string PlayerId, string SvgData) : DrawnToDressCommand(PlayerId);

    // ── Outfit building phase ─────────────────────────────────────────────────

    /// <summary>A player claims an item from the shared clothing pool.</summary>
    public record ClaimItemCommand(string PlayerId, Guid ItemId) : DrawnToDressCommand(PlayerId);

    /// <summary>A player returns a previously claimed item back to the pool.</summary>
    public record ReturnItemCommand(string PlayerId, Guid ItemId, ClothingType SlotType) : DrawnToDressCommand(PlayerId);

    /// <summary>A player locks in their outfit (prevents further changes).</summary>
    public record LockOutfitCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    /// <summary>Host ends the outfit building phase.</summary>
    public record EndOutfitBuildingCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    // ── Outfit customization phase ────────────────────────────────────────────

    /// <summary>A player submits their outfit with a name and optional sketch.</summary>
    public record SubmitOutfitCommand(string PlayerId, string Name, string? SketchData = null)
        : DrawnToDressCommand(PlayerId);

    /// <summary>Host ends the customization phase and advances the game.</summary>
    public record EndCustomizationCommand(string PlayerId) : DrawnToDressCommand(PlayerId);

    // ── Voting phase ──────────────────────────────────────────────────────────

    /// <summary>
    /// A player casts a vote for a matchup.
    /// <paramref name="Votes"/> maps each criterion to <c>true</c> (Outfit A) or <c>false</c> (Outfit B).
    /// </summary>
    public record CastVoteCommand(string PlayerId, Guid MatchupId, Dictionary<VotingCriterion, bool> Votes)
        : DrawnToDressCommand(PlayerId);

    /// <summary>Host finalizes the current voting round and advances (or ends) the tournament.</summary>
    public record FinalizeVotingRoundCommand(string PlayerId) : DrawnToDressCommand(PlayerId);
}
