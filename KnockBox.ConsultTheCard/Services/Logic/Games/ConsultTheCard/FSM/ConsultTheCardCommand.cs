namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM
{
    /// <summary>
    /// Base for all player-issued commands processed by the Consult the Card FSM.
    /// Every command carries the ID of the player who issued it so that states can
    /// validate permissions (host-only commands, active-player restrictions, etc.).
    /// </summary>
    public abstract record ConsultTheCardCommand(string PlayerId);

    // ── CluePhase ─────────────────────────────────────────────────────────────

    /// <summary>Player submits a one-word clue during the clue phase.</summary>
    public record SubmitClueCommand(string PlayerId, string Clue) : ConsultTheCardCommand(PlayerId);

    // ── Discussion ────────────────────────────────────────────────────────────

    /// <summary>Host advances the game from discussion to the voting phase.</summary>
    public record AdvanceToVoteCommand(string PlayerId) : ConsultTheCardCommand(PlayerId);

    /// <summary>Any player votes to end the current game (once per elimination cycle).</summary>
    public record VoteToEndGameCommand(string PlayerId) : ConsultTheCardCommand(PlayerId);

    // ── Voting ────────────────────────────────────────────────────────────────

    /// <summary>Player casts a vote to eliminate the targeted player.</summary>
    public record CastVoteCommand(string PlayerId, string TargetPlayerId) : ConsultTheCardCommand(PlayerId);

    // ── Reveal ────────────────────────────────────────────────────────────────

    /// <summary>Informant guesses the Agent word during the reveal phase.</summary>
    public record InformantGuessCommand(string PlayerId, string GuessedWord) : ConsultTheCardCommand(PlayerId);

    // ── GameOver ──────────────────────────────────────────────────────────────

    /// <summary>Host starts the next game in a multi-game session.</summary>
    public record StartNextGameCommand(string PlayerId) : ConsultTheCardCommand(PlayerId);

    /// <summary>Host returns all players to the lobby.</summary>
    public record ReturnToLobbyCommand(string PlayerId) : ConsultTheCardCommand(PlayerId);
}
