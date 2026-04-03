using System;
using System.Linq;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
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
            if (setupCommand.Choice != 10m && setupCommand.Choice != -10m)
            {
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid choice. Must be 10.0 or -10.0.");
            }

            if (!context.GamePlayers.TryGetValue(setupCommand.PlayerId, out var playerState))
            {
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
            }

            playerState.CurrentPoints = setupCommand.Choice;
            playerState.ActiveOperator = setupCommand.Choice > 0 ? CardOperator.Add : CardOperator.Subtract;
            playerState.ScoreTimestamp = DateTimeOffset.UtcNow;

            // Check if everyone chose
            if (context.GamePlayers.Values.All(p => p.CurrentPoints == 10m || p.CurrentPoints == -10m))
            {
                // Deal cards
                context.State.Deck = OperatorGameContext.GenerateDeck(context.GamePlayers.Count);
                foreach (var player in context.GamePlayers.Values)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var card = context.State.Deck[0];
                        context.State.Deck.RemoveAt(0);
                        player.Hand.Add(card);
                    }
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
        if (!context.State.Config.TimersEnabled) return ValueResult<TimeSpan>.FromValue(TimeSpan.MaxValue);
        var elapsed = now - context.State.StateStartTime;
        var remaining = context.State.Config.SetupPhaseTimeout - elapsed;
        return ValueResult<TimeSpan>.FromValue(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> Tick(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Config.TimersEnabled) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);

        var elapsed = now - context.State.StateStartTime;
        if (elapsed >= context.State.Config.SetupPhaseTimeout)
        {
            foreach (var p in context.GamePlayers.Values)
            {
                if (p.CurrentPoints == 0m)
                {
                    p.CurrentPoints = 10m;
                    p.ScoreTimestamp = DateTimeOffset.UtcNow;
                }
            }

            if (context.GamePlayers.Values.All(p => p.CurrentPoints == 10m || p.CurrentPoints == -10m))
            {
                context.State.Deck = OperatorGameContext.GenerateDeck(context.GamePlayers.Count);
                foreach (var player in context.GamePlayers.Values)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var card = context.State.Deck[0];
                        context.State.Deck.RemoveAt(0);
                        player.Hand.Add(card);
                    }
                }

                context.State.TurnManager.SetTurnOrder(context.GamePlayers.Keys);
                context.State.Phase = OperatorGamePhase.Play;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new PlayPhaseState());
            }
        }
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }
}
