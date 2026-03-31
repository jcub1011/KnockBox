using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.ConsultTheCard.FSM;
using KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.ConsultTheCard
{
    /// <summary>
    /// Server-authoritative, event-driven FSM engine for Consult the Card.
    /// The engine is a singleton; all mutable game state lives in
    /// <see cref="ConsultTheCardGameState"/> (and its <see cref="ConsultTheCardGameContext"/>),
    /// which is created per game session.
    /// </summary>
    public class ConsultTheCardGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<ConsultTheCardGameEngine> logger,
        ILogger<ConsultTheCardGameState> stateLogger) : AbstractGameEngine(minPlayerCount: 4, maxPlayerCount: 8)
    {
        // ── AbstractGameEngine lifecycle ─────────────────────────────────────

        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new ConsultTheCardGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState);
            logger.LogInformation("Created ConsultTheCard state with host [{id}].", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        public override Task<Result> StartAsync(
            User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not ConsultTheCardGameState gameState)
                return Task.FromResult(Result.FromError(
                    "Error starting game.",
                    $"State type [{state?.GetType().Name}] cannot be cast to [{nameof(ConsultTheCardGameState)}]."));

            if (host != gameState.Host)
                return Task.FromResult(Result.FromError("Only the host can start the game."));

            var context = new ConsultTheCardGameContext(gameState, randomNumberService, logger);
            var fsm = new FiniteStateMachine<ConsultTheCardGameContext, ConsultTheCardCommand>(logger);
            context.Fsm = fsm;

            var executeResult = gameState.Execute(() =>
            {
                gameState.UpdateJoinableStatus(false);
                gameState.Context = context;

                // Snapshot all registered players into GamePlayers.
                foreach (var user in gameState.Players)
                {
                    gameState.GamePlayers[user.Id] = new ConsultTheCardPlayerState
                    {
                        PlayerId = user.Id,
                        DisplayName = user.Name
                    };
                    gameState.TurnOrder.Add(user.Id);
                }

                fsm.TransitionTo(context, new SetupState());
            });

            if (executeResult.IsFailure)
                return Task.FromResult<Result>(executeResult.Error.Error);

            return Task.FromResult(Result.Success);
        }

        // ── FSM core ─────────────────────────────────────────────────────────

        /// <summary>
        /// Processes a player command by delegating to the current FSM state inside the
        /// game's execute lock. State transitions are handled automatically.
        /// </summary>
        internal Result ProcessCommand(ConsultTheCardGameContext context, ConsultTheCardCommand command)
        {
            var executeResult = context.State.Execute(() =>
            {
                var fsmResult = context.Fsm.HandleCommand(context, command);
                if (fsmResult.TryGetFailure(out var err))
                {
                    logger.LogError("FSM command error: {msg}", err.PublicMessage);
                    return Result.FromError(err.PublicMessage, err.InternalMessage);
                }
                return Result.Success;
            });

            if (!executeResult.IsSuccess) return executeResult.Error.Error;
            return executeResult.Value;
        }

        /// <summary>
        /// Drives time-based transitions. Call periodically from a timer or background service.
        /// Always delegates to the FSM regardless of <see cref="ConsultTheCardGameConfig.EnableTimers"/>
        /// (individual FSM states check the flag themselves).
        /// </summary>
        public Result Tick(ConsultTheCardGameContext context, DateTimeOffset now)
        {
            var executeResult = context.State.Execute(() =>
            {
                var fsmResult = context.Fsm.Tick(context, now);
                if (fsmResult.TryGetFailure(out var err))
                {
                    logger.LogError("FSM tick error: {msg}", err.PublicMessage);
                    return Result.FromError(err.PublicMessage, err.InternalMessage);
                }
                return Result.Success;
            });

            if (!executeResult.IsSuccess) return executeResult.Error.Error;
            return executeResult.Value;
        }

        // ── Public UI-facing methods ─────────────────────────────────────────

        /// <summary>Player submits a one-word clue during the clue phase.</summary>
        public Result SubmitClue(User player, ConsultTheCardGameState state, string clue)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SubmitClueCommand(player.Id, clue));
        }

        /// <summary>Player casts a vote to eliminate the targeted player.</summary>
        public Result CastVote(User player, ConsultTheCardGameState state, string targetPlayerId)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new CastVoteCommand(player.Id, targetPlayerId));
        }

        /// <summary>Informant guesses the Agent word during the reveal phase.</summary>
        public Result InformantGuess(User player, ConsultTheCardGameState state, string guessedWord)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new InformantGuessCommand(player.Id, guessedWord));
        }

        /// <summary>Host advances the game from discussion to the voting phase.</summary>
        public Result AdvanceToVote(User player, ConsultTheCardGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new AdvanceToVoteCommand(player.Id));
        }

        /// <summary>Any player votes to end the current game (once per elimination cycle).</summary>
        public Result VoteToEndGame(User player, ConsultTheCardGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new VoteToEndGameCommand(player.Id));
        }

        /// <summary>Host starts the next game in a multi-game session.</summary>
        public Result StartNextGame(User player, ConsultTheCardGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new StartNextGameCommand(player.Id));
        }

        /// <summary>
        /// Returns the game to the lobby so players can join/leave and settings can be changed.
        /// Only the host can trigger this.
        /// </summary>
        public Result ReturnToLobby(User host, ConsultTheCardGameState state)
        {
            if (state.Host.Id != host.Id)
                return Result.FromError("Only the host can return the game to the lobby.");

            return state.Execute(() =>
            {
                state.Context = null;
                state.GamePlayers.Clear();
                state.TurnOrder.Clear();
                state.CurrentCluePlayerIndex = 0;
                state.CurrentEliminationCycle = 0;
                state.CurrentWordPair = null;
                state.CurrentRoundClues.Clear();
                state.CurrentRoundVotes.Clear();
                state.UsedClues.Clear();
                state.LastElimination = null;
                state.LastInformantGuess = null;
                state.AwaitingInformantGuess = false;
                state.WinResult = null;
                state.EndGameVoteStatus = new EndGameVoteStatus([], 0);
                state.GameScores.Clear();
                state.UpdateJoinableStatus(true);
            });
        }

        /// <summary>
        /// Resets the game so another round can be played with the same players.
        /// Only the host can trigger a reset.
        /// </summary>
        public Result ResetGame(User host, ConsultTheCardGameState state)
        {
            if (state.Host.Id != host.Id)
                return Result.FromError("Only the host can reset the game.");

            return state.Execute(() =>
            {
                // Create a fresh context and FSM.
                var context = new ConsultTheCardGameContext(state, randomNumberService, logger);
                var fsm = new FiniteStateMachine<ConsultTheCardGameContext, ConsultTheCardCommand>(logger);
                context.Fsm = fsm;
                state.Context = context;

                // Clear per-game state.
                state.GamePlayers.Clear();
                state.TurnOrder.Clear();
                state.CurrentCluePlayerIndex = 0;
                state.CurrentEliminationCycle = 0;
                state.CurrentWordPair = null;
                state.CurrentRoundClues.Clear();
                state.CurrentRoundVotes.Clear();
                state.UsedClues.Clear();
                state.LastElimination = null;
                state.LastInformantGuess = null;
                state.AwaitingInformantGuess = false;
                state.WinResult = null;
                state.EndGameVoteStatus = new EndGameVoteStatus([], 0);

                // Re-snapshot players.
                foreach (var user in state.Players)
                {
                    state.GamePlayers[user.Id] = new ConsultTheCardPlayerState
                    {
                        PlayerId = user.Id,
                        DisplayName = user.Name
                    };
                    state.TurnOrder.Add(user.Id);
                }

                fsm.TransitionTo(context, new SetupState());
            });
        }

        // ── Player-leave handling ────────────────────────────────────────────

        /// <summary>
        /// Called whenever a player unregisters from the game (disconnect, tab close, or kick).
        /// Removes the player from the turn order, marks them as eliminated, adjusts indices,
        /// voids votes targeting the disconnected player, checks win conditions, and
        /// auto-advances if needed.
        /// </summary>
        internal void HandlePlayerLeft(User player, ConsultTheCardGameState state)
        {
            // If the game hasn't been started yet (no context) there is no game state to fix.
            if (state.Context is null || state.IsDisposed) return;

            var context = state.Context;

            state.Execute(() =>
            {
                int leftIndex = state.TurnOrder.IndexOf(player.Id);

                // Remove from turn order.
                if (leftIndex >= 0)
                    state.TurnOrder.RemoveAt(leftIndex);

                // Mark as eliminated.
                var playerState = context.GetPlayer(player.Id);
                if (playerState is not null)
                    playerState.IsEliminated = true;

                // Adjust CurrentCluePlayerIndex if needed.
                if (leftIndex >= 0 && state.TurnOrder.Count > 0)
                {
                    if (leftIndex < state.CurrentCluePlayerIndex)
                    {
                        state.CurrentCluePlayerIndex--;
                    }
                    else if (leftIndex == state.CurrentCluePlayerIndex
                             && state.CurrentCluePlayerIndex >= state.TurnOrder.Count)
                    {
                        state.CurrentCluePlayerIndex = 0;
                    }
                }
                else if (state.TurnOrder.Count == 0)
                {
                    state.CurrentCluePlayerIndex = 0;
                }

                logger.LogInformation(
                    "Player [{id}] left the game. TurnOrder now has {n} player(s).",
                    player.Id, state.TurnOrder.Count);

                // If during VotePhase: void any votes cast for the disconnected player.
                if (state.GamePhase == ConsultTheCardGamePhase.Voting)
                {
                    foreach (var ps in context.GetAlivePlayers())
                    {
                        if (ps.VoteTargetId == player.Id)
                        {
                            ps.VoteTargetId = null;
                            ps.HasVoted = false;
                            logger.LogInformation(
                                "HandlePlayerLeft: voided vote from [{voter}] targeting disconnected [{target}].",
                                ps.PlayerId, player.Id);
                        }
                    }
                }

                // Check win conditions.
                var winResult = context.CheckWinConditions();
                if (winResult.GameOver)
                {
                    state.WinResult = winResult;
                    context.Fsm.TransitionTo(context, new GameOverState());
                    return;
                }

                // Auto-advance if the leaving player was the current clue giver during CluePhase.
                if (state.GamePhase == ConsultTheCardGamePhase.CluePhase
                    && leftIndex >= 0)
                {
                    // The current clue giver index may now point to the next player;
                    // re-enter CluePhaseState to skip to the next alive player.
                    context.Fsm.TransitionTo(context, new CluePhaseState());
                    return;
                }

                // Auto-advance if during VotePhase and all remaining alive players have voted.
                if (state.GamePhase == ConsultTheCardGamePhase.Voting
                    && context.GetAlivePlayers().All(p => p.HasVoted))
                {
                    // All votes are in — process the vote result by re-entering the FSM.
                    // The VotePhaseState checks for all voted on command handling,
                    // but since we voided votes, we re-trigger via a no-op transition.
                    context.Fsm.TransitionTo(context, new VotePhaseState());
                    return;
                }
            });
        }

        // ── Utility ──────────────────────────────────────────────────────────

        private static bool TryGetContext(
            ConsultTheCardGameState state,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ConsultTheCardGameContext? context,
            out Result error)
        {
            if (state.Context is null)
            {
                context = null;
                error = Result.FromError("The game has not been started yet.");
                return false;
            }
            context = state.Context;
            error = default;
            return true;
        }
    }
}
