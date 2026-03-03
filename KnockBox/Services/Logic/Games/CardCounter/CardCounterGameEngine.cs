using KnockBox.Extensions.Collections;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Mvc.TagHelpers;

namespace KnockBox.Services.Logic.Games.CardCounter
{
    public class CardCounterGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<CardCounterGameEngine> logger,
        ILogger<CardCounterGameState> stateLogger) : AbstractGameEngine
    {
        private readonly Dictionary<Operator, OperatorCard> _operatorCardMap = new()
        {
            { Operator.Add, new OperatorCard(Operator.Add, "Adds the player pot to their balance, clearing the pot.") },
            { Operator.Subtract, new OperatorCard(Operator.Subtract, "Subtracts the player pot from their balance, clearing the pot.") },
            { Operator.Multiply, new OperatorCard(Operator.Multiply, "Multiplies the player pot with their balance, clearing the pot.") },
            { Operator.Divide, new OperatorCard(Operator.Divide, "Divides the player balance with their pot, clearing the pot.") },
        };

        private readonly Dictionary<ActionType, ActionCard> _actionCardMap = new()
        {
            { ActionType.FeelingLucky, new ActionCard(ActionType.FeelingLucky, 
                "Forces the next player to draw a card. The player who initially plays this card does not have their turn skipped. The round will continue from them.") },
            { ActionType.MakeMyLuck, new ActionCard(ActionType.MakeMyLuck, 
                "View the top 3 cards of the current shoe and reorder them.") },
            { ActionType.Skim, new ActionCard(ActionType.Skim,
                "Swap any digit in your pot with a digit in a different player's pot.") },
            { ActionType.Burn, new ActionCard(ActionType.Burn,
                "Discard the top card in the current shoe. The discarded card will be revealed to all players.") },
            { ActionType.TurnTheTable, new ActionCard(ActionType.TurnTheTable,
                "Reverse the digit order of a player's pot. Can be used on yourself.") },
            { ActionType.Compd, new ActionCard(ActionType.Compd,
                "Negate the effect of any aciton card that targets you.") },
            { ActionType.NotMyMoney, new ActionCard(ActionType.NotMyMoney,
                "Redirects an operator to a different player. Uses that player's pot and balanace.") },
            { ActionType.Launder, new ActionCard(ActionType.Launder,
                "Swap your entire pot with a different player's pot.") }
        };

        private ValueResult<ActionCard> GetRandomActionCard()
        {
            if (_actionCardMap.Count == 0)
            {
                logger.LogCritical("Action card map is empty. Unable to generate action card deck.");
                return ValueResult<ActionCard>.FromError("No action cards are defined.");
            }

            int randomCardIndex = randomNumberService.GetRandomInt(0, _actionCardMap.Count, RandomType.Secure);
            return _actionCardMap.Values.ElementAt(randomCardIndex);
        }

        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null.");

            var gameState = new CardCounterGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return gameState;
        }

        public override async Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not CardCounterGameState gameState)
                return Result.FromError("Error starting game.", $"Game state of type [{state.GetType().Name}] couldn't be cast to type [{nameof(CardCounterGameState)}].");

            if (host != gameState.Host)
                return Result.FromError("Only the host can start the game.");

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
                InitializeGame(gameState);
            });

            if (executeResult.IsFailure) return executeResult;
            await gameState.GameStartEventManager.NotifyAsync();

            return Result.Success;
        }

        private void InitializeGame(CardCounterGameState state)
        {
            state.Execute(() =>
            {
                state.GamePhase = GamePhase.BuyIn;
                state.RoundPhase = RoundPhase.AwardActionCards;
                state.PlayerStates.Clear();
                state.TurnOrder.Clear();
                state.TurnOrder.AddRange(state.Players.Select(p => p.Id));

                foreach (var user in state.Players)
                {
                    // Host does not participate
                    if (state.Host == user) continue;

                    var playerState = new Player(user);
                    state.PlayerStates[user.Id] = playerState;

                    playerState.RemainingPasses = state.Config.TotalPassesPerPlayer;
                    playerState.BuyInRoll = randomNumberService.GetRandomInt(1, 7, RandomType.Fast);
                }

                BuildMainDeck(state);
            });
        }

        private void BuildMainDeck(CardCounterGameState state)
        {
            List<BaseCard> cards = new(state.Config.DeckSize);
            state.MainDeck.Clear();

            int numNumberCards = (int)(state.Config.DeckSize * (state.Config.NumberToOperatorRatio / (state.Config.NumberToOperatorRatio + 1)));
            int numOpCards = state.Config.DeckSize - numNumberCards;

            for (int i = 0; i < numNumberCards; i++)
            {
                long cardValue = i % 10;
                state.MainDeck.Push(new NumberCard(cardValue, $"A card with a value of {cardValue}."));
            }

            int addSubCards = (int)(numOpCards * (state.Config.AddSubToMulDivRatio / (state.Config.AddSubToMulDivRatio + 1)));
            int mulDivCards = numOpCards - addSubCards;

            for (int i = 0; i < addSubCards; i++)
            {
                var opp = i % 2 == 0 ? Operator.Add : Operator.Subtract;

                if (!_operatorCardMap.TryGetValue(opp, out var card))
                {
                    logger.LogError("No card is mapped for operator [{opp}]. Defaulting to add operator.", opp);
                    card = new OperatorCard(Operator.Add, "Adds the player pot to their balance, clearing the pot.");
                }

                state.MainDeck.Push(card);
            }

            for (int i = 0; i < mulDivCards; i++)
            {
                var opp = i % 2 == 0 ? Operator.Multiply : Operator.Divide;

                if (!_operatorCardMap.TryGetValue(opp, out var card))
                {
                    logger.LogError("No card is mapped for operator [{opp}]. Defaulting to Multiply operator.", opp);
                    card = new OperatorCard(Operator.Multiply, "Multiplies the player pot with their balance, clearing the pot.");
                }

                state.MainDeck.Push(card);
            }

            Shuffle(cards);
            state.MainDeck.PushRange(cards);
        }

        /// <summary>
        /// Deals action cards to all players and completes when all players have selected their action cards.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result> DealActionCardsAsync(CardCounterGameState state, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Result.Canceled;

            // Distribute cards
            state.Execute(() => 
            {
                foreach (var playerState in state.PlayerStates.Values)
                {
                    for (int i = 0; i < state.Config.ActionsDealtPerRound; i++)
                    {
                        var cardResult = GetRandomActionCard();
                        if (cardResult.TryGetSuccess(out var card))
                        {
                            playerState.ActionCards.Add(card);
                        }
                        else
                        {
                            logger.LogError("Error selecting random action card: {msg}",
                                cardResult.Error.Error.InternalMessage);
                        }
                    }
                }
            });

            await state.ActionCardsDealtEventManager.NotifyAsync();
            return Result.Success;
        }

        /// <summary>
        /// Deals the next shoe.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result> DealNextShoeAsync(CardCounterGameState state, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Result.Canceled;

            var executeResult = await state.ExecuteAsync(async () =>
            {
                state.CurrentShoe.Clear();
                if (state.MainDeck.Count == 0)
                {
                    state.GamePhase = GamePhase.GameEnd;
                    return;
                }

                state.ShoeIndex++;
                int shoeSize = randomNumberService.GetRandomInt(state.Config.MinShoeSize, state.Config.MaxShoeSize + 1, RandomType.Secure);

                if (state.MainDeck.Count < state.Config.MinShoeSize)
                {
                    shoeSize = state.MainDeck.Count;
                }
                else if (state.MainDeck.Count - shoeSize < state.Config.MinShoeSize)
                {
                    int remainder = state.MainDeck.Count - state.Config.MinShoeSize;

                    if (remainder < state.Config.MinShoeSize)
                    {
                        shoeSize = state.MainDeck.Count;
                    }
                    else
                    {
                        int maxAllowed = Math.Min(state.MainDeck.Count - state.Config.MinShoeSize, state.Config.MaxShoeSize);
                        shoeSize = randomNumberService.GetRandomInt(state.Config.MinShoeSize, maxAllowed + 1, RandomType.Secure);
                    }
                }

                state.CurrentShoe.PushRange(state.MainDeck.PopRange(shoeSize));
                RecalculateShoeCounts(state);
            }, ct);

            if (!executeResult.IsSuccess) return executeResult;
            await state.ShoeDealEventManager.NotifyAsync();
            return Result.Success;
        }

        private static void RecalculateShoeCounts(CardCounterGameState state)
        {
            state.OperatorCounts.Clear();
            state.ValueCounts.Clear();

            RecalculateShoeCounts(state, state.CurrentShoe, []);
        }

        private static void RecalculateShoeCounts(CardCounterGameState state, 
            IEnumerable<BaseCard> added, IEnumerable<BaseCard> removed)
        {
            foreach (var newCard in added)
            {
                if (newCard is OperatorCard op)
                {
                    IncrementCount(state.OperatorCounts, op.Operator);
                }
                else if (newCard is NumberCard num)
                {
                    IncrementCount(state.ValueCounts, num.Value);
                }
            }

            foreach (var oldCard in removed)
            {
                if (oldCard is OperatorCard op)
                {
                    DecrementCount(state.OperatorCounts, op.Operator);
                }
                else if (oldCard is NumberCard num)
                {
                    DecrementCount(state.ValueCounts, num.Value);
                }
            }
        }

        private static void IncrementCount<TKey>(Dictionary<TKey, int> counts, TKey key)
            where TKey : notnull
        {
            counts.TryGetValue(key, out int current);
            counts[key] = current + 1;
        }

        private static void DecrementCount<TKey>(Dictionary<TKey, int> counts, TKey key)
            where TKey : notnull
        {
            counts.TryGetValue(key, out int current);
            if (current <= 1) counts.Remove(key);
            else counts[key] = current - 1;
        }

        private void Shuffle(List<BaseCard> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = randomNumberService.GetRandomInt(0, n + 1, RandomType.Secure);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }

        public async Task<Result> SkipTurnAsync(CardCounterGameState state, User player)
        {
            var executeResult = state.Execute(() =>
            {
                string currentPlayerId = state.TurnOrder[state.CurrentPlayerIndex];

                if (player.Id != currentPlayerId)
                {
                    return Result.FromError("You can only skip when it is your turn.");
                }

                if (!state.PlayerStates.TryGetValue(currentPlayerId, out var playerState))
                {
                    return Result.FromError("Error skipping turn.",
                        $"Player state missing for player [{currentPlayerId}].");
                }

                if (playerState.RemainingPasses <= 0)
                {
                    return Result.FromError("You have no passes remaining.");
                }

                state.CurrentPlayerIndex = (state.CurrentPlayerIndex + 1) % state.TurnOrder.Count;
                return Result.Success;
            });

            if (executeResult.TryGetSuccess(out var result))
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.IsSuccess)
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            await state.TurnChangeEventManager.NotifyAsync();

            return Result.Success;
        }

        public async Task<Result> DrawCard(CardCounterGameState state, User player)
        {
            var executeResult = state.Execute(() =>
            {
                string currentPlayerId = state.TurnOrder[state.CurrentPlayerIndex];

                if (player.Id != currentPlayerId)
                {
                    return ValueResult<BaseCard>.FromError("You can only draw when it is your turn.");
                }

                if (!state.PlayerStates.TryGetValue(currentPlayerId, out var playerState))
                {
                    return ValueResult<BaseCard>.FromError("Error skipping turn.",
                        $"Player state missing for player [{currentPlayerId}].");
                }

                if (!state.MainDeck.TryPop(out var card))
                {
                    return ValueResult<BaseCard>.FromError("There are no cards to draw.");
                }

                state.CurrentPlayerIndex = (state.CurrentPlayerIndex + 1) % state.TurnOrder.Count;
                return ValueResult<BaseCard>.FromValue(card);
            });

            if (executeResult.TryGetSuccess(out var result))
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.TryGetSuccess(out var drawnCard))
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            await state.TurnChangeEventManager.NotifyAsync();

            return Result.Success;
        }

        public Result SetBuyIn(User player, CardCounterGameState state, bool isNegative)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.GamePhase != GamePhase.BuyIn) return;

                statePlayer.Balance = statePlayer.BuyInRoll * 8 * (isNegative ? -1 : 1);
                statePlayer.HasSetBuyIn = true;

                if (state.GamePlayers.Values.All(p => p.HasSetBuyIn))
                {
                    state.GamePhase = GamePhase.Playing;
                }
            });
        }

        public Result DrawCard(User player, CardCounterGameState state)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.GamePhase != GamePhase.Playing) return;

                string activePlayerId = state.TurnOrder[state.CurrentPlayerIndex];
                if (player.Id != activePlayerId) return;
                if (statePlayer.PrivateReveal != null) return;
                if (state.CurrentShoe.Count == 0) return;

                var card = state.CurrentShoe[0];
                state.CurrentShoe.RemoveAt(0);
                RecalculateShoeCounts(state);

                if (card is NumberCard num)
                {
                    statePlayer.Pot.Add(num.Value);
                }
                else if (card is OperatorCard opCard)
                {
                    if (statePlayer.Pot.Count > 0)
                    {
                         if (statePlayer.PotValue == 0 && opCard.Op == Operator.Divide)
                         {
                              statePlayer.PassesRemaining++;
                         }
                         else
                         {
                             int potVal = statePlayer.PotValue;
                             float newVal = statePlayer.Balance;
                             if (opCard.Op == Operator.Add) newVal += potVal;
                             if (opCard.Op == Operator.Subtract) newVal -= potVal;
                             if (opCard.Op == Operator.Multiply) newVal *= potVal;
                             if (opCard.Op == Operator.Divide) newVal /= potVal;

                             statePlayer.Balance = (int)Math.Round(newVal);
                         }
                         statePlayer.Pot.Clear();
                    }
                }

                EndTurn(state);
            });
        }

        public Result PassTurn(User player, CardCounterGameState state)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.GamePhase != GamePhase.Playing) return;

                string activePlayerId = state.TurnOrder[state.CurrentPlayerIndex];
                if (player.Id != activePlayerId) return;
                if (statePlayer.PrivateReveal != null) return;

                if (statePlayer.PassesRemaining > 0)
                {
                    statePlayer.PassesRemaining--;
                    EndTurn(state);
                }
            });
        }

        public Result FoldPot(User player, CardCounterGameState state)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.GamePhase != GamePhase.Playing) return;

                string activePlayerId = state.TurnOrder[state.CurrentPlayerIndex];
                if (player.Id != activePlayerId) return;
                if (statePlayer.PrivateReveal != null) return;

                if (statePlayer.PassesRemaining > 0)
                {
                    statePlayer.PassesRemaining--;
                    statePlayer.Pot.Clear();
                }
            });
        }

        public Result PlayActionCard(User player, CardCounterGameState state, int cardIndex, string? targetPlayerId = null)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.GamePhase != GamePhase.Playing) return;

                string activePlayerId = state.TurnOrder[state.CurrentPlayerIndex];
                if (player.Id != activePlayerId) return;
                if (statePlayer.PrivateReveal != null) return;

                if (cardIndex >= 0 && cardIndex < statePlayer.ActionHand.Count)
                {
                    var card = statePlayer.ActionHand[cardIndex];
                    statePlayer.ActionHand.RemoveAt(cardIndex);

                    if (card.Action == ActionType.Burn)
                    {
                        if (state.CurrentShoe.Count > 0)
                        {
                            var burnedCard = state.CurrentShoe[0];
                            state.CurrentShoe.RemoveAt(0);
                            state.DiscardPile.Add(burnedCard);
                            RecalculateShoeCounts(state);

                            if (state.CurrentShoe.Count == 0)
                            {
                                DealActionCards(state);
                                DealNextShoe(state);
                            }
                        }
                    }
                    else if (card.Action == ActionType.MakeMyLuck)
                    {
                        int cardsToReveal = Math.Min(3, state.CurrentShoe.Count);
                        if (cardsToReveal > 0)
                        {
                            statePlayer.PrivateReveal = state.CurrentShoe.Take(cardsToReveal).ToList();
                            state.CurrentShoe.RemoveRange(0, cardsToReveal);
                            RecalculateShoeCounts(state);
                        }
                    }
                    // Other actions unimplemented in dummy
                }
            });
        }

        public Result SubmitReorder(User player, CardCounterGameState state, int[] reorderedIndices)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.GamePhase != GamePhase.Playing) return;

                if (statePlayer.PrivateReveal != null)
                {
                    if (reorderedIndices.Length == statePlayer.PrivateReveal.Count && reorderedIndices.Distinct().Count() == reorderedIndices.Length && reorderedIndices.All(i => i >= 0 && i < statePlayer.PrivateReveal.Count))
                    {
                        var reorderedCards = reorderedIndices.Select(i => statePlayer.PrivateReveal[i]).ToList();
                        state.CurrentShoe.InsertRange(0, reorderedCards);
                        statePlayer.PrivateReveal = null;
                        RecalculateShoeCounts(state);
                    }
                }
            });
        }

        public Result DiscardExcess(User player, CardCounterGameState state, int[] discardIndices)
        {
            return state.Execute(() =>
            {
                // Unimplemented dummy
            });
        }

        public Result AcceptPending(User player, CardCounterGameState state)
        {
            return state.Execute(() =>
            {
                // Unimplemented dummy
            });
        }
    }
}