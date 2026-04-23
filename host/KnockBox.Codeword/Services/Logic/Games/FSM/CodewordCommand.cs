namespace KnockBox.Codeword.Services.Logic.Games.FSM
{
    /// <summary>
    /// Base for all player-issued commands processed by the Consult the Card FSM.
    /// Every command carries the ID of the player who issued it so that states can
    /// validate permissions (host-only commands, active-player restrictions, etc.).
    /// </summary>
    public abstract record CodewordCommand(string PlayerId);

    // ── CluePhase ─────────────────────────────────────────────────────────────

    /// <summary>Player submits a one-word clue during the clue phase.</summary>
    public record SubmitClueCommand(string PlayerId, string Clue) : CodewordCommand(PlayerId);

    // ── Discussion ────────────────────────────────────────────────────────────

    /// <summary>Host advances the game from discussion to the voting phase.</summary>
    public record AdvanceToVoteCommand(string PlayerId) : CodewordCommand(PlayerId);

    /// <summary>Any player votes to end the current game (once per elimination cycle).</summary>
    public record VoteToEndGameCommand(string PlayerId) : CodewordCommand(PlayerId);

    /// <summary>Player skips the remaining discussion time.</summary>
    public record SkipRemainingTimeCommand(string PlayerId) : CodewordCommand(PlayerId);

    // ── Voting ────────────────────────────────────────────────────────────────

    /// <summary>Player selects a target to eliminate (not yet locked in).</summary>
    public record CastVoteCommand(string PlayerId, string TargetPlayerId) : CodewordCommand(PlayerId);

    /// <summary>Player locks in their selected vote.</summary>
    public record LockInVoteCommand(string PlayerId) : CodewordCommand(PlayerId);

    // ── Reveal ────────────────────────────────────────────────────────────────

    /// <summary>Informant guesses the Agent word during the reveal phase.</summary>
    public record InformantGuessCommand(string PlayerId, string GuessedWord) : CodewordCommand(PlayerId);

    // ── GameOver ──────────────────────────────────────────────────────────────

    /// <summary>Host starts the next game in a multi-game session.</summary>
    public record StartNextGameCommand(string PlayerId) : CodewordCommand(PlayerId);

    /// <summary>Host returns all players to the lobby.</summary>
    public record ReturnToLobbyCommand(string PlayerId) : CodewordCommand(PlayerId);
}
