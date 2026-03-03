using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;
using KnockBox.Extensions;
using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.CardCounter
{
    public class CardCounterGameState(
        User host,
        ILogger<CardCounterGameState> logger)
        : AbstractGameState(host, logger)
    {
        public GamePhase Phase { get; set; }
        public List<Card> MainDeck { get; set; } = new();
        public List<Card> CurrentShoe { get; set; } = new();
        public int ShoeIndex { get; set; }
        public Dictionary<CardType, int> ShoeCardCounts { get; set; } = new();
        public List<Card> DiscardPile { get; set; } = new();
        public List<ActionCard> ActionDeck { get; set; } = new();
        public int CurrentPlayerIndex { get; set; }
        public List<string> TurnOrder { get; set; } = new();
        public GameConfig Config { get; set; } = new();

        public ConcurrentDictionary<string, PlayerState> GamePlayers { get; set; } = new();

        public PendingAction? CurrentPendingAction { get; set; }
        public ForcedDrawChain? PendingChain { get; set; }
    }

    #region Enums

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

    #endregion
}