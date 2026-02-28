using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.CardCounter
{
    public class CardCounterGameState(
        User host,
        ILogger<CardCounterGameState> logger,
        IRandomNumberService randomNumberService)
        : AbstractGameState(host, logger)
    {
        private readonly IRandomNumberService _random = randomNumberService;

        public GamePhase Phase { get; private set; }
        public List<Card> MainDeck { get; private set; } = new();
        public List<Card> CurrentShoe { get; private set; } = new();
        public int ShoeIndex { get; private set; }
        public Dictionary<CardType, int> ShoeCardCounts { get; private set; } = new();
        public List<Card> DiscardPile { get; private set; } = new();
        public List<ActionCard> ActionDeck { get; private set; } = new();
        public int CurrentPlayerIndex { get; private set; }
        public List<string> TurnOrder { get; private set; } = new();
        public GameConfig Config { get; private set; } = new();

        public ConcurrentDictionary<string, PlayerState> GamePlayers { get; private set; } = new();

        public PendingAction? CurrentPendingAction { get; private set; }
        public ForcedDrawChain? PendingChain { get; private set; }

        public void InitializeGame()
        {
            Phase = GamePhase.BuyIn;
            TurnOrder = Players.Select(p => p.Id).ToList();

            foreach (var p in Players)
            {
                int buyInRoll = _random.GetRandomInt(1, 7, RandomType.Secure);
                GamePlayers.TryAdd(p.Id, new PlayerState { 
                    PlayerId = p.Id, 
                    DisplayName = p.Name,
                    IsHost = p.Id == Host.Id,
                    PassesRemaining = Config.TotalPassesPerPlayer,
                    BuyInRoll = buyInRoll
                });
            }

            BuildMainDeck();
            BuildActionDeck();

            DealActionCards();

            DealNextShoe();
        }

        private void BuildMainDeck()
        {
            MainDeck.Clear();
            int numNumberCards = (int)(Config.DeckSize * (Config.NumberToOperatorRatio / (Config.NumberToOperatorRatio + 1)));
            int numOpCards = Config.DeckSize - numNumberCards;
            
            for (int i = 0; i < numNumberCards; i++)
            {
                MainDeck.Add(new NumberCard(i % 10));
            }

            int addSubCards = (int)(numOpCards * (Config.AddSubToMulDivRatio / (Config.AddSubToMulDivRatio + 1)));
            int mulDivCards = numOpCards - addSubCards;

            for (int i = 0; i < addSubCards; i++)
            {
                MainDeck.Add(new OperatorCard(i % 2 == 0 ? Operator.Add : Operator.Subtract));
            }

            for (int i = 0; i < mulDivCards; i++)
            {
                MainDeck.Add(new OperatorCard(i % 2 == 0 ? Operator.Multiply : Operator.Divide));
            }

            Shuffle(MainDeck);
        }

        private void BuildActionDeck()
        {
            ActionDeck.Clear();
            var types = Enum.GetValues<ActionType>();
            for (int i = 0; i < 50; i++)
            {
                 ActionDeck.Add(new ActionCard(types[_random.GetRandomInt(0, types.Length, RandomType.Secure)]));
            }
        }

        private void DealActionCards()
        {
            foreach (var p in GamePlayers.Values)
            {
                for (int i = 0; i < Config.ActionsDealtPerRound && ActionDeck.Count > 0; i++)
                {
                    var card = ActionDeck[0];
                    ActionDeck.RemoveAt(0);
                    p.ActionHand.Add(card);
                }
            }
        }

        private void DealNextShoe()
        {
            CurrentShoe.Clear();
            if (MainDeck.Count == 0)
            {
                 Phase = GamePhase.GameOver;
                 return;
            }

            int shoeSize = _random.GetRandomInt(Config.MinShoeSize, Config.MaxShoeSize + 1, RandomType.Secure);
            if (MainDeck.Count <= Config.MinShoeSize * 2) 
                 shoeSize = MainDeck.Count;

            shoeSize = Math.Min(shoeSize, MainDeck.Count);

            for (int i = 0; i < shoeSize; i++)
            {
                CurrentShoe.Add(MainDeck[0]);
                MainDeck.RemoveAt(0);
            }

            ShoeIndex++;
            RecalculateShoeCounts();
        }

        private void RecalculateShoeCounts()
        {
            ShoeCardCounts[CardType.Number] = CurrentShoe.Count(c => c.Type == CardType.Number);
            ShoeCardCounts[CardType.Operator] = CurrentShoe.Count(c => c.Type == CardType.Operator);
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

        public Result HandleAction(User user, PlayerAction action)
        {
            return Execute(() =>
            {
                if (!GamePlayers.TryGetValue(user.Id, out var player)) return;

                if (Phase == GamePhase.BuyIn && action.ActionKind == ActionKind.SetBuyIn)
                {
                     bool isNegative = (bool)action.Data["IsNegative"];
                     player.Balance = player.BuyInRoll * 8 * (isNegative ? -1 : 1);
                     player.HasSetBuyIn = true;

                     if (GamePlayers.Values.All(p => p.HasSetBuyIn))
                     {
                          Phase = GamePhase.Playing;
                     }
                     return;
                }

                if (Phase != GamePhase.Playing) return;

                string activePlayerId = TurnOrder[CurrentPlayerIndex];
                if (user.Id != activePlayerId && action.ActionKind != ActionKind.AcceptPending) return;

                if (player.PrivateReveal != null && action.ActionKind != ActionKind.ReorderMakeMyLuck)
                {
                    // Player must resolve the revealed cards first
                    return;
                }

                if (action.ActionKind == ActionKind.PlayActionCard)
                {
                    if (action.Data.TryGetValue("CardIndex", out var cardIndexObj) && cardIndexObj is int cardIndex)
                    {
                        if (cardIndex >= 0 && cardIndex < player.ActionHand.Count)
                        {
                            var card = player.ActionHand[cardIndex];
                            player.ActionHand.RemoveAt(cardIndex);
                            // Action cards use a separate discard pile or just get removed. DiscardPile is for MainDeck

                            // Process the specific action card
                            if (card.Action == ActionType.Burn)
                            {
                                if (CurrentShoe.Count > 0)
                                {
                                    var burnedCard = CurrentShoe[0];
                                    CurrentShoe.RemoveAt(0);
                                    DiscardPile.Add(burnedCard);
                                    RecalculateShoeCounts();

                                    if (CurrentShoe.Count == 0)
                                    {
                                        DealActionCards();
                                        DealNextShoe();
                                    }
                                }
                            }
                            else if (card.Action == ActionType.MakeMyLuck)
                            {
                                int cardsToReveal = Math.Min(3, CurrentShoe.Count);
                                if (cardsToReveal > 0)
                                {
                                    player.PrivateReveal = CurrentShoe.Take(cardsToReveal).ToList();
                                    CurrentShoe.RemoveRange(0, cardsToReveal);
                                    RecalculateShoeCounts();
                                }
                            }
                            // TODO: Implement other action cards (FeelingLucky, Skim, TurnTheTable, Compd, NotMyMoney, Launder)
                        }
                    }
                }
                else if (action.ActionKind == ActionKind.ReorderMakeMyLuck)
                {
                    if (player.PrivateReveal != null && action.Data.TryGetValue("ReorderedIndices", out var indicesObj) && indicesObj is int[] indices)
                    {
                        if (indices.Length == player.PrivateReveal.Count && indices.Distinct().Count() == indices.Length && indices.All(i => i >= 0 && i < player.PrivateReveal.Count))
                        {
                            var reorderedCards = indices.Select(i => player.PrivateReveal[i]).ToList();
                            CurrentShoe.InsertRange(0, reorderedCards);
                            player.PrivateReveal = null;
                            RecalculateShoeCounts();
                        }
                    }
                }
                else if (action.ActionKind == ActionKind.Draw)
                {
                    if (CurrentShoe.Count == 0) return;
                    var card = CurrentShoe[0];
                    CurrentShoe.RemoveAt(0);
                    RecalculateShoeCounts();

                    if (card is NumberCard num)
                    {
                        player.Pot.Add(num.Value);
                    }
                    else if (card is OperatorCard opCard)
                    {
                        if (player.Pot.Count > 0)
                        {
                             if (player.PotValue == 0 && opCard.Op == Operator.Divide)
                             {
                                  // Simplified logic: gain pass
                                  player.PassesRemaining++;
                             }
                             else
                             {
                                 int potVal = player.PotValue;
                                 float newVal = player.Balance;
                                 if (opCard.Op == Operator.Add) newVal += potVal;
                                 if (opCard.Op == Operator.Subtract) newVal -= potVal;
                                 if (opCard.Op == Operator.Multiply) newVal *= potVal;
                                 if (opCard.Op == Operator.Divide) newVal /= potVal;
                                 
                                 player.Balance = (int)Math.Round(newVal);
                             }
                             player.Pot.Clear();
                        }
                    }

                    EndTurn();
                }
                else if (action.ActionKind == ActionKind.Pass)
                {
                    if (player.PassesRemaining > 0)
                    {
                        player.PassesRemaining--;
                        EndTurn();
                    }
                }
                else if (action.ActionKind == ActionKind.Fold)
                {
                    if (player.PassesRemaining > 0)
                    {
                        player.PassesRemaining--;
                        player.Pot.Clear();
                    }
                }
            });
        }

        private void EndTurn()
        {
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % TurnOrder.Count;
            if (CurrentShoe.Count == 0)
            {
                 DealActionCards();
                 DealNextShoe();
            }
        }
    }
}