using KnockBox.Extensions.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KnockBox.Services.Logic.Games.Operator;

public class OperatorGameEngine(ILogger<OperatorGameState> stateLogger, IRandomNumberService randomNumberService)
    : AbstractGameEngine(minPlayerCount: 2, maxPlayerCount: int.MaxValue)
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
    {
        if (host is null)
            return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", "Host was null."));

        var state = new OperatorGameState(host, stateLogger);
        state.Context = new OperatorGameContext(state, randomNumberService);
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

        var context = new OperatorGameContext(operatorState, randomNumberService);
        var fsm = new FiniteStateMachine<OperatorGameContext, OperatorCommand>(stateLogger);
        context.Fsm = fsm;

        return await state.ExecuteAsync(() =>
        {
            var allParticipants = operatorState.Players.ToList();

            // Initialize GamePlayers (deck generation and dealing happen in SetupState after choices)
            foreach (var user in allParticipants)
            {
                var playerState = new OperatorPlayerState { UserId = user.Id };
                operatorState.GamePlayers[user.Id] = playerState;
            }

            // 3. Set Phase to Setup
            operatorState.Phase = OperatorGamePhase.Setup;
            operatorState.Context = context;
            fsm.TransitionTo(context, new KnockBox.Operator.Services.Logic.FSM.States.SetupState());
            
            // 4. Initialize Turn Manager
            operatorState.TurnManager.SetTurnOrder(allParticipants.Select(p => p.Id));
            
            // 5. Update Joinable Status
            operatorState.UpdateJoinableStatus(false);
            
            return ValueTask.CompletedTask;
        }, ct);
    }

    /// <summary>
    /// Processes a game command by delegating to the current FSM state.
    /// </summary>
    public Task<Result> ExecuteCommandAsync(OperatorGameState state, OperatorCommand command)
    {
        if (state.Context?.Fsm == null)
            return Task.FromResult(Result.FromError("FSM not initialized."));

        var result = state.Execute(() =>
        {
            var fsmResult = state.Context.Fsm.HandleCommand(state.Context, command);
            if (fsmResult.TryGetFailure(out var err))
            {
                return Result.FromError(err.PublicMessage, err.InternalMessage);
            }
            return Result.Success;
        });

        if (!result.IsSuccess) return Task.FromResult<Result>(result.Error.Error);
        return Task.FromResult(result.Value);
    }

    /// <summary>
    /// Drives time-based transitions.
    /// </summary>
    public Result Tick(OperatorGameContext context, DateTimeOffset now)
    {
        if (context.Fsm == null) return Result.Success;

        var executeResult = context.State.Execute(() =>
        {
            var fsmResult = context.Fsm.Tick(context, now);
            if (fsmResult.TryGetFailure(out var err))
            {
                return Result.FromError(err.PublicMessage, err.InternalMessage);
            }
            return Result.Success;
        });

        if (!executeResult.IsSuccess) return executeResult.Error.Error;
        return executeResult.Value;
    }
}
