using KnockBox.Extensions.Collections;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM
{
    /// <summary>
    /// Per-game context that holds shared data and helpers used by FSM states.
    /// Created when the game starts and stored on <see cref="CardCounterGameState.Context"/>.
    /// </summary>
    public class CardCounterGameContext
    {
        private static readonly ActionCard[] ActionCardPool =
            Enum.GetValues<ActionType>().Select(t => new ActionCard(t)).ToArray();

        public CardCounterGameContext(
            CardCounterGameState state,
            IRandomNumberService rng,
            ILogger logger)
        {
            State = state;
            Rng = rng;
            Logger = logger;
        }

        // ── Core references ───────────────────────────────────────────────────

        /// <summary>The underlying AbstractGameState subclass for this game instance.</summary>
        public CardCounterGameState State { get; }

        public IRandomNumberService Rng { get; }
        public ILogger Logger { get; }

        /// <summary>The currently active FSM state. Set before entering the first state.</summary>
        public ICardCounterGameState CurrentFsmState { get; set; } = null!;

        /// <summary>
        /// Resolution stack used for multi-step interactions such as the Feeling Lucky chain
        /// and Comp'd responses. Bottom entry is the chain originator.
        /// </summary>
        public Stack<string> ResolutionStack { get; } = new();

        // ── Convenience accessors (delegate to State) ─────────────────────────

        public System.Collections.Concurrent.ConcurrentDictionary<string, PlayerState> GamePlayers => State.GamePlayers;
        public Stack<BaseCard> MainDeck => State.MainDeck;
        public Stack<BaseCard> CurrentShoe => State.CurrentShoe;
        public Stack<BaseCard> DiscardPile => State.DiscardPile;
        public Stack<string> ForceDrawStack => State.ForceDrawStack;
        public List<string> TurnOrder => State.TurnOrder;
        public GameConfig Config => State.Config;

        // ── Turn helpers ──────────────────────────────────────────────────────

        /// <summary>Player ID of the currently active player, or null if there are no players.</summary>
        public string? CurrentPlayerId =>
            TurnOrder.Count > 0 ? TurnOrder[State.CurrentPlayerIndex] : null;

        public bool IsCurrentPlayer(string playerId) => CurrentPlayerId == playerId;

        public PlayerState? GetCurrentPlayer() =>
            CurrentPlayerId is { } id && GamePlayers.TryGetValue(id, out var ps) ? ps : null;

        public PlayerState? GetPlayer(string playerId) =>
            GamePlayers.TryGetValue(playerId, out var ps) ? ps : null;

        /// <summary>Advances the turn pointer to the next player in TurnOrder (wraps around).</summary>
        public void AdvanceTurn() =>
            State.CurrentPlayerIndex = (State.CurrentPlayerIndex + 1) % TurnOrder.Count;

        // ── Card / deck helpers ───────────────────────────────────────────────

        /// <summary>Returns a random action card from the pool.</summary>
        public ActionCard GetRandomActionCard()
        {
            int index = Rng.GetRandomInt(0, ActionCardPool.Length, RandomType.Secure);
            return ActionCardPool[index];
        }

        /// <summary>
        /// Deals <see cref="GameConfig.ActionsDealtPerRound"/> action cards to every player.
        /// Cards are always dealt; players with more than <see cref="GameConfig.ActionHandLimit"/>
        /// cards afterward must discard via <see cref="CardCounterCommand.DiscardActionCardsCommand"/>.
        /// </summary>
        public void DealActionCards()
        {
            foreach (var player in GamePlayers.Values)
            {
                for (int i = 0; i < Config.ActionsDealtPerRound; i++)
                    player.ActionHand.Add(GetRandomActionCard());
            }
        }

        /// <summary>
        /// Deals the next shoe from the main deck, updating <see cref="CardCounterGameState.ShoeCardCounts"/>.
        /// Returns <c>true</c> if a shoe was dealt; <c>false</c> if the main deck is exhausted.
        /// </summary>
        public bool DealNextShoe()
        {
            CurrentShoe.Clear();
            State.ShoeCardCounts.Clear();

            if (MainDeck.Count == 0)
                return false;

            State.ShoeIndex++;
            int shoeSize = ComputeShoeSize();
            CurrentShoe.PushRange(MainDeck.PopRange(shoeSize));
            RecalculateShoeCounts();
            return true;
        }

        private int ComputeShoeSize()
        {
            int remaining = MainDeck.Count;
            int min = Config.MinShoeSize;
            int max = Config.MaxShoeSize;

            if (remaining <= min) return remaining;

            int maxAllowed = Math.Min(max, remaining - min);
            if (maxAllowed < min) return remaining;

            return Rng.GetRandomInt(min, maxAllowed + 1, RandomType.Secure);
        }

        /// <summary>Recomputes <see cref="CardCounterGameState.ShoeCardCounts"/> from the current shoe.</summary>
        public void RecalculateShoeCounts()
        {
            State.ShoeCardCounts.Clear();
            foreach (var card in CurrentShoe)
            {
                var type = card is NumberCard ? CardType.Number : CardType.Operator;
                State.ShoeCardCounts.TryGetValue(type, out int current);
                State.ShoeCardCounts[type] = current + 1;
            }
        }

        /// <summary>
        /// Decrements the shoe card count for a card that was just drawn or discarded.
        /// </summary>
        public void DecrementShoeCount(BaseCard drawn)
        {
            var type = drawn is NumberCard ? CardType.Number : CardType.Operator;
            if (State.ShoeCardCounts.TryGetValue(type, out int count))
            {
                if (count <= 1) State.ShoeCardCounts.Remove(type);
                else State.ShoeCardCounts[type] = count - 1;
            }
        }

        // ── Card application ──────────────────────────────────────────────────

        /// <summary>Appends a number card digit to the target player's pot.</summary>
        public void ApplyNumberCard(PlayerState player, NumberCard card) =>
            player.Pot.Add(card.Value);

        /// <summary>
        /// Applies an operator card to the target player: computes the new balance from
        /// the pot, then clears the pot. If the pot is empty, this is a no-op.
        /// Handles division by zero with a random event.
        /// </summary>
        public void ApplyOperatorCard(PlayerState player, OperatorCard card)
        {
            if (player.Pot.Count == 0)
                return;

            double potValue = player.PotValue;
            double balanceBefore = player.Balance;
            player.Pot.Clear();

            if (potValue == 0 && card.Op == Operator.Divide)
            {
                HandleDivisionByZero(player);
                State.LastOperatorResult = new OperatorResultInfo(
                    player.PlayerId, player.DisplayName, card.Op, balanceBefore, player.Balance);
                return;
            }

            player.Balance = card.Op switch
            {
                Operator.Add => player.Balance + potValue,
                Operator.Subtract => player.Balance - potValue,
                Operator.Multiply => Math.Round(player.Balance * potValue, MidpointRounding.AwayFromZero),
                Operator.Divide => potValue == 0
                    ? player.Balance
                    : Math.Round(player.Balance / potValue, MidpointRounding.AwayFromZero),
                _ => player.Balance + potValue
            };

            State.LastOperatorResult = new OperatorResultInfo(
                player.PlayerId, player.DisplayName, card.Op, balanceBefore, player.Balance);
        }

        // ── Discard history helpers ───────────────────────────────────────────

        /// <summary>Records a shoe card drawn by a player into the discard history.</summary>
        public void RecordDraw(PlayerState player, BaseCard card)
        {
            string desc = FormatBaseCard(card);
            string symbol = GetBaseCardSymbol(card);
            State.DiscardHistory.Add(new DiscardHistoryEntry(desc, symbol, player.DisplayName, false));
            State.LastDrawnCard = new LastDrawnCardInfo(player.PlayerId, player.DisplayName, card);
        }

        /// <summary>Records a shoe card drawn by a player (redirected by Not My Money) into the discard history.</summary>
        public void RecordRedirectedDraw(PlayerState drawer, PlayerState target, BaseCard card)
        {
            string desc = FormatBaseCard(card);
            string symbol = GetBaseCardSymbol(card);
            State.DiscardHistory.Add(new DiscardHistoryEntry(desc, symbol, drawer.DisplayName, false));
            State.LastDrawnCard = new LastDrawnCardInfo(drawer.PlayerId, drawer.DisplayName, card, target.PlayerId, target.DisplayName);
        }

        /// <summary>Records a shoe card burned (discarded without being drawn) into the discard history.</summary>
        public void RecordBurn(BaseCard card)
        {
            string desc = FormatBaseCard(card);
            string symbol = GetBaseCardSymbol(card);
            State.DiscardHistory.Add(new DiscardHistoryEntry($"{desc} (Burned)", "🔥", null, true));
        }

        /// <summary>Records an action card played by a player into the discard history.</summary>
        public void RecordActionCardPlay(PlayerState player, ActionCard card)
        {
            string name = GetActionCardName(card.Action);
            string symbol = GetActionCardSymbol(card.Action);
            State.DiscardHistory.Add(new DiscardHistoryEntry(name, symbol, player.DisplayName, true));
        }

        private static string FormatBaseCard(BaseCard card) => card switch
        {
            NumberCard nc => $"# {nc.Value}",
            OperatorCard oc => oc.Op switch
            {
                Operator.Add => "+",
                Operator.Subtract => "−",
                Operator.Multiply => "×",
                Operator.Divide => "÷",
                _ => "?"
            },
            _ => "?"
        };

        private static string GetBaseCardSymbol(BaseCard card) => card switch
        {
            NumberCard => "🔢",
            OperatorCard oc => oc.Op switch
            {
                Operator.Add => "➕",
                Operator.Subtract => "➖",
                Operator.Multiply => "✖️",
                Operator.Divide => "➗",
                _ => "➕"
            },
            _ => "🃏"
        };

        private static string GetActionCardName(ActionType action) => action switch
        {
            ActionType.FeelingLucky => "Feeling Lucky",
            ActionType.MakeMyLuck => "Make My Luck",
            ActionType.Skim => "Skim",
            ActionType.Burn => "Burn",
            ActionType.TurnTheTable => "Turn The Table",
            ActionType.Compd => "Comp'd",
            ActionType.NotMyMoney => "Not My Money",
            ActionType.Launder => "Launder",
            _ => action.ToString()
        };

        private static string GetActionCardSymbol(ActionType action) => action switch
        {
            ActionType.FeelingLucky => "🎲",
            ActionType.MakeMyLuck => "⭐",
            ActionType.Skim => "✂️",
            ActionType.Burn => "🔥",
            ActionType.TurnTheTable => "🔄",
            ActionType.Compd => "🛡️",
            ActionType.NotMyMoney => "💸",
            ActionType.Launder => "🧺",
            _ => "🃏"
        };

        private void HandleDivisionByZero(PlayerState player)
        {
            int roll = Rng.GetRandomInt(0, 4, RandomType.Secure);
            switch (roll)
            {
                case 0:
                    player.PassesRemaining++;
                    Logger.LogInformation("Div/0: player [{id}] gains a pass.", player.PlayerId);
                    break;
                case 1:
                    if (player.PassesRemaining > 0) player.PassesRemaining--;
                    Logger.LogInformation("Div/0: player [{id}] loses a pass.", player.PlayerId);
                    break;
                case 2:
                    if (player.ActionHand.Count < Config.ActionHandLimit)
                        player.ActionHand.Add(GetRandomActionCard());
                    Logger.LogInformation("Div/0: player [{id}] gains an action card.", player.PlayerId);
                    break;
                case 3:
                    if (player.ActionHand.Count > 0)
                    {
                        int idx = Rng.GetRandomInt(0, player.ActionHand.Count, RandomType.Secure);
                        player.ActionHand.RemoveAt(idx);
                    }
                    Logger.LogInformation("Div/0: player [{id}] loses an action card.", player.PlayerId);
                    break;
            }
        }
    }
}
