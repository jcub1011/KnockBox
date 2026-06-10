using System.Text.Json.Serialization;

namespace KnockBox.Codeword.Contracts;

/// <summary>
/// Per-recipient projected view of a Codeword lobby, sent server → browser over the hub.
/// The projection is <b>default-deny</b>: the hidden word pair is never put on the wire,
/// and the only secret that crosses is the recipient's OWN role + secret word
/// (<see cref="MyRole"/> / <see cref="MySecretWord"/>). Other players' roles surface only
/// once publicly revealed via <see cref="CodewordPlayerStateView.RevealedRole"/> (eliminated
/// players) or the round-result records (<see cref="LastElimination"/> etc.).
/// </summary>
public sealed record CodewordView(
    CodewordGamePhase Phase,
    Guid HostId,
    Guid RecipientId,
    bool HostIsParticipant,
    bool IsJoinable,
    int CurrentGameNumber,
    int CurrentEliminationCycle,
    Guid? CurrentPlayerId,
    // The recipient's own secret — the ONLY secret on the wire:
    Role? MyRole,
    string? MySecretWord,
    // Public roster / players:
    IReadOnlyList<RosterEntryView> Roster,
    IReadOnlyList<CodewordPlayerStateView> Players,
    // Public round data:
    IReadOnlyList<ClueEntry> CurrentRoundClues,
    IReadOnlyList<VoteEntry> CurrentRoundVotes,
    EliminationResult? LastElimination,
    InformantGuessResult? LastInformantGuess,
    bool AwaitingInformantGuess,
    WinConditionResult? WinResult,
    EndGameVoteStatus EndGameVoteStatus,
    EndGameVoteStatus SkipTimeVoteStatus,
    IReadOnlyDictionary<string, string> UsedClues,
    IReadOnlyDictionary<string, int> GameScores,
    CodewordSettings Settings,
    DateTimeOffset? PhaseEndsAtUtc,
    int PhaseDurationSeconds);

/// <summary>A pre-game lobby roster entry (host + joined players), display names only.</summary>
public sealed record RosterEntryView(Guid PlayerId, string DisplayName, bool IsHost);

/// <summary>
/// Per-player in-game state. <b>All fields are public</b> — a player's secret role/word is
/// never carried here. <see cref="RevealedRole"/> is non-null only for an eliminated player
/// once the phase publicly reveals it (Reveal / ContinueOrEndRound / GameOver).
/// </summary>
public sealed record CodewordPlayerStateView(
    Guid PlayerId,
    string DisplayName,
    bool IsEliminated,
    bool HasSubmittedClue,
    bool HasVoted,
    Guid? VoteTargetId,
    bool? ContinueOrEndVote,
    bool HasVotedToEndGame,
    bool HasVotedToSkipTime,
    int Score,
    // All clues this player has submitted this game — public (every clue is broadcast).
    IReadOnlyList<string> ClueHistory,
    Role? RevealedRole);

/// <summary>Command names the client sends to the server engine via the hub.</summary>
public static class CodewordCommands
{
    public const string Start = "start";                    // host deals (HostPlays carried in payload)
    public const string UpdateSettings = "update-settings";
    public const string KickPlayer = "kick-player";
    public const string SubmitClue = "submit-clue";
    public const string CastVote = "cast-vote";
    public const string LockInVote = "lock-in-vote";
    public const string InformantGuess = "informant-guess";
    public const string AdvanceToVote = "advance-to-vote";
    public const string SkipTime = "skip-time";
    public const string VoteEndGame = "vote-end-game";
    public const string ContinueOrEndRound = "continue-or-end-round";
    public const string StartNextGame = "start-next-game";
    public const string ReturnToLobby = "return-to-lobby";
}

/// <summary>Payload for <see cref="CodewordCommands.Start"/>: whether the host plays.</summary>
public sealed record StartPayload(bool HostPlays);

/// <summary>Payload for <see cref="CodewordCommands.SubmitClue"/>.</summary>
public sealed record SubmitCluePayload(string Clue);

/// <summary>Payload for <see cref="CodewordCommands.CastVote"/>.</summary>
public sealed record VotePayload(Guid TargetId);

/// <summary>Payload for <see cref="CodewordCommands.InformantGuess"/>.</summary>
public sealed record InformantGuessPayload(string GuessedWord);

/// <summary>Payload for <see cref="CodewordCommands.ContinueOrEndRound"/>.</summary>
public sealed record ContinueOrEndPayload(bool VoteToEnd);

/// <summary>Payload for <see cref="CodewordCommands.KickPlayer"/>.</summary>
public sealed record KickPayload(Guid TargetId);

/// <summary>
/// Source-generated JSON context so the contract DTOs survive IL trimming in the WASM
/// client without reflection roots. <c>UseStringEnumConverter</c> matches the server's
/// wire format (the host's <c>GameViewCoordinator</c> writes enums as strings) for the
/// projected view and every command payload. <see cref="CodewordSettings"/> doubles as
/// the <c>update-settings</c> payload.
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(CodewordView))]
[JsonSerializable(typeof(CodewordSettings))]
[JsonSerializable(typeof(StartPayload))]
[JsonSerializable(typeof(SubmitCluePayload))]
[JsonSerializable(typeof(VotePayload))]
[JsonSerializable(typeof(InformantGuessPayload))]
[JsonSerializable(typeof(ContinueOrEndPayload))]
[JsonSerializable(typeof(KickPayload))]
public partial class CodewordContractsJsonContext : JsonSerializerContext;
