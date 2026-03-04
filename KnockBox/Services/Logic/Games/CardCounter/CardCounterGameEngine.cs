using KnockBox.Extensions.Collections;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

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
            gameState.EventManager.Notify(new GameStartArgs());

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
        public Result DealActionCards(CardCounterGameState state, CancellationToken ct = default)
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

            state.EventManager.Notify(new ActionCardsDealtArgs());
            return Result.Success;
        }

        /// <summary>
        /// Deals the next shoe.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public Result DealNextShoe(CardCounterGameState state, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Result.Canceled;

            var executeResult = state.Execute(() =>
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
            });

            if (!executeResult.IsSuccess) return executeResult;
            state.EventManager.Notify(new ShoeDealtArgs());
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

        public Result SkipTurn(CardCounterGameState state, User player)
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

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (executeResult.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.IsSuccess)
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            state.EventManager.Notify(new TurnChangeArgs());

            return Result.Success;
        }

        public ValueResult<BaseCard> DrawCard(CardCounterGameState state, User player)
        {
            var executeResult = state.Execute(() =>
            {
                if (state.GamePhase != GamePhase.Playing)
                    return ValueResult<BaseCard>.FromError("Unable to draw a card outside the play stage.");

                if (state.TurnOrder[state.CurrentPlayerIndex] != player.Id)
                    return ValueResult<BaseCard>.FromError("You can't draw a card outside of your turn.");

                if (state.CurrentShoe.Count == 0)
                    return ValueResult<BaseCard>.FromError("There are no cards left to draw.");

                if (!state.PlayerStates.TryGetValue(player.Id, out var playerState))
                    return ValueResult<BaseCard>.FromError("Unable to get player state.",
                        $"No player state is registered for player [{player.Id}].");

                var card = state.CurrentShoe.Pop();
                RecalculateShoeCounts(state, [], [card]);
                return ValueResult<BaseCard>.FromValue(card);
            });

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (result.TryGetFailure(out var error)) return error;
                return ValueResult<BaseCard>.Canceled;
            }

            if (!result.IsSuccess)
            {
                return result;
            }

            state.EventManager.Notify(new CardDrawnArgs(result.Value, player));
            return result;
        }

        public Result EndTurn(CardCounterGameState state, User player)
        {
            var executeResult = state.Execute(() =>
            {
                if (state.GamePhase != GamePhase.Playing)
                    return Result.FromError("Unable end turn outside the play stage.");

                if (state.TurnOrder[state.CurrentPlayerIndex] != player.Id)
                    return Result.FromError("You can't end your turn when it is not your turn.");

                state.CurrentPlayerIndex = (state.CurrentPlayerIndex + 1) % state.TurnOrder.Count;
                return Result.Success;
            });

            state.EventManager.Notify(new TurnChangeArgs());
            return Result.Success;
        }

        public Result ApplyNumberCard(CardCounterGameState state, User target, NumberCard card)
        {
            var executeResult = state.Execute(() =>
            {
                if (!state.PlayerStates.TryGetValue(target.Id, out var playerState))
                    return Result.FromError("Unable to apply number to player.", 
                        $"Player [{target.Id}] does not have a player state registered.");

                playerState.Pot.Add(card);
                return Result.Success;
            });

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (executeResult.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.IsSuccess)
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            state.EventManager.Notify(new NumberCardAppliedArgs(card, target));
            return Result.Success;
        }

        public Result ApplyOperatorCard(CardCounterGameState state, User target, OperatorCard card)
        {
            var executeResult = state.Execute(() =>
            {
                if (!state.PlayerStates.TryGetValue(target.Id, out var playerState))
                    return Result.FromError("Unable to apply number to player.", 
                        $"Player [{target.Id}] does not have a player state registered.");

                var potValueResult = playerState.GetPotValue();

                if (!potValueResult.TryGetSuccess(out var potValue))
                    return Result.FromError(potValueResult.Error.Error);

                playerState.Balance = card.Operator switch
                {
                    Operator.Divide => playerState.Balance / potValue,
                    Operator.Multiply => playerState.Balance * potValue,
                    Operator.Subtract => playerState.Balance - potValue,
                    Operator.Add or _ => playerState.Balance + potValue
                };

                return Result.Success;
            });

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (executeResult.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.IsSuccess)
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            state.EventManager.Notify(new OperatorCardAppliedArgs(card, target));
            return Result.Success;
        }

        public Result RequestAllPlayerBuyIns(CardCounterGameState state)
        {
            var executeResult = state.Execute(() =>
            {
                if (state.GamePhase != GamePhase.BuyIn)
                    return Result.FromError("Can't set buy in outside of the buy in stage.");
                return Result.Success;
            });

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (executeResult.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.IsSuccess)
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            state.EventManager.Notify(new RequestBuyInArgs());
            return Result.Success;
        }

        public Result SetBuyIn(CardCounterGameState state, User player, int buyInRoll)
        {
            var executeResult = state.Execute(() =>
            {
                if (state.GamePhase != GamePhase.BuyIn)
                    return Result.FromError("Can't set buy in outside of the buy in stage.");

                if (!state.PlayerStates.TryGetValue(player.Id, out var playerState))
                    return Result.FromError("Unable to set buy in.",
                        $"Player state is not set for player [{player.Id}].");

                playerState.BuyInRoll = buyInRoll;
                playerState.Balance = RollToBalance(buyInRoll);
                return Result.Success;
            });

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.IsSuccess)
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            state.EventManager.Notify(new BuyInSetArgs(player));
            return Result.Success;
        }

        public int RollToBalance(int roll) => roll * 8;

        public Result PassTurn(CardCounterGameState state, User player)
        {
            var executeResult = state.Execute(() =>
            {
                if (state.GamePhase != GamePhase.Playing)
                    return Result.FromError("Unable to pass turn outside the play stage.");

                if (state.TurnOrder[state.CurrentPlayerIndex] != player.Id)
                    return Result.FromError("You can't pass outside of your turn.");

                if (!state.PlayerStates.TryGetValue(player.Id, out var playerState))
                    return Result.FromError("Unable to set buy in.",
                        $"Player state is not set for player [{player.Id}].");

                if (playerState.RemainingPasses <= 0)
                    return Result.FromError("You have no remaining passes.");

                playerState.RemainingPasses--;
                return Result.Success;
            });

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.IsSuccess) return result;

            state.EventManager.Notify(new TurnChangeArgs());
            return result;
        }

        #region Action Implementations

        /// <summary>
        /// Invokes the Feeling Lucky card action.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="player">The player that played this card.</param>
        /// <returns></returns>
        public async Task<Result> PlayFeelingLuckyAsync(CardCounterGameState state, User player)
        {
            var executeResult = state.Execute(() =>
            {
                if (!state.PlayerStates.TryGetValue(player.Id, out var playerState))
                    return ValueResult<ActionCard>.FromError("Unable to set buy in.",
                        $"Player state is not set for player [{player.Id}].");

                if (!_actionCardMap.TryGetValue(ActionType.FeelingLucky, out var card))
                    return ValueResult<ActionCard>.FromError("Unable to play card.",
                        $"No action card is registered for [{ActionType.FeelingLucky}].");

                if (!playerState.ActionCards.Remove(card))
                    return ValueResult<ActionCard>.FromError("You don't have this card.");

                state.ForceDrawStack.Push(player.Id);

                return ValueResult<ActionCard>.FromValue(card);
            });

            if (!executeResult.TryGetSuccess(out var result))
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            if (!result.TryGetSuccess(out var card))
            {
                if (result.TryGetFailure(out var error)) return error;
                return Result.Canceled;
            }

            state.EventManager.Notify(new ActionCardPlayedArgs(card, player));
            return Result.Success;
        }

        public async Task<Result> PlayMakeMyLuckAsync(CardCounterGameState state)
        {
            throw new NotImplementedException();
        }

        public async Task<Result> PlaySkimAsync(CardCounterGameState state)
        {
            throw new NotImplementedException();
        }

        public async Task<Result> PlayBurnAsync(CardCounterGameState state)
        {
            throw new NotImplementedException();
        }

        public async Task<Result> PlayTurnTheTableAsync(CardCounterGameState state)
        {
            throw new NotImplementedException();
        }

        public async Task<Result> PlayCompdAsync(CardCounterGameState state)
        {
            throw new NotImplementedException();
        }

        public async Task<Result> PlayNotMyMoneyAsync(CardCounterGameState state)
        {
            throw new NotImplementedException();
        }

        public async Task<Result> PlayLaunderAsync(CardCounterGameState state)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}