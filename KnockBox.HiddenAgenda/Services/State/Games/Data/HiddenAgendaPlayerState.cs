using System.Collections.Generic;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;

namespace KnockBox.HiddenAgenda.Services.State.Games.Data;

public class HiddenAgendaPlayerState
{
    public string PlayerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Position
    public int CurrentSpaceId { get; set; }

    // Secret tasks (3 per round)
    public List<SecretTask> SecretTasks { get; set; } = [];

    // Event card (hold max 1)
    public EventCard? HeldEventCard { get; set; }

    // Detour state
    public bool DetourPending { get; set; }
    public string? DetourTargetPlayerId { get; set; }

    // Guess submission
    public bool HasSubmittedGuess { get; set; }
    // Key: opponent player ID, Value: list of 3 guessed task IDs
    public Dictionary<string, List<string>>? GuessSubmission { get; set; }

    // Scoring
    public int RoundScore { get; set; }
    public int CumulativeScore { get; set; }

    // Turn tracking
    public int TurnsTakenThisRound { get; set; }
    public int GuessCountdownTurnsRemaining { get; set; } // Set when countdown triggers

    /// <summary>
    /// For Rivalry task R6: the randomly assigned player to shadow.
    /// Set at task draw time if R6 is in the player's tasks.
    /// </summary>
    public string? RivalryTargetPlayerId { get; set; }

    // Spin/movement history
    public int LastSpinResult { get; set; }
    public int? LastMoveDestination { get; set; }
    public List<MovementRecord> MovementHistory { get; set; } = [];

    // Card history (for task evaluation and Catalog event)
    public List<CardPlayRecord> CardPlayHistory { get; set; } = [];
    public List<CardDrawRecord> CardDrawHistory { get; set; } = [];
}

public record MovementRecord(int TurnNumber, int SpaceId, Wing Wing, int SpinResult);

public record CardPlayRecord(
    int TurnNumber,
    CurationCard Card,
    int SelectedIndex,           // Which of the 3 drawn cards was selected
    CollectionType[] AffectedCollections,
    CurationCardType CardType,
    CurationCardType EffectiveCardType,  // Acquire if all deltas positive, Remove if all negative, Trade if mixed
    Dictionary<CollectionType, int>? CollectionProgressSnapshot = null  // snapshot at time of play, for R4/R5 evaluation
);

public record CardDrawRecord(
    int TurnNumber,
    List<CurationCard> DrawnCards  // All 3 drawn cards (for Catalog reveals)
);
