using KnockBox.Operator.Models;
using KnockBox.Operator.Services.State;
using System.Collections.Concurrent;

namespace KnockBox.Operator.Services.Logic.FSM;

public class OperatorGameContext(OperatorGameState state)
{
    public OperatorGameState State { get; } = state;

    public ConcurrentDictionary<string, OperatorPlayerState> GamePlayers => State.GamePlayers;
}
