using System.Collections.Concurrent;
using System.Collections.Generic;
using KnockBox.Core.Primitives.Returns;
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
    
    public List<Card> Deck { get; set; } = [];
    public List<Card> DiscardPile { get; set; } = [];
    public List<ActionLogEntry> ActionLog { get; set; } = [];
    public string? LastBlockedActionMessage { get; set; }
    public string? BlockedAttackerId { get; set; }
    
    public OperatorGamePhase Phase { get; set; } = OperatorGamePhase.Setup;
    
    // Host-configurable match rules. Always replaced atomically via UpdateSettings; the
    // setter is private so callers can't bypass the lock. Persisted to the host's browser
    // localStorage by the lobby page so preferred rules survive across sessions.
    public OperatorSettings Settings { get; private set; } = new();

    /// <summary>
    /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s result
    /// inside <see cref="AbstractGameState.Execute(Action)"/>, so subscribers observe a
    /// single consistent transition and notification fires once after the lock releases.
    /// </summary>
    public Result UpdateSettings(Func<OperatorSettings, OperatorSettings> mutate) =>
        Execute(() => { Settings = mutate(Settings); });

    public DateTimeOffset StateStartTime { get; set; } = DateTimeOffset.UtcNow;
    
    public TurnManager TurnManager { get; } = new();

    public IGameActionCommand? PendingGameActionCommand { get; set; }
    public HashSet<string> ReactionTargetPlayerIds { get; set; } = [];
    public List<PlayerReaction> PlayerReactions { get; set; } = [];

    public int TurnCount { get; set; }

    public string? WinnerPlayerId { get; set; }
}
