using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;
using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.CardCounter
{
    public class CardCounterGameState(
        User host,
        ILogger<CardCounterGameState> logger)
        : AbstractGameState(host, logger)
    {
        /// <summary>
        /// The current phase of the game.
        /// </summary>
        public GamePhase Phase { get; set; }

        /// <summary>
        /// The cards in the main deck.
        /// </summary>
        public readonly Stack<BaseCard> MainDeck = [];

        /// <summary>
        /// The cards in this round.
        /// </summary>
        public readonly Stack<BaseCard> CurrentShoe = [];

        /// <summary>
        /// The current round index.
        /// </summary>
        public int ShoeIndex { get; set; }
        
        /// <summary>
        /// The count of operators in this round.
        /// </summary>
        public readonly Dictionary<Operator, int> OperatorCounts = [];

        /// <summary>
        /// The count of value cards in this round.
        /// </summary>
        public readonly Dictionary<int, int> ValueCounts = [];

        /// <summary>
        /// The discard pile for the whole game.
        /// </summary>
        public readonly Stack<BaseCard> DiscardPile = [];

        /// <summary>
        /// The deck for action cards.
        /// </summary>
        public readonly Stack<ActionCard> ActionDeck = [];

        /// <summary>
        /// The force drawn players. The bottom of the stack is the player that initiated the force draw chain.
        /// </summary>
        public readonly Stack<string> ForceDrawStack = [];

        /// <summary>
        /// The players in turn order.
        /// </summary>
        public readonly List<string> TurnOrder = [];

        /// <summary>
        /// The current player turn.
        /// </summary>
        public int CurrentPlayerIndex { get; set; }

        /// <summary>
        /// The configuration for the game.
        /// </summary>
        public GameConfig Config { get; set; } = new();

        /// <summary>
        /// The states for all the players.
        /// </summary>
        public ConcurrentDictionary<string, Player> PlayerStates { get; set; } = new();
    }

    #region Enums

    public enum GamePhase
    {
        GameStart,
        Playing,
        GameEnd
    }

    public enum Operator
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    public enum ActionType
    {
        FeelingLucky,
        MakeMyLuck,
        Skim,
        Burn,
        TurnTheTable,
        Compd,
        NotMyMoney,
        Launder
    }

    #endregion

    #region Data Containers

    /// <summary>
    /// The base class for cards.
    /// </summary>
    /// <param name="Description"></param>
    public record class BaseCard(string Description);

    public record class OperatorCard(Operator Operator, string Description) : BaseCard(Description);

    public record class NumberCard(long Value, string Description) : BaseCard(Description);

    public record class ActionCard(ActionType Action, string Description) : BaseCard(Description);

    public class Player(User user)
    {
        public readonly User User = user;
        public readonly List<NumberCard> Pot = [];
        public readonly List<ActionCard> ActionCards = [];
        public long Balance { get; set; } = 0L;
        public long PotValue 
        { 
            get
            {
                if (Pot.Count == 0) return 0L;
                return long.TryParse(string.Join("", Pot.Select(p => p.Value)), out var result) ? result : 0L;
            } 
        }

        public ValueResult<long> GetPotValue()
        {
            if (Pot.Count == 0) return 0L;

            string concatenated = string.Join("", Pot.Select(p => p.Value));
            return long.TryParse(concatenated, out var result)
                ? result : ValueResult<long>.FromError("Error parsing pot value.");
        }
    }

    public class GameConfig
    {
        public int DeckSize { get; set; } = 52;
        public float NumberToOperatorRatio { get; set; } = 4.0f;
        public float AddSubToMulDivRatio { get; set; } = 4.0f;
        public int ActionsDealtPerRound { get; set; } = 3;
        public int ActionHandLimit { get; set; } = 6;
        public int TotalPassesPerPlayer { get; set; } = 3;
        public int MinShoeSize { get; set; } = 12;
        public int MaxShoeSize { get; set; } = 20;
        public int ActionResponseTimeoutMs { get; set; } = 15000;
    }

    #endregion
}