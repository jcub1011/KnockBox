using KnockBox.Extensions.Events;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.State.Games.CardCounter
{
    public class CardCounterGameState(
        User host,
        ILogger<CardCounterGameState> logger)
        : AbstractGameState(host, logger)
    {
        #region Events and Callbacks

        /// <summary>
        /// The event manager for the action cards dealt event.
        /// </summary>
        public readonly ThreadSafeEventManager ActionCardsDealtEventManager = new();

        /// <summary>
        /// The callback used to handle the action card played event.
        /// </summary>
        public Func<ActionCardPlayedArgs, Task>? ActionCardPlayedCallback { get; set; }

        /// <summary>
        /// The callback used to handle the shoe deal event.
        /// </summary>
        public Func<Task>? ShoeDealCallback { get; set; }

        /// <summary>
        /// The callback used to handle the game start event.
        /// </summary>
        public Func<Task>? GameStartCallback { get; set; }

        /// <summary>
        /// The callback used to handle the turn change event.
        /// </summary>
        public Func<Task>? TurnEndedCallback { get; set; }

        #endregion

        /// <summary>
        /// The current phase of the game.
        /// </summary>
        public GamePhase GamePhase { get; set; }

        /// <summary>
        /// The current phase of the round.
        /// </summary>
        public RoundPhase RoundPhase { get; set; }

        /// <summary>
        /// The states for all the players.
        /// </summary>
        public readonly Dictionary<string, Player> PlayerStates = [];

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
    }

    #region Enums

    public enum GamePhase
    {
        BuyIn,
        Playing,
        GameEnd
    }

    public enum RoundPhase
    {
        AwardActionCards,
        PopulateShoe,
        Play,
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

    /// <param name="Card">The card that was played.</param>
    /// <param name="User">The user that played the action card.</param>
    public record class ActionCardPlayedArgs(ActionCard Card, User User);

    public class Player(User user)
    {
        public readonly User User = user;
        public readonly List<NumberCard> Pot = [];
        public readonly List<ActionCard> ActionCards = [];

        /// <summary>
        /// The buy in roll this player started with.
        /// </summary>
        public long BuyInRoll { get; set; } = 0L;

        /// <summary>
        /// The current balance of this player. Includes the <see cref="BuyInRoll"/>.
        /// </summary>
        public long Balance { get; set; } = 0L;

        /// <summary>
        /// The number of passes this player has left.
        /// </summary>
        public int RemainingPasses { get; set; } = 0;

        /// <summary>
        /// Gets the concatenated value of the pot.
        /// </summary>
        /// <returns></returns>
        public ValueResult<long> GetPotValue()
        {
            if (Pot.Count == 0) return 0L;

            string concatenated = string.Join("", Pot.Select(p => p.Value));
            return long.TryParse(concatenated, out var result)
                ? result : ValueResult<long>.FromError("Error parsing pot value.");
        }

        /// <summary>
        /// Resets all the values in this player.
        /// </summary>
        public void Reset()
        {
            Pot.Clear();
            ActionCards.Clear();
            Balance = 0L;
            BuyInRoll = 0L;
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