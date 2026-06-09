using System.Text.Json;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Tooling.Collections;
using KnockBox.Core.Primitives.Returns;
using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.CardCounter.Services.Logic.Games.FSM.States;
using KnockBox.CardCounter.Services.Projection;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.CardCounter.Services.Logic.Games
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
        ILogger<CardCounterGameState> stateLogger)
        : AbstractGameEngine<CardCounterGameState>, IGameStateProjector, IGameCommandHandler, IServerTickHandler
    {
        private readonly CardCounterStateProjector _projector = new();

        // Match the hub's wire format: enums as strings, case-insensitive property
        // names, so a client-serialized command payload deserializes here.
        private static readonly JsonSerializerOptions CommandJsonOptions = new()
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true,
        };

        // ── Hub projection / command / tick surface ──────────────────────────

        /// <summary>Per-recipient projection entry point used by the host's <c>GameViewCoordinator</c>.</summary>
        public object? ProjectFor(AbstractGameState state, Guid recipientId)
            => ((IGameStateProjector)_projector).ProjectFor(state, recipientId);

        /// <summary>
        /// Maps a hub command name to the same engine method a Razor page used to call
        /// directly. Per-command authorization (host-only, active-player, target-only)
        /// lives in the invoked methods / FSM states.
        /// </summary>
        public async ValueTask<Result> HandleCommandAsync(
            User caller, AbstractGameState state, string command, string? payloadJson, CancellationToken ct = default)
        {
            if (state is not CardCounterGameState s)
                return Result.FromError("Invalid game state for Card Counter.");

            return command switch
            {
                CardCounterCommands.Start             => await StartFromPayload(caller, s, payloadJson, ct),
                CardCounterCommands.SetBuyIn          => SetBuyInFromPayload(caller, s, payloadJson),
                CardCounterCommands.DrawCard          => DrawCard(caller, s),
                CardCounterCommands.PassTurn          => PassTurn(caller, s),
                CardCounterCommands.FoldPot           => FoldPot(caller, s),
                CardCounterCommands.PlayAction        => PlayActionFromPayload(caller, s, payloadJson),
                CardCounterCommands.AcceptPending     => AcceptPending(caller, s),
                CardCounterCommands.SubmitReorder     => SubmitReorderFromPayload(caller, s, payloadJson),
                CardCounterCommands.Discard           => DiscardFromPayload(caller, s, payloadJson),
                CardCounterCommands.SkimSelect        => SkimSelectFromPayload(caller, s, payloadJson),
                CardCounterCommands.NotMyMoneyTarget  => NotMyMoneyTargetFromPayload(caller, s, payloadJson),
                CardCounterCommands.NotMyMoneyCancel  => NotMyMoneyCancel(caller, s),
                CardCounterCommands.ReturnToLobby     => ReturnToLobby(caller, s),
                CardCounterCommands.UpdateSettings    => UpdateSettingsFromPayload(caller, s, payloadJson),
                CardCounterCommands.KickPlayer        => KickFromPayload(caller, s, payloadJson),
                _ => Result.FromError($"Unknown command [{command}].")
            };
        }

        /// <summary>Server-owned clock entry point; drives the FSM's time-based transitions.</summary>
        void IServerTickHandler.Tick(AbstractGameState state, DateTimeOffset now)
        {
            if (state is CardCounterGameState s && s.Context is not null)
                Tick(s.Context, now);
        }

        // ── Command payload adapters ─────────────────────────────────────────

        private async Task<Result> StartFromPayload(User caller, CardCounterGameState state, string? payloadJson, CancellationToken ct)
        {
            // The deal buttons choose whether the host plays; carry it into settings
            // before the host-checked StartAsync runs StartAsyncCore.
            var payload = Deserialize<StartPayload>(payloadJson);
            if (caller.Id == state.Host.Id)
                state.UpdateSettings(cfg => cfg with { HostPlays = payload?.HostPlays ?? false });
            return await StartAsync(caller, state, ct);
        }

        private Result SetBuyInFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            if (Deserialize<SetBuyInPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed buy-in payload.");
            return SetBuyIn(caller, state, p.IsNegative);
        }

        private Result PlayActionFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            if (Deserialize<PlayActionPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed play-action payload.");
            return PlayActionCard(caller, state, p.CardIndex, p.TargetPlayerId);
        }

        private Result SubmitReorderFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            if (Deserialize<ReorderPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed reorder payload.");
            return SubmitReorder(caller, state, p.ReorderedIndices);
        }

        private Result DiscardFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            if (Deserialize<DiscardPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed discard payload.");
            return DiscardActionCards(caller, state, p.CardIndices);
        }

        private Result SkimSelectFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            if (Deserialize<SkimPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed skim payload.");
            return SkimSelect(caller, state, p.SourceDigitIndex, p.TargetDigitIndex);
        }

        private Result NotMyMoneyTargetFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            if (Deserialize<TargetPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed target payload.");
            return NotMyMoneySelectTarget(caller, state, p.TargetPlayerId);
        }

        private Result UpdateSettingsFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            // Host-only, and only meaningful before the game starts (the House Rules
            // drawer is a lobby control). HostPlays is owned by the deal buttons, so it
            // is preserved across a settings edit.
            if (caller.Id != state.Host.Id)
                return Result.FromError("Only the host can change the settings.");
            if (!state.IsJoinable)
                return Result.FromError("Settings can only change before the game starts.");
            if (Deserialize<CardCounterSettings>(payloadJson) is not { } incoming)
                return Result.FromError("Malformed settings payload.");
            return state.UpdateSettings(cur => incoming with { HostPlays = cur.HostPlays });
        }

        private Result KickFromPayload(User caller, CardCounterGameState state, string? payloadJson)
        {
            if (Deserialize<TargetPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed kick payload.");

            var target = state.Players.FirstOrDefault(e => e.User.Id == p.TargetPlayerId).User;
            if (target is null)
                return Result.FromError("Player is not in this lobby.");

            return state.KickPlayer(caller, target);
        }

        private static T? Deserialize<T>(string? payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) return default;
            try { return JsonSerializer.Deserialize<T>(payloadJson, CommandJsonOptions); }
            catch (JsonException) { return default; }
        }

        // ── AbstractGameEngine lifecycle ─────────────────────────────────────

        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.", $"Parameter {nameof(host)} was null.");

            var gameState = new CardCounterGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            gameState.SubscribePlayerUnregistered(player => HandlePlayerLeft(player, gameState));
            logger.LogInformation("Created CardCounter state with host [{id}].", host.Id);
            return gameState;
        }

        protected override async Task<Result> StartAsyncCore(
            CardCounterGameState gameState, CancellationToken ct = default)
        {
            // The host counts as a participant when configured to play, so a host-as-player game
            // can start with no other players. A shared-display game still needs a joined player.
            int participantCount = gameState.Players.Length + (gameState.Settings.HostPlays ? 1 : 0);
            if (participantCount == 0)
                return Result.FromError("At least one player must be in the game before starting.");

            var context = new CardCounterGameContext(gameState, randomNumberService, logger);
            var fsm = new FiniteStateMachine<CardCounterGameContext, CardCounterCommand>(logger);
            context.Fsm = fsm;

            var executeResult = gameState.Execute(() =>
            {
                gameState.SetJoinable(false);
                gameState.SetHostIsParticipant(gameState.Settings.HostPlays);
                gameState.Context = context;
                InitializeGame(context);
            });

            if (executeResult.IsFailure) return executeResult;

            // Transition into the initial FSM state.
            // In Active Operator Mode the buy-in step is skipped: every player starts with a
            // balance of 10 and the game goes straight to the first round.
            if (gameState.Settings.ActiveOperatorMode)
            {
                foreach (var ps in context.State.GamePlayers.Values)
                {
                    ps.Balance = 10;
                    ps.HasSetBuyIn = true;
                }
                context.Fsm.TransitionTo(context, new RoundEndState());
            }
            else
            {
                context.Fsm.TransitionTo(context, new BuyInState());
            }
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
                context.Fsm.HandleCommand(context, command);
            });
        }

        /// <summary>
        /// Drives time-based transitions (e.g., action-response timeouts).
        /// Call periodically from a timer or background service.
        /// Does nothing when <see cref="CardCounterSettings.EnableActionTimer"/> is <c>false</c>.
        /// </summary>
        public Result Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            return context.State.Execute(() =>
            {
                if (!context.Settings.EnableActionTimer) return;
                context.Fsm.Tick(context, now);
            });
        }

        private static void TransitionTo(CardCounterGameContext context, IGameState<CardCounterGameContext, CardCounterCommand> next)
        {
            context.Fsm.TransitionTo(context, next);
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
        public Result PlayActionCard(User player, CardCounterGameState state, int cardIndex, Guid? targetPlayerId = null)
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
        public Result NotMyMoneySelectTarget(User player, CardCounterGameState state, Guid targetPlayerId)
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
        /// Hooks for the base <see cref="AbstractGameEngine{TState}.ReturnToLobby"/> (host-only,
        /// terminal-phase-only) so players can join/leave and settings can change before the next game.
        /// </summary>
        protected override bool IsTerminalPhase(CardCounterGameState state) => state.Phase == GamePhase.GameOver;

        /// <inheritdoc />
        protected override void ResetForLobby(CardCounterGameState state)
        {
            state.Context = null;
            state.GamePlayers.Clear();
            state.TurnManager.SetTurnOrder([]);
            state.ShoeIndex = 0;
            state.DiscardHistory.Clear();
            state.MainDeck.Clear();
            state.CurrentShoe.Clear();
            state.DiscardPile.Clear();
            state.LastPlayedAction = null;
            state.LastDrawnCard = null;
            state.LastOperatorResult = null;
            state.LastOperatorChange = null;
            state.PendingReaction = null;
            state.FeelingLuckyTargetId = null;
            state.IsNotMyMoneySelecting = false;
            state.PendingNotMyMoneyOperator = null;
            state.ForceDrawStack.Clear();
            state.IsNewShoe = false;
            state.HedgeYourBetPlayerId = null;
        }

        // ── Player-leave handling ─────────────────────────────────────────────

        /// <summary>
        /// Called whenever a player unregisters from the game (disconnect, tab close, or kick).
        /// Removes the player from the turn order and player table. If the leaving player was
        /// the active player the FSM is immediately advanced to the next player's turn. If no
        /// players remain the game transitions to <see cref="GameOverState"/>.
        /// </summary>
        internal void HandlePlayerLeft(User player, CardCounterGameState state)
        {
            // If the game hasn't been started yet (no context) there is no turn order to fix.
            if (state.Context is null || state.IsDisposed) return;

            var context = state.Context;

            state.Execute(() =>
            {
                int leftIndex = state.TurnManager.TurnOrder.IndexOf(player.Id);
                if (leftIndex < 0) return; // Not in turn order; nothing to adjust.

                // Remember whether the leaving player was the one currently taking their turn.
                bool wasActiveTurn = leftIndex == state.TurnManager.CurrentPlayerIndex
                                     && state.Phase == GamePhase.Playing;

                state.TurnManager.TurnOrder.RemoveAt(leftIndex);
                state.GamePlayers.TryRemove(player.Id, out _);

                logger.LogInformation(
                    "Player [{id}] left the game. TurnOrder now has {n} player(s).",
                    player.Id, state.TurnManager.TurnOrder.Count);

                // If no players remain, end the game immediately.
                if (state.TurnManager.TurnOrder.Count == 0)
                {
                    TransitionTo(context, new GameOverState());
                    return;
                }

                // Adjust CurrentPlayerIndex to stay pointed at the correct next player.
                if (leftIndex < state.TurnManager.CurrentPlayerIndex)
                {
                    // A player before the current one was removed; shift the index left.
                    state.TurnManager.SetCurrentPlayerIndex(state.TurnManager.CurrentPlayerIndex - 1);
                }
                else if (leftIndex == state.TurnManager.CurrentPlayerIndex
                         && state.TurnManager.CurrentPlayerIndex >= state.TurnManager.TurnOrder.Count)
                {
                    // The removed player was last in the list; wrap to the first player.
                    state.TurnManager.SetCurrentPlayerIndex(0);
                }
                // else: removed player was after the current one — index is unaffected.

                // If the active player left while a turn was in progress, immediately start
                // the next player's turn so the game doesn't stall waiting for a response
                // from a player that is no longer connected.
                if (wasActiveTurn)
                {
                    TransitionTo(context, new PlayerTurnState());
                    return;
                }

                // During BuyIn, check whether all remaining players have now committed
                // their buy-in (the leaving player may have been the only one outstanding).
                if (state.Phase == GamePhase.BuyIn
                    && state.GamePlayers.Values.All(p => p.HasSetBuyIn))
                {
                    TransitionTo(context, new RoundEndState());
                }
            });
        }

        // ── Initialisation helpers ────────────────────────────────────────────

        private void InitializeGame(CardCounterGameContext context)
        {
            var state = context.State;
            state.GamePlayers.Clear();
            state.TurnManager.SetTurnOrder([]);
            state.ShoeIndex = 0;

            // Register every participant. When the host is configured as a participant,
            // state.Participants includes the host (at index 0) alongside the joined players;
            // otherwise it equals state.Players (non-host only), preserving prior behavior.
            var playerIds = new List<Guid>();
            foreach (var entry in state.Participants)
            {
                var ps = new PlayerState
                {
                    PlayerId = entry.User.Id,
                    DisplayName = entry.DisplayName,
                    PassesRemaining = state.Settings.TotalPassesPerPlayer,
                    BuyInRoll = randomNumberService.GetRandomInt(1, 7, RandomType.Fast),
                    ActiveOperator = state.Settings.ActiveOperatorMode ? Operator.Add : null
                };

                state.GamePlayers[entry.User.Id] = ps;
                playerIds.Add(entry.User.Id);
            }
            state.TurnManager.SetTurnOrder(playerIds);

            BuildAndShuffleDeck(context);
        }

        private void BuildAndShuffleDeck(CardCounterGameContext context)
        {
            var state = context.State;
            var cfg = state.Settings;
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
