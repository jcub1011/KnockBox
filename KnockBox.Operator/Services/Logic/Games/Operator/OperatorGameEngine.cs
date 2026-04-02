using KnockBox.Extensions.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KnockBox.Services.Logic.Games.Operator;

public class OperatorGameEngine(ILogger<OperatorGameState> stateLogger) 
    : AbstractGameEngine(minPlayerCount: 2, maxPlayerCount: 8)
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
    {
        if (host is null)
            return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", "Host was null."));

        var state = new OperatorGameState(host, stateLogger);
        state.Context = new OperatorGameContext(state);
        state.UpdateJoinableStatus(true);
        return Task.FromResult(ValueResult<AbstractGameState>.FromValue(state));
    }

    public override async Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
    {
        if (state is not OperatorGameState operatorState)
        {
            return Result.FromError("Invalid game state type.");
        }

        if (host.Id != operatorState.Host.Id)
        {
            return Result.FromError("Only the host can start the game.");
        }

        return await state.ExecuteAsync(() =>
        {
            // 1. Generate Deck
            // We include the host in the player count.
            var allPlayers = operatorState.Players.Concat([operatorState.Host]).ToList();
            operatorState.Deck = OperatorGameContext.GenerateDeck(allPlayers.Count);
            
            // 2. Initialize GamePlayers and Deal 5 cards
            foreach (var user in allPlayers)
            {
                var playerState = new OperatorPlayerState { UserId = user.Id };
                for (int i = 0; i < 5; i++)
                {
                    if (operatorState.Deck.Count > 0)
                    {
                        var card = operatorState.Deck[0];
                        operatorState.Deck.RemoveAt(0);
                        playerState.Hand.Add(card);
                    }
                }
                operatorState.GamePlayers[user.Id] = playerState;
            }

            // 3. Set Phase to Setup
            operatorState.Phase = OperatorGamePhase.Setup;
            
            // 4. Initialize Turn Manager
            operatorState.TurnManager.SetTurnOrder(allPlayers.Select(p => p.Id));
            
            // 5. Update Joinable Status
            operatorState.UpdateJoinableStatus(false);
            
            return ValueTask.CompletedTask;
        }, ct);
    }

    /// <summary>
    /// Processes a game command by delegating to the current FSM state.
    /// </summary>
    public Task<Result> ProcessCommand(OperatorGameState state, OperatorCommand command)
    {
        // Stub for now. Transitions to actual FSM logic will happen in Phase 3.
        return Task.FromResult(Result.Success);
    }
}
