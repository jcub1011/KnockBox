using KnockBox.Codeword.Services.Logic.Games.FSM;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Components;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Codeword.Services.State.Games
{
    public class CodewordGameState(
        User host,
        ILogger<CodewordGameState> logger)
        : AbstractGameState(host, logger),
          IPhasedGameState<CodewordGamePhase>,
          IPlayerTrackedGameState<CodewordPlayerState>,
          IFsmContextGameState<CodewordGameContext>
    {
        /// <summary>
        /// The FSM context for this game instance. Set when the game starts.
        /// </summary>
        public CodewordGameContext? Context { get; set; }

        /// <summary>
        /// The current phase of the game.
        /// </summary>
        public CodewordGamePhase Phase { get; private set; }

        /// <summary>
        /// Updates the current phase. Notification is intentionally NOT raised here —
        /// callers run inside <c>Execute</c>/<c>ExecuteAsync</c>, which fires
        /// <c>NotifyStateChanged</c> exactly once after the lock is released.
        /// Calling Notify inline would run subscribers while the executeLock is held
        /// and can deadlock the Blazor dispatcher (see arch doc:
        /// "Notify outside the lock").
        /// </summary>
        public void SetPhase(CodewordGamePhase phase) => Phase = phase;

        /// <summary>
        /// All player states, keyed by player ID.
        /// </summary>
        public ConcurrentDictionary<Guid, CodewordPlayerState> GamePlayers { get; } = new();

        /// <summary>
        /// Manages turn order and active player tracking.
        /// </summary>
        public TurnManager TurnManager { get; } = new();

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
        /// Host-configurable match rules. Always replaced atomically via
        /// <see cref="UpdateSettings"/>; the setter is private so callers can't bypass
        /// the lock. Persisted to the host's browser localStorage by the lobby page so
        /// preferred rules survive across sessions.
        /// </summary>
        public CodewordSettings Settings { get; private set; } = new();

        /// <summary>
        /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s
        /// result and reflects the new <c>HostPlays</c> value into
        /// <see cref="AbstractGameState.HostIsParticipant"/> in the same critical
        /// section. The replacement + participation update happen inside one
        /// <see cref="AbstractGameState.Execute(Action)"/>, so subscribers observe a
        /// single consistent transition.
        /// </summary>
        public Result UpdateSettings(Func<CodewordSettings, CodewordSettings> mutate) =>
            Execute(() =>
            {
                Settings = mutate(Settings);
                SetHostIsParticipant(Settings.HostPlays);
            });

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
        public readonly Dictionary<string, string> UsedClues = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cumulative scores across games, keyed by player ID.
        /// </summary>
        public readonly Dictionary<Guid, int> GameScores = [];
    }

    #region Enums

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
    public record ClueEntry(Guid PlayerId, string PlayerName, string Clue);

    /// <summary>A vote cast by a player during the voting phase.</summary>
    public record VoteEntry(Guid VoterId, string VoterName, Guid TargetId, string TargetName);

    /// <summary>The result of an elimination round.</summary>
    public record EliminationResult(Guid PlayerId, string PlayerName, Role Role, bool WasTie);

    /// <summary>The result of the Informant's word guess attempt.</summary>
    public record InformantGuessResult(Guid PlayerId, string PlayerName, string GuessedWord, bool WasCorrect);

    /// <summary>Evaluates whether the game is over and which team won.</summary>
    public record WinConditionResult(bool GameOver, Role? WinningTeam, string Reason);

    /// <summary>Tracks player votes to end the game early.</summary>
    public record EndGameVoteStatus(HashSet<Guid> VotedToEnd, int RequiredVotes);

    #endregion
}
