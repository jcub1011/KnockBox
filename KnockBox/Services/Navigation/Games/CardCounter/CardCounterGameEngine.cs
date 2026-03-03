using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Navigation.Games.CardCounter
{
    public class CardCounterGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<CardCounterGameEngine> logger,
        ILogger<CardCounterGameState> stateLogger) : AbstractGameEngine
    {
        private readonly IRandomNumberService _random = randomNumberService;

        public override async Task<Result<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Result.FromError<AbstractGameState>(new ArgumentNullException(nameof(host)));

            var gameState = new CardCounterGameState(host, stateLogger, randomNumberService);
            gameState.UpdateJoinableStatus(true);
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return Result.FromValue<AbstractGameState>(gameState);
        }

        public override async Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not CardCounterGameState gameState)
                return Result.FromError(new InvalidCastException($"Game state of type [{state.GetType().Name}] couldn't be cast to type [{nameof(CardCounterGameState)}]."));

            if (host != gameState.Host)
                return Result.FromError(new InvalidOperationException($"Only the host can start the game."));

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
                InitializeGame(gameState);
            });

            if (executeResult.IsFailure) return executeResult;
            return Result.Success;
        }

        private void InitializeGame(CardCounterGameState state)
        {
            state.Phase = GamePhase.BuyIn;
            state.TurnOrder = state.Players.Select(p => p.Id).ToList();

            foreach (var p in state.Players)
            {
                int buyInRoll = _random.GetRandomInt(1, 7, RandomType.Secure);
                state.GamePlayers.TryAdd(p.Id, new PlayerState { 
                    PlayerId = p.Id, 
                    DisplayName = p.Name,
                    IsHost = p.Id == state.Host.Id,
                    PassesRemaining = state.Config.TotalPassesPerPlayer,
                    BuyInRoll = buyInRoll
                });
            }

            BuildMainDeck(state);
            BuildActionDeck(state);

            DealActionCards(state);
            DealNextShoe(state);
        }

        private void BuildMainDeck(CardCounterGameState state)
        {
            state.MainDeck.Clear();
            int numNumberCards = (int)(state.Config.DeckSize * (state.Config.NumberToOperatorRatio / (state.Config.NumberToOperatorRatio + 1)));
            int numOpCards = state.Config.DeckSize - numNumberCards;

            for (int i = 0; i < numNumberCards; i++)
            {
                state.MainDeck.Add(new NumberCard(i % 10));
            }

            int addSubCards = (int)(numOpCards * (state.Config.AddSubToMulDivRatio / (state.Config.AddSubToMulDivRatio + 1)));
            int mulDivCards = numOpCards - addSubCards;

            for (int i = 0; i < addSubCards; i++)
            {
                state.MainDeck.Add(new OperatorCard(i % 2 == 0 ? Operator.Add : Operator.Subtract));
            }

            for (int i = 0; i < mulDivCards; i++)
            {
                state.MainDeck.Add(new OperatorCard(i % 2 == 0 ? Operator.Multiply : Operator.Divide));
            }

            Shuffle(state.MainDeck);
        }

        private void BuildActionDeck(CardCounterGameState state)
        {
            state.ActionDeck.Clear();
            var types = Enum.GetValues<ActionType>();
            for (int i = 0; i < 50; i++)
            {
                 state.ActionDeck.Add(new ActionCard(types[_random.GetRandomInt(0, types.Length, RandomType.Secure)]));
            }
        }

        private void DealActionCards(CardCounterGameState state)
        {
            foreach (var p in state.GamePlayers.Values)
            {
                for (int i = 0; i < state.Config.ActionsDealtPerRound && state.ActionDeck.Count > 0; i++)
                {
                    var card = state.ActionDeck[0];
                    state.ActionDeck.RemoveAt(0);
                    p.ActionHand.Add(card);
                }
            }
        }

        private void DealNextShoe(CardCounterGameState state)
        {
            state.CurrentShoe.Clear();
            if (state.MainDeck.Count == 0)
            {
                 state.Phase = GamePhase.GameOver;
                 return;
            }

            int shoeSize = _random.GetRandomInt(state.Config.MinShoeSize, state.Config.MaxShoeSize + 1, RandomType.Secure);
            if (state.MainDeck.Count <= state.Config.MinShoeSize * 2) 
                 shoeSize = state.MainDeck.Count;

            shoeSize = Math.Min(shoeSize, state.MainDeck.Count);

            for (int i = 0; i < shoeSize; i++)
            {
                state.CurrentShoe.Add(state.MainDeck[0]);
                state.MainDeck.RemoveAt(0);
            }

            state.ShoeIndex++;
            RecalculateShoeCounts(state);
        }

        private void RecalculateShoeCounts(CardCounterGameState state)
        {
            state.ShoeCardCounts[CardType.Number] = state.CurrentShoe.Count(c => c.Type == CardType.Number);
            state.ShoeCardCounts[CardType.Operator] = state.CurrentShoe.Count(c => c.Type == CardType.Operator);
        }

        private void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _random.GetRandomInt(0, n + 1, RandomType.Secure);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        private void EndTurn(CardCounterGameState state)
        {
            state.CurrentPlayerIndex = (state.CurrentPlayerIndex + 1) % state.TurnOrder.Count;
            if (state.CurrentShoe.Count == 0)
            {
                 DealActionCards(state);
                 DealNextShoe(state);
            }
        }

        public Result SetBuyIn(User player, CardCounterGameState state, bool isNegative)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.Phase != GamePhase.BuyIn) return;

                statePlayer.Balance = statePlayer.BuyInRoll * 8 * (isNegative ? -1 : 1);
                statePlayer.HasSetBuyIn = true;

                if (state.GamePlayers.Values.All(p => p.HasSetBuyIn))
                {
                    state.Phase = GamePhase.Playing;
                }
            });
        }

        public Result DrawCard(User player, CardCounterGameState state)
        {
            return state.Execute(() =>
            {
                if (!state.GamePlayers.TryGetValue(player.Id, out var statePlayer)) return;
                if (state.Phase != GamePhase.Playing) return;

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
                if (state.Phase != GamePhase.Playing) return;

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
                if (state.Phase != GamePhase.Playing) return;

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
                if (state.Phase != GamePhase.Playing) return;

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
                if (state.Phase != GamePhase.Playing) return;

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