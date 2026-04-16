using System.Collections.Concurrent;
using System.Collections.Generic;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Components;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;

namespace KnockBox.Operator.Services.State;

public class OperatorGameState(
    User host,
    ILogger<OperatorGameState> logger)
    : AbstractGameState(host, logger),
      IFsmContextGameState<OperatorGameContext>
{
    public OperatorGameContext? Context { get; set; }

    public ConcurrentDictionary<string, OperatorPlayerState> GamePlayers { get; } = new();
    
    public List<Card> Deck { get; set; } = new();
    public List<Card> DiscardPile { get; set; } = new();
    public List<ActionLogEntry> ActionLog { get; set; } = new();
    public string? LastBlockedActionMessage { get; set; }
    public string? BlockedAttackerId { get; set; }
    
    public OperatorGamePhase Phase { get; set; } = OperatorGamePhase.Setup;
    
    public OperatorGameConfig Config { get; set; } = new();
    public DateTimeOffset StateStartTime { get; set; } = DateTimeOffset.UtcNow;
    
    public TurnManager TurnManager { get; } = new();

    public IGameActionCommand? PendingGameActionCommand { get; set; }
    public HashSet<string> ReactionTargetPlayerIds { get; set; } = new();
    public List<PlayerReaction> PlayerReactions { get; set; } = new();

    public int TurnCount { get; set; }

    public string? WinnerPlayerId { get; set; }
}
