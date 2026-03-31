using KnockBox.Services.Logic.Games.ConsultTheCard.FSM;
using KnockBox.Services.State.Games.ConsultTheCard.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.ConsultTheCard
{
    public class ConsultTheCardGameState(
        User host,
        ILogger<ConsultTheCardGameState> logger)
        : AbstractGameState(host, logger)
    {
        /// <summary>
        /// The FSM context for this game instance. Set when the game starts.
        /// </summary>
        public ConsultTheCardGameContext? Context { get; internal set; }

        /// <summary>
        /// The current phase of the game.
        /// </summary>
        public ConsultTheCardGamePhase GamePhase { get; set; }

        /// <summary>
        /// All player states, keyed by player ID.
        /// </summary>
        public readonly ConcurrentDictionary<string, ConsultTheCardPlayerState> GamePlayers = new();

        /// <summary>
        /// Players in turn order (by player ID).
        /// </summary>
        public readonly List<string> TurnOrder = [];

        /// <summary>
        /// Index into <see cref="TurnOrder"/> identifying the current clue-giving player.
        /// </summary>
        public int CurrentCluePlayerIndex { get; set; }

        /// <summary>
        /// The current elimination cycle number within a game. Starts at 0.
        /// </summary>
        public int CurrentEliminationCycle { get; set; }

        /// <summary>
        /// The current game number in a multi-game session. Starts at 1.
        /// </summary>
        public int CurrentGameNumber { get; set; } = 1;

        /// <summary>
        /// The pair of words selected for the current game (Agent word and Insider word).
        /// </summary>
        public string[]? CurrentWordPair { get; set; }

        /// <summary>
        /// Clues submitted during the current round.
        /// </summary>
        public readonly List<ClueEntry> CurrentRoundClues = [];

        /// <summary>
        /// Votes cast during the current round.
        /// </summary>
        public readonly List<VoteEntry> CurrentRoundVotes = [];

        /// <summary>
        /// The result of the most recent elimination.
        /// </summary>
        public EliminationResult? LastElimination { get; set; }

        /// <summary>
        /// The result of the most recent Informant guess attempt.
        /// </summary>
        public InformantGuessResult? LastInformantGuess { get; set; }

        /// <summary>
        /// True when the game is waiting for the Informant to guess a word.
        /// </summary>
        public bool AwaitingInformantGuess { get; set; }

        /// <summary>
        /// The result of the win condition evaluation, if the game has ended.
        /// </summary>
        public WinConditionResult? WinResult { get; set; }

        /// <summary>
        /// Game configuration (tunable playtesting values).
        /// </summary>
        public ConsultTheCardGameConfig Config { get; set; } = new();

        /// <summary>
        /// Tracking for the "vote to end game" mechanic.
        /// </summary>
        public EndGameVoteStatus EndGameVoteStatus { get; set; } = new([], 0);

        /// <summary>
        /// Tracking for the "vote to skip remaining time" mechanic.
        /// </summary>
        public EndGameVoteStatus SkipTimeVoteStatus { get; set; } = new([], 0);

        /// <summary>
        /// All clue words used by any player in the current game.
        /// Prevents reuse across players and cycles.
        /// </summary>
        public readonly HashSet<string> UsedClues = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cumulative scores across games, keyed by player ID.
        /// </summary>
        public readonly Dictionary<string, int> GameScores = [];
    }

    #region Enums

    public enum ConsultTheCardGamePhase
    {
        Setup,
        CluePhase,
        Discussion,
        Voting,
        Reveal,
        GameOver
    }

    public enum Role
    {
        Agent,
        Insider,
        Informant
    }

    #endregion

    #region Records

    /// <summary>A thematic group of 2+ words; 2 selected at runtime.</summary>
    public record WordGroup(string[] Words);

    /// <summary>A clue submitted by a player during the clue phase.</summary>
    public record ClueEntry(string PlayerId, string PlayerName, string Clue);

    /// <summary>A vote cast by a player during the voting phase.</summary>
    public record VoteEntry(string VoterId, string VoterName, string TargetId, string TargetName);

    /// <summary>The result of an elimination round.</summary>
    public record EliminationResult(string PlayerId, string PlayerName, Role Role, bool WasTie);

    /// <summary>The result of the Informant's word guess attempt.</summary>
    public record InformantGuessResult(string PlayerId, string PlayerName, string GuessedWord, bool WasCorrect);

    /// <summary>Evaluates whether the game is over and which team won.</summary>
    public record WinConditionResult(bool GameOver, Role? WinningTeam, string Reason);

    /// <summary>Tracks player votes to end the game early.</summary>
    public record EndGameVoteStatus(HashSet<string> VotedToEnd, int RequiredVotes);

    #endregion

    #region Configuration

    public class ConsultTheCardGameConfig
    {
        public int SetupPhaseTimeoutMs { get; set; } = 5000;
        public int CluePhaseTimeoutMs { get; set; } = 30000;
        public int DiscussionPhaseTimeoutMs { get; set; } = 120000;
        public int VotePhaseTimeoutMs { get; set; } = 15000;
        public int RevealPhaseTimeoutMs { get; set; } = 10000;
        public int InformantGuessTimeoutMs { get; set; } = 30000;
        public bool EnableTimers { get; set; } = true;
        public int TotalGames { get; set; } = 5;
    }

    #endregion
}
