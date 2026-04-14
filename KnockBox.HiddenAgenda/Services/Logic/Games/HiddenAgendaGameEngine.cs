using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Users;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.Extensions.Logging;

namespace KnockBox.HiddenAgenda.Services.Logic.Games
{
    public class HiddenAgendaGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<HiddenAgendaGameEngine> logger,
        ILogger<HiddenAgendaGameState> stateLogger) 
        : AbstractGameEngine(minPlayerCount: 3, maxPlayerCount: 6)
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new HiddenAgendaGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState);
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        public override Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not HiddenAgendaGameState gameState)
                return Task.FromResult(Result.FromError("Error starting game.", $"Game state of type [{(state?.GetType().Name ?? "null")}] couldn't be cast to type [{nameof(HiddenAgendaGameState)}]."));

            if (host != gameState.Host)
                return Task.FromResult(Result.FromError("Only the host can start the game."));

            var context = new HiddenAgendaGameContext(gameState, randomNumberService, logger);
            var fsm = new FiniteStateMachine<HiddenAgendaGameContext, HiddenAgendaCommand>(logger);
            context.Fsm = fsm;

            var executeResult = gameState.Execute(() =>
            {
                gameState.UpdateJoinableStatus(false);
                gameState.Context = context;
                
                // Initialize board
                gameState.BoardGraph = BoardDefinitions.CreateGrandCircuit();
                
                // Initialize players
                foreach (var user in gameState.Players)
                {
                    var playerState = new HiddenAgendaPlayerState 
                    { 
                        PlayerId = user.Id, 
                        DisplayName = user.Name,
                        CurrentSpaceId = 0 // Start at Grand Hall Foyer
                    };
                    gameState.GamePlayers[user.Id] = playerState;
                    gameState.TurnManager.TurnOrder.Add(user.Id);
                }

                fsm.TransitionTo(context, new RoundSetupState());
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }

        internal Result ProcessCommand(HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            var executeResult = context.State.Execute(() =>
            {
                var fsmResult = context.Fsm.HandleCommand(context, command);
                if (fsmResult.TryGetFailure(out var err))
                    return Result.FromError(err.PublicMessage, err.InternalMessage);
                return Result.Success;
            });
            if (!executeResult.IsSuccess) return executeResult.Error.Error;
            return executeResult.Value;
        }

        public Result Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            var executeResult = context.State.Execute(() =>
            {
                var fsmResult = context.Fsm.Tick(context, now);
                if (fsmResult.TryGetFailure(out var err))
                    return Result.FromError(err.PublicMessage, err.InternalMessage);
                return Result.Success;
            });
            if (!executeResult.IsSuccess) return executeResult.Error.Error;
            return executeResult.Value;
        }

        private bool TryGetContext(HiddenAgendaGameState state, out HiddenAgendaGameContext context, out Result error)
        {
            context = state.Context!;
            if (context == null)
            {
                error = Result.FromError("Game context not found.");
                return false;
            }
            error = Result.Success;
            return true;
        }

        // UI-facing methods
        public Result Spin(User player, HiddenAgendaGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SpinCommand(player.Id));
        }

        public Result SelectDestination(User player, HiddenAgendaGameState state, int destinationSpaceId)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SelectDestinationCommand(player.Id, destinationSpaceId));
        }

        public Result SelectCurationCard(User player, HiddenAgendaGameState state, int cardIndex)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SelectCurationCardCommand(player.Id, cardIndex));
        }

        public Result SelectTradeOption(User player, HiddenAgendaGameState state, bool useAlternate)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SelectTradeOptionCommand(player.Id, useAlternate));
        }

        public Result PlayCatalog(User player, HiddenAgendaGameState state, string targetPlayerId)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new PlayCatalogCommand(player.Id, targetPlayerId));
        }

        public Result PlayDetour(User player, HiddenAgendaGameState state, string targetPlayerId)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new PlayDetourCommand(player.Id, targetPlayerId));
        }

        public Result SkipEventCard(User player, HiddenAgendaGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SkipEventCardCommand(player.Id));
        }

        public Result SelectEventCardAction(User player, HiddenAgendaGameState state, bool keepNewCard)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SelectEventCardActionCommand(player.Id, keepNewCard));
        }

        public Result SubmitGuess(User player, HiddenAgendaGameState state, Dictionary<string, List<string>> guesses)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SubmitGuessCommand(player.Id, guesses));
        }

        public Result SkipGuess(User player, HiddenAgendaGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SkipGuessCommand(player.Id));
        }

        public Result SubmitFinalGuess(User player, HiddenAgendaGameState state, Dictionary<string, List<string>> guesses)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SubmitFinalGuessCommand(player.Id, guesses));
        }

        public Result SkipFinalGuess(User player, HiddenAgendaGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SkipFinalGuessCommand(player.Id));
        }

        public Result StartNextRound(User player, HiddenAgendaGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new StartNextRoundCommand(player.Id));
        }

        public Result ReturnToLobby(User player, HiddenAgendaGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            var result = ProcessCommand(ctx, new ReturnToLobbyCommand(player.Id));
            // Dispose outside the Execute lock -- the command sets phase to Lobby as a signal
            if (result.IsSuccess && state.Phase == GamePhase.Lobby)
                state.Dispose();
            return result;
        }

        public Result PlayAgain(User player, HiddenAgendaGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new PlayAgainCommand(player.Id));
        }

        internal void HandlePlayerLeft(User player, HiddenAgendaGameState state)
        {
            logger.LogInformation("Player [{playerId}] left the game.", player.Id);
            if (state.Context == null) return;

            state.Execute(() =>
            {
                bool wasCurrentPlayer = state.TurnManager.CurrentPlayer == player.Id;
                state.TurnManager.TurnOrder.Remove(player.Id);
                state.GamePlayers.TryRemove(player.Id, out _);

                if (wasCurrentPlayer && state.TurnManager.TurnOrder.Count > 0)
                {
                    state.TurnManager.NextTurn();
                    state.Context.Fsm.TransitionTo(state.Context, new EventCardPhaseState());
                }
            });
        }
    }
}
