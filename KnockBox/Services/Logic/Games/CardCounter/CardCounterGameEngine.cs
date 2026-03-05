using KnockBox.Extensions.Collections;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.CardCounter.FSM;
using KnockBox.Services.Logic.Games.CardCounter.FSM.States;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.CardCounter
{
    /// <summary>
    /// Server-authoritative, event-driven FSM engine for Card Counter.
    /// The engine is a singleton; all mutable game state lives in
    /// <see cref="CardCounterGameState"/> (and its <see cref="CardCounterGameContext"/>),
    /// which is created per game session.
    /// </summary>
    public class CardCounterGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<CardCounterGameEngine> logger,
        ILogger<CardCounterGameState> stateLogger) : AbstractGameEngine
    {
        // ── AbstractGameEngine lifecycle ─────────────────────────────────────

        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.", $"Parameter {nameof(host)} was null.");

            var gameState = new CardCounterGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            logger.LogInformation("Created CardCounter state with host [{id}].", host.Id);
            return gameState;
        }

        public override async Task<Result> StartAsync(
            User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not CardCounterGameState gameState)
                return Result.FromError(
                    "Error starting game.",
                    $"State type [{state?.GetType().Name}] cannot be cast to [{nameof(CardCounterGameState)}].");

            if (host != gameState.Host)
                return Result.FromError("Only the host can start the game.");

            if (gameState.Players.Count == 0)
                return Result.FromError("At least one other player must join before starting the game.");

            var context = new CardCounterGameContext(gameState, randomNumberService, logger);

            var executeResult = gameState.Execute(() =>
            {
                gameState.UpdateJoinableStatus(false);
                gameState.Context = context;
                InitializeGame(context);
            });

            if (executeResult.IsFailure) return executeResult;

            // Transition into the initial FSM state (BuyIn)
            TransitionTo(context, new BuyInState());
            return Result.Success;
        }

        // ── FSM core ─────────────────────────────────────────────────────────

        /// <summary>
        /// Processes a player command by delegating to the current FSM state inside the
        /// game's execute lock. State transitions are handled automatically.
        /// </summary>
        public Result ProcessCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            return context.State.Execute(() =>
            {
                var next = context.CurrentFsmState.HandleCommand(context, command);
                if (next is not null) TransitionTo(context, next);
            });
        }

        /// <summary>
        /// Drives time-based transitions (e.g., action-response timeouts).
        /// Call periodically from a timer or background service.
        /// </summary>
        public Result Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            return context.State.Execute(() =>
            {
                var next = context.CurrentFsmState.Tick(context, now);
                if (next is not null) TransitionTo(context, next);
            });
        }

        private static void TransitionTo(CardCounterGameContext context, ICardCounterGameState next)
        {
            context.CurrentFsmState = next;
            next.OnEnter(context);
        }

        // ── Public UI-facing methods ─────────────────────────────────────────

        /// <summary>Sets a player's buy-in sign (positive or negative).</summary>
        public Result SetBuyIn(User player, CardCounterGameState state, bool isNegative)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SetBuyInCommand(player.Id, isNegative));
        }

        /// <summary>Active player draws the top card from the current shoe.</summary>
        public Result DrawCard(User player, CardCounterGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new DrawCardCommand(player.Id));
        }

        /// <summary>Active player passes their draw (costs one pass).</summary>
        public Result PassTurn(User player, CardCounterGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new PassTurnCommand(player.Id));
        }

        /// <summary>Active player folds their pot (costs one pass; turn continues).</summary>
        public Result FoldPot(User player, CardCounterGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new FoldPotCommand(player.Id));
        }

        /// <summary>Active player plays an action card from their hand.</summary>
        public Result PlayActionCard(User player, CardCounterGameState state, int cardIndex, string? targetPlayerId = null)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new PlayActionCardCommand(player.Id, cardIndex, targetPlayerId));
        }

        /// <summary>Targeted player accepts a pending blockable action without playing Comp'd.</summary>
        public Result AcceptPending(User player, CardCounterGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new AcceptPendingCommand(player.Id));
        }

        /// <summary>
        /// Player submits their chosen card order after a Make My Luck reveal.
        /// </summary>
        public Result SubmitReorder(User player, CardCounterGameState state, int[] reorderedIndices)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SubmitReorderCommand(player.Id, reorderedIndices));
        }

        /// <summary>
        /// Player discards action cards from their hand when over the hand limit.
        /// </summary>
        public Result DiscardActionCards(User player, CardCounterGameState state, int[] cardIndices)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new DiscardActionCardsCommand(player.Id, cardIndices));
        }

        /// <summary>
        /// Active player selects which digits to swap during a Skim action.
        /// </summary>
        public Result SkimSelect(User player, CardCounterGameState state, int sourceDigitIndex, int targetDigitIndex)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SkimSelectCommand(player.Id, sourceDigitIndex, targetDigitIndex));
        }

        /// <summary>
        /// Active player selects the target for a Not My Money operator redirect.
        /// </summary>
        public Result NotMyMoneySelectTarget(User player, CardCounterGameState state, string targetPlayerId)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new NotMyMoneySelectTargetCommand(player.Id, targetPlayerId));
        }

        /// <summary>
        /// Active player cancels a pending Not My Money redirect (operator applies to self).
        /// </summary>
        public Result NotMyMoneyCancel(User player, CardCounterGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new NotMyMoneyCancelCommand(player.Id));
        }

        /// <summary>
        /// Resets the game so another round can be played with the same players.
        /// Only the host can trigger a reset.
        /// </summary>
        public Result ResetGame(User host, CardCounterGameState state)
        {
            if (state.Host.Id != host.Id)
                return Result.FromError("Only the host can reset the game.");

            if (state.GamePhase != GamePhase.GameOver)
                return Result.FromError("Can only reset after the game is over.");

            return state.Execute(() =>
            {
                // Create a fresh context and re-run initialization
                var context = new CardCounterGameContext(state, randomNumberService, logger);
                state.Context = context;
                state.DiscardHistory.Clear();
                state.MainDeck.Clear();
                state.CurrentShoe.Clear();
                state.DiscardPile.Clear();
                state.LastPlayedAction = null;
                state.LastDrawnCard = null;
                state.PendingReaction = null;
                state.FeelingLuckyTargetId = null;
                state.NotMyMoneyPending = false;
                state.IsNotMyMoneySelecting = false;
                state.ForceDrawStack.Clear();
                InitializeGame(context);
                TransitionTo(context, new BuyInState());
            });
        }

        // ── Initialisation helpers ────────────────────────────────────────────

        private void InitializeGame(CardCounterGameContext context)
        {
            var state = context.State;
            state.GamePlayers.Clear();
            state.TurnOrder.Clear();
            state.CurrentPlayerIndex = 0;
            state.ShoeIndex = 0;

            // Register every non-host player
            foreach (var user in state.Players)
            {
                var ps = new PlayerState
                {
                    PlayerId = user.Id,
                    DisplayName = user.Name,
                    PassesRemaining = state.Config.TotalPassesPerPlayer,
                    BuyInRoll = randomNumberService.GetRandomInt(1, 7, RandomType.Fast)
                };

                state.GamePlayers[user.Id] = ps;
                state.TurnOrder.Add(user.Id);
            }

            BuildAndShuffleDeck(context);
        }

        private void BuildAndShuffleDeck(CardCounterGameContext context)
        {
            var state = context.State;
            var cfg = state.Config;
            var cards = new List<BaseCard>(cfg.DeckSize);

            int numNumberCards = (int)(cfg.DeckSize * (cfg.NumberToOperatorRatio / (cfg.NumberToOperatorRatio + 1)));
            int numOpCards = cfg.DeckSize - numNumberCards;

            for (int i = 0; i < numNumberCards; i++)
                cards.Add(new NumberCard(i % 10));

            int addSubCards = (int)(numOpCards * (cfg.AddSubToMulDivRatio / (cfg.AddSubToMulDivRatio + 1)));
            int mulDivCards = numOpCards - addSubCards;

            for (int i = 0; i < addSubCards; i++)
                cards.Add(new OperatorCard(i % 2 == 0 ? Operator.Add : Operator.Subtract));

            for (int i = 0; i < mulDivCards; i++)
                cards.Add(new OperatorCard(i % 2 == 0 ? Operator.Multiply : Operator.Divide));

            Shuffle(cards);
            state.MainDeck.Clear();
            state.MainDeck.PushRange(cards);
        }

        private void Shuffle<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = randomNumberService.GetRandomInt(0, n + 1, RandomType.Secure);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static bool TryGetContext(
            CardCounterGameState state,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CardCounterGameContext? context,
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
