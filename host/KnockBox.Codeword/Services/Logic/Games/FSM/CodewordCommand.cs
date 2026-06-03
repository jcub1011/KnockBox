namespace KnockBox.Codeword.Services.Logic.Games.FSM
{
    /// <summary>
    /// Base for all player-issued commands processed by the Consult the Card FSM.
    /// Every command carries the ID of the player who issued it so that states can
    /// validate permissions (host-only commands, active-player restrictions, etc.).
    /// </summary>
    public abstract record CodewordCommand(Guid PlayerId);

    // ── CluePhase ─────────────────────────────────────────────────────────────

    /// <summary>Player submits a one-word clue during the clue phase.</summary>
    public record SubmitClueCommand(Guid PlayerId, string Clue) : CodewordCommand(PlayerId);

    // ── Discussion ────────────────────────────────────────────────────────────

    /// <summary>Host advances the game from discussion to the voting phase.</summary>
    public record AdvanceToVoteCommand(Guid PlayerId) : CodewordCommand(PlayerId);

    /// <summary>Any player votes to end the current game (once per elimination cycle).</summary>
    public record VoteToEndGameCommand(Guid PlayerId) : CodewordCommand(PlayerId);

    /// <summary>Player skips the remaining discussion time.</summary>
    public record SkipRemainingTimeCommand(Guid PlayerId) : CodewordCommand(PlayerId);

    // ── Voting ────────────────────────────────────────────────────────────────

    /// <summary>Player selects a target to eliminate (not yet locked in).</summary>
    public record CastVoteCommand(Guid PlayerId, Guid TargetPlayerId) : CodewordCommand(PlayerId);

    /// <summary>Player locks in their selected vote.</summary>
    public record LockInVoteCommand(Guid PlayerId) : CodewordCommand(PlayerId);

    // ── Reveal ────────────────────────────────────────────────────────────────

    /// <summary>Informant guesses the Agent word during the reveal phase.</summary>
    public record InformantGuessCommand(Guid PlayerId, string GuessedWord) : CodewordCommand(PlayerId);

    // ── ContinueOrEndRound ────────────────────────────────────────────────────

    /// <summary>Player votes whether to continue the game with another round
    /// or end the game now (after a non-Informant elimination).</summary>
    public record ContinueOrEndRoundVoteCommand(Guid PlayerId, bool VoteToEnd) : CodewordCommand(PlayerId);

    // ── GameOver ──────────────────────────────────────────────────────────────

    /// <summary>Host starts the next game in a multi-game session.</summary>
    public record StartNextGameCommand(Guid PlayerId) : CodewordCommand(PlayerId);

    /// <summary>Host returns all players to the lobby.</summary>
    public record ReturnToLobbyCommand(Guid PlayerId) : CodewordCommand(PlayerId);
}
