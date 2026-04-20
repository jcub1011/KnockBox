using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Codeword.Services.Logic.Games.FSM;
using KnockBox.Codeword.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Codeword.Services.Logic.Games
{
    /// <summary>
    /// Server-authoritative, event-driven FSM engine for Consult the Card.
    /// The engine is a singleton; all mutable game state lives in
    /// <see cref="CodewordGameState"/> (and its <see cref="CodewordGameContext"/>),
    /// which is created per game session.
    /// </summary>
    public class CodewordGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<CodewordGameEngine> logger,
        ILogger<CodewordGameState> stateLogger) : AbstractGameEngine(minPlayerCount: 4, maxPlayerCount: 8)
    {
        // ── AbstractGameEngine lifecycle ─────────────────────────────────────

        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new CodewordGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState);
            logger.LogInformation("Created Codeword state with host [{id}].", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        public override Task<Result> StartAsync(
            User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not CodewordGameState gameState)
                return Task.FromResult(Result.FromError(
                    "Error starting game.",
                    $"State type [{state?.GetType().Name}] cannot be cast to [{nameof(CodewordGameState)}]."));

            if (host != gameState.Host)
                return Task.FromResult(Result.FromError("Only the host can start the game."));

            var context = new CodewordGameContext(gameState, randomNumberService, logger);
            var fsm = new FiniteStateMachine<CodewordGameContext, CodewordCommand>(logger);
            context.Fsm = fsm;

            var executeResult = gameState.Execute(() =>
            {
                gameState.UpdateJoinableStatus(false);
                gameState.Context = context;

                // Snapshot all registered players into GamePlayers.
                foreach (var user in gameState.Players)
                {
                    gameState.GamePlayers[user.Id] = new CodewordPlayerState
                    {
                        PlayerId = user.Id,
                        DisplayName = user.Name
                    };
                    gameState.TurnManager.TurnOrder.Add(user.Id);
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
        internal Result ProcessCommand(CodewordGameContext context, CodewordCommand command)
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
        /// Always delegates to the FSM regardless of <see cref="CodewordGameConfig.EnableTimers"/>
        /// (individual FSM states check the flag themselves).
        /// </summary>
        public Result Tick(CodewordGameContext context, DateTimeOffset now)
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
        public Result SubmitClue(User player, CodewordGameState state, string clue)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SubmitClueCommand(player.Id, clue));
        }

        /// <summary>Player casts a vote to eliminate the targeted player (not yet locked in).</summary>
        public Result CastVote(User player, CodewordGameState state, string targetPlayerId)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new CastVoteCommand(player.Id, targetPlayerId));
        }

        /// <summary>Player locks in their selected vote.</summary>
        public Result LockInVote(User player, CodewordGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new LockInVoteCommand(player.Id));
        }

        /// <summary>Informant guesses the Agent word during the reveal phase.</summary>
        public Result InformantGuess(User player, CodewordGameState state, string guessedWord)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new InformantGuessCommand(player.Id, guessedWord));
        }

        /// <summary>Host advances the game from discussion to the voting phase.</summary>
        public Result AdvanceToVote(User player, CodewordGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new AdvanceToVoteCommand(player.Id));
        }

        /// <summary>Host or player skips the remaining discussion time.</summary>
        public Result SkipRemainingTime(User player, CodewordGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SkipRemainingTimeCommand(player.Id));
        }

        /// <summary>Any player votes to end the current game (once per elimination cycle).</summary>
        public Result VoteToEndGame(User player, CodewordGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new VoteToEndGameCommand(player.Id));
        }

        /// <summary>Host starts the next game in a multi-game session.</summary>
        public Result StartNextGame(User player, CodewordGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new StartNextGameCommand(player.Id));
        }

        /// <summary>
        /// Returns the game to the lobby so players can join/leave and settings can be changed.
        /// Only the host can trigger this.
        /// </summary>
        public Result ReturnToLobby(User host, CodewordGameState state)
        {
            if (state.Host.Id != host.Id)
                return Result.FromError("Only the host can return the game to the lobby.");

            return state.Execute(() =>
            {
                state.Context = null;
                state.GamePlayers.Clear();
                state.TurnManager.TurnOrder.Clear();
                state.TurnManager.SetCurrentPlayerIndex(0);
                state.CurrentEliminationCycle = 0;
                state.CurrentWordPair = null;
                state.CurrentRoundClues.Clear();
                state.CurrentRoundVotes.Clear();
                state.UsedClues.Clear();
                state.LastElimination = null;
                state.LastInformantGuess = null;
                state.AwaitingInformantGuess = false;
                state.WinResult = null;
                state.CurrentGameNumber = 1;
                state.EndGameVoteStatus = new EndGameVoteStatus([], 0);
                state.GameScores.Clear();
                state.UpdateJoinableStatus(true);
            });
        }

        /// <summary>
        /// Resets the game so another round can be played with the same players.
        /// Only the host can trigger a reset.
        /// </summary>
        public Result ResetGame(User host, CodewordGameState state)
        {
            if (state.Host.Id != host.Id)
                return Result.FromError("Only the host can reset the game.");

            return state.Execute(() =>
            {
                // Create a fresh context and FSM.
                var context = new CodewordGameContext(state, randomNumberService, logger);
                var fsm = new FiniteStateMachine<CodewordGameContext, CodewordCommand>(logger);
                context.Fsm = fsm;
                state.Context = context;

                // Clear per-game state.
                state.GamePlayers.Clear();
                state.TurnManager.TurnOrder.Clear();
                state.TurnManager.SetCurrentPlayerIndex(0);
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
                    state.GamePlayers[user.Id] = new CodewordPlayerState
                    {
                        PlayerId = user.Id,
                        DisplayName = user.Name
                    };
                    state.TurnManager.TurnOrder.Add(user.Id);
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
        internal void HandlePlayerLeft(User player, CodewordGameState state)
        {
            // If the game hasn't been started yet (no context) there is no game state to fix.
            if (state.Context is null || state.IsDisposed) return;

            var context = state.Context;

            state.Execute(() =>
            {
                int leftIndex = state.TurnManager.TurnOrder.IndexOf(player.Id);

                // Remove from turn order.
                if (leftIndex >= 0)
                    state.TurnManager.TurnOrder.RemoveAt(leftIndex);

                // Mark as eliminated.
                var playerState = context.GetPlayer(player.Id);
                if (playerState is not null)
                    playerState.IsEliminated = true;

                // Adjust CurrentPlayerIndex if needed.
                if (leftIndex >= 0 && state.TurnManager.TurnOrder.Count > 0)
                {
                    if (leftIndex < state.TurnManager.CurrentPlayerIndex)
                    {
                        state.TurnManager.SetCurrentPlayerIndex(state.TurnManager.CurrentPlayerIndex - 1);
                    }
                    else if (leftIndex == state.TurnManager.CurrentPlayerIndex
                             && state.TurnManager.CurrentPlayerIndex >= state.TurnManager.TurnOrder.Count)
                    {
                        state.TurnManager.SetCurrentPlayerIndex(0);
                    }
                }
                else if (state.TurnManager.TurnOrder.Count == 0)
                {
                    state.TurnManager.SetCurrentPlayerIndex(0);
                }

                logger.LogInformation(
                    "Player [{id}] left the game. TurnOrder now has {n} player(s).",
                    player.Id, state.TurnManager.TurnOrder.Count);

                // If during VotePhase or Discussion: void any votes cast for the disconnected player
                // and remove the disconnected player's own outbound vote.
                if (state.Phase == CodewordGamePhase.Voting || state.Phase == CodewordGamePhase.Discussion)
                {
                    foreach (var ps in context.GetAlivePlayers())
                    {
                        if (ps.VoteTargetId == player.Id)
                        {
                            ps.VoteTargetId = null;
                            ps.HasVoted = false;
                            logger.LogDebug(
                                "HandlePlayerLeft: voided vote from [{voter}] targeting disconnected [{target}].",
                                ps.PlayerId, player.Id);
                        }
                    }

                    // Remove the disconnected player's own vote entry and state.
                    state.CurrentRoundVotes.RemoveAll(v => v.VoterId == player.Id);
                    if (playerState is not null)
                    {
                        playerState.HasVoted = false;
                        playerState.VoteTargetId = null;
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
                if (state.Phase == CodewordGamePhase.CluePhase
                    && leftIndex >= 0)
                {
                    // The current clue giver index may now point to the next player;
                    // re-enter CluePhaseState to skip to the next alive player.
                    context.Fsm.TransitionTo(context, new CluePhaseState());
                    return;
                }

                // Auto-advance if during Vote/Discussion Phase and all remaining alive players have voted.
                if ((state.Phase == CodewordGamePhase.Voting || state.Phase == CodewordGamePhase.Discussion)
                    && context.GetAlivePlayers().All(p => p.HasVoted))
                {
                    // All votes are in — process the vote result by re-entering the FSM.
                    if (state.Phase == CodewordGamePhase.Voting)
                        context.Fsm.TransitionTo(context, new VotePhaseState());
                    else
                        context.Fsm.TransitionTo(context, new DiscussionPhaseState());
                    return;
                }
            });
        }

        // ── Utility ──────────────────────────────────────────────────────────

        private static bool TryGetContext(
            CodewordGameState state,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CodewordGameContext? context,
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
