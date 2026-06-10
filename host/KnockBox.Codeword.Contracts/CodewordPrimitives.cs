namespace KnockBox.Codeword.Contracts;

/// <summary>The phases of a Codeword game, in order.</summary>
public enum CodewordGamePhase
{
    Setup,
    CluePhase,
    Discussion,
    Voting,
    Reveal,
    ContinueOrEndRound,
    GameOver
}

/// <summary>
/// A player's hidden role for the current game. The Agent/Insider word pair plus the
/// Informant make up the social-deduction roster; a recipient learns only their OWN
/// role (projected as <c>CodewordView.MyRole</c>) and the publicly revealed role of an
/// eliminated player (<c>CodewordPlayerStateView.RevealedRole</c>).
/// </summary>
public enum Role
{
    Agent,
    Insider,
    Informant
}

/// <summary>A clue submitted by a player during the clue phase (public once submitted).</summary>
public record ClueEntry(Guid PlayerId, string PlayerName, string Clue);

/// <summary>A vote cast by a player during the voting phase.</summary>
public record VoteEntry(Guid VoterId, string VoterName, Guid TargetId, string TargetName);

/// <summary>The result of an elimination round.</summary>
public record EliminationResult(Guid PlayerId, string PlayerName, Role Role, bool WasTie);

/// <summary>The result of the Informant's word guess attempt.</summary>
public record InformantGuessResult(Guid PlayerId, string PlayerName, string GuessedWord, bool WasCorrect);

/// <summary>Evaluates whether the game is over and which team won.</summary>
public record WinConditionResult(bool GameOver, Role? WinningTeam, string Reason);

/// <summary>Tracks player votes to end the current game / skip remaining time early.</summary>
public record EndGameVoteStatus(HashSet<Guid> VotedToEnd, int RequiredVotes);
