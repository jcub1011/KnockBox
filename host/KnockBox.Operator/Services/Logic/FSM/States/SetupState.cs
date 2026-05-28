using System;
using System.Linq;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.States;

public class SetupState : IOperatorGameState, ITimedGameState<OperatorGameContext, OperatorCommand>
{
    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> OnEnter(OperatorGameContext context)
    {
        context.State.StateStartTime = DateTimeOffset.UtcNow;
        foreach (var state in context.State.GamePlayers.Values)
        {
            state.CurrentPoints = 0m; // reset points
        }
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }

    public Result OnExit(OperatorGameContext context)
    {
        return Result.Success;
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> HandleCommand(OperatorGameContext context, OperatorCommand command)
    {
        if (command is SubmitSetupChoiceCommand setupCommand)
        {
            if (setupCommand.Choice != context.State.Settings.InitialPointsPositive && setupCommand.Choice != context.State.Settings.InitialPointsNegative)
            {
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError($"Invalid choice. Must be {context.State.Settings.InitialPointsPositive} or {context.State.Settings.InitialPointsNegative}.");
            }

            if (!context.GamePlayers.TryGetValue(setupCommand.PlayerId, out var playerState))
            {
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
            }

            playerState.CurrentPoints = setupCommand.Choice;
            playerState.ActiveOperator = setupCommand.Choice > 0 ? CardOperator.Add : CardOperator.Subtract;
            playerState.ScoreTimestamp = DateTimeOffset.UtcNow;

            // Check if everyone chose
            var posPoints = context.State.Settings.InitialPointsPositive;
            var negPoints = context.State.Settings.InitialPointsNegative;
            if (context.GamePlayers.Values.All(p => p.CurrentPoints == posPoints || p.CurrentPoints == negPoints))
            {
                // Deal cards
                context.State.Deck = OperatorGameContext.GenerateDeck(context.GamePlayers.Count, context.Rng);
                foreach (var player in context.GamePlayers.Values)
                {
                    context.DealCards(player, context.State.Settings.MaxHandSize);
                }

                // Initialize TurnManager
                context.State.TurnManager.SetTurnOrder(context.GamePlayers.Keys);
                
                context.State.Phase = OperatorGamePhase.Play;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new PlayPhaseState());
            }

            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid command for SetupPhase.");
    }

    public ValueResult<TimeSpan> GetRemainingTime(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Settings.TimersEnabled) return ValueResult<TimeSpan>.FromValue(TimeSpan.MaxValue);
        var elapsed = now - context.State.StateStartTime;
        var remaining = context.State.Settings.SetupPhaseTimeout - elapsed;
        return ValueResult<TimeSpan>.FromValue(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> Tick(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Settings.TimersEnabled) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);

        var elapsed = now - context.State.StateStartTime;
        if (elapsed >= context.State.Settings.SetupPhaseTimeout)
        {
            foreach (var p in context.GamePlayers.Values)
            {
                if (p.CurrentPoints == 0m)
                {
                    p.CurrentPoints = context.State.Settings.InitialPointsPositive;
                    p.ActiveOperator = CardOperator.Add;
                    p.ScoreTimestamp = DateTimeOffset.UtcNow;
                }
            }

            var posPoints2 = context.State.Settings.InitialPointsPositive;
            var negPoints2 = context.State.Settings.InitialPointsNegative;
            if (context.GamePlayers.Values.All(p => p.CurrentPoints == posPoints2 || p.CurrentPoints == negPoints2))
            {
                context.State.Deck = OperatorGameContext.GenerateDeck(context.GamePlayers.Count, context.Rng);
                foreach (var player in context.GamePlayers.Values)
                {
                    context.DealCards(player, context.State.Settings.MaxHandSize);
                }

                context.State.TurnManager.SetTurnOrder(context.GamePlayers.Keys);
                context.State.Phase = OperatorGamePhase.Play;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new PlayPhaseState());
            }
        }
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }
}
