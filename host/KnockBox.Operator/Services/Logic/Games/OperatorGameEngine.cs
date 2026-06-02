using KnockBox.Core.Primitives.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KnockBox.Operator.Services.Logic.Games;

public class OperatorGameEngine(
    ILogger<OperatorGameEngine> logger,
    ILogger<OperatorGameState> stateLogger,
    IRandomNumberService randomNumberService)
    : AbstractGameEngine<OperatorGameState>(minPlayerCount: 2, maxPlayerCount: int.MaxValue)
{
    /// <summary>
    /// Operator counts the host as a participant when <see cref="OperatorSettings.HostPlays"/>
    /// is on, so readiness is gated on <see cref="AbstractGameState.Participants"/>.<c>Length</c>
    /// rather than the base check's <c>Players.Length</c>. (Start gating is enforced by the
    /// lobby button; this keeps the readiness API correct for any caller that consults it.)
    /// </summary>
    public override Task<bool> CanStartAsync(AbstractGameState state, CancellationToken ct = default)
    {
        int count = state.Participants.Length;
        bool valid = MinPlayerCount <= count && count <= MaxPlayerCount && state.IsJoinable;
        return Task.FromResult(valid);
    }

    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
    {
        if (host is null)
            return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", "Host was null."));

        var state = new OperatorGameState(host, stateLogger);
        state.Context = new OperatorGameContext(state, randomNumberService);
        state.Execute(() => state.SetJoinable(true));
        logger.LogInformation("Created Operator gameState with user [{userId}] as host.", host.Id);
        return Task.FromResult(ValueResult<AbstractGameState>.FromValue(state));
    }

    protected override async Task<Result> StartAsyncCore(OperatorGameState operatorState, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Starting Operator game hosted by user [{hostId}] with {playerCount} player(s).",
            operatorState.Host.Id,
            operatorState.Players.Length);

        var context = new OperatorGameContext(operatorState, randomNumberService);
        var fsm = new FiniteStateMachine<OperatorGameContext, OperatorCommand>(stateLogger);
        context.Fsm = fsm;

        return await operatorState.ExecuteAsync(() =>
        {
            // Fix the host's participation from settings at start time so the snapshot below
            // is self-contained regardless of button ordering. When HostPlays is false,
            // Participants == Players and behavior is unchanged.
            operatorState.SetHostIsParticipant(operatorState.Settings.HostPlays);

            var allParticipants = operatorState.Participants.ToList();

            // Initialize GamePlayers (deck generation and dealing happen in SetupState after choices)
            foreach (var entry in allParticipants)
            {
                var playerState = new OperatorPlayerState { UserId = entry.User.Id };
                operatorState.GamePlayers[entry.User.Id] = playerState;
            }

            // 3. Set Phase to Setup
            operatorState.Phase = OperatorGamePhase.Setup;
            operatorState.Context = context;
            fsm.TransitionTo(context, new KnockBox.Operator.Services.Logic.FSM.States.SetupState());

            // 4. Initialize Turn Manager
            operatorState.TurnManager.SetTurnOrder(allParticipants.Select(p => p.User.Id));

            // 5. Update Joinable Status
            operatorState.SetJoinable(false);

            return ValueTask.CompletedTask;
        }, ct);
    }

    /// <summary>
    /// Returns the game to the lobby (host-only, terminal-phase-only) via the base
    /// <see cref="AbstractGameEngine{TState}.ReturnToLobby"/>. Flipping the state back to
    /// joinable re-renders every player's page at the lobby — no navigation needed.
    /// </summary>
    protected override bool IsTerminalPhase(OperatorGameState state) => state.Phase == OperatorGamePhase.GameOver;

    /// <inheritdoc />
    protected override void ResetForLobby(OperatorGameState state)
    {
        // Fresh context mirrors CreateStateAsync; StartAsyncCore replaces it again on
        // the next start. Keeps the lobby's pre-start invariant (non-null Context).
        state.Context = new OperatorGameContext(state, randomNumberService);
        state.GamePlayers.Clear();
        state.Deck = [];
        state.DiscardPile = [];
        state.ActionLog = [];
        state.LastBlockedActionMessage = null;
        state.BlockedAttackerId = null;
        state.TurnManager.SetTurnOrder([]);
        state.PendingGameActionCommand = null;
        state.ReactionTargetPlayerIds = [];
        state.PlayerReactions = [];
        state.TurnCount = 0;
        state.WinnerPlayerId = null;
        state.Phase = OperatorGamePhase.Setup;
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
