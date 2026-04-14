using System.Collections.Concurrent;
using System.Collections.Generic;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Components;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Users;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.Extensions.Logging;

namespace KnockBox.HiddenAgenda.Services.State.Games;

public class HiddenAgendaGameState(User host, ILogger<HiddenAgendaGameState> logger)
    : AbstractGameState(host, logger),
      IPhasedGameState<GamePhase>,
      IConfigurableGameState<HiddenAgendaGameConfig>,
      IPlayerTrackedGameState<HiddenAgendaPlayerState>,
      IFsmContextGameState<HiddenAgendaGameContext>
{
    public GamePhase Phase { get; private set; }

    public void SetPhase(GamePhase phase)
    {
        Phase = phase;
    }

    // FSM context (set when game starts)
    public HiddenAgendaGameContext? Context { get; set; }

    // Configuration
    public HiddenAgendaGameConfig Config { get; set; } = new();

    // Player state
    public ConcurrentDictionary<string, HiddenAgendaPlayerState> GamePlayers { get; } = new();

    // Turn management
    public TurnManager TurnManager { get; } = new();

    // Board
    public BoardGraph BoardGraph { get; set; } = null!;

    // Collection progress (mutable, reset each round)
    public Dictionary<CollectionType, int> CollectionProgress { get; } = new();

    // Round tracking
    public int CurrentRound { get; set; }
    public int TotalTurnsTaken { get; set; }

    // Guess countdown
    public bool GuessCountdownActive { get; set; }
    public string? FirstGuessPlayerId { get; set; }

    // Task pool for current round
    public IReadOnlyList<SecretTask> CurrentTaskPool { get; set; } = [];

    // Global play history for cross-player task evaluation (Rivalry tasks)
    public List<TurnRecord> RoundPlayHistory { get; } = [];

    // Reachable spaces for current player during MovePhase (set by FSM state)
    public List<BoardSpace>? ReachableSpaces { get; set; }

    // Current player's drawn cards during DrawPhase (set by FSM state)
    public List<CurationCard>? DrawnCards { get; set; }
}

public enum GamePhase
{
    Lobby,
    RoundSetup,
    EventCardPhase,
    SpinPhase,
    MovePhase,
    DrawPhase,
    GuessPhase,
    FinalGuess,
    Reveal,
    RoundOver,
    MatchOver
}

public record TurnRecord(
    int TurnNumber,
    string PlayerId,
    CardPlayRecord? CardPlay,
    int SpaceId,
    Wing Wing
);
