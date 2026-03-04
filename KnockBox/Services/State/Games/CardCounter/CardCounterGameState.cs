using KnockBox.Services.Logic.Games.CardCounter.FSM;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.CardCounter
{
    public class CardCounterGameState(
        User host,
        ILogger<CardCounterGameState> logger)
        : AbstractGameState(host, logger)
    {
        /// <summary>
        /// The FSM context for this game instance. Set when the game starts.
        /// </summary>
        public CardCounterGameContext? Context { get; internal set; }

        /// <summary>
        /// The current phase of the game.
        /// </summary>
        public GamePhase GamePhase { get; set; }

        /// <summary>
        /// Players in turn order (by player ID).
        /// </summary>
        public readonly List<string> TurnOrder = [];

        /// <summary>
        /// Index into <see cref="TurnOrder"/> identifying the active player.
        /// </summary>
        public int CurrentPlayerIndex { get; set; }

        /// <summary>
        /// All player states, keyed by player ID.
        /// </summary>
        public readonly ConcurrentDictionary<string, PlayerState> GamePlayers = new();

        /// <summary>
        /// Current shoe index (incremented each time a new shoe is dealt).
        /// </summary>
        public int ShoeIndex { get; set; }

        /// <summary>
        /// Visible card-type counts for the current shoe, updated as cards are drawn.
        /// </summary>
        public readonly Dictionary<CardType, int> ShoeCardCounts = [];

        // ── Internal deck data (managed by engine / FSM states) ──────────────

        public readonly Stack<BaseCard> MainDeck = new();
        public readonly Stack<BaseCard> CurrentShoe = new();
        public readonly Stack<BaseCard> DiscardPile = new();

        /// <summary>
        /// Tracks the Feeling Lucky chain: bottom entry is the originator.
        /// </summary>
        public readonly Stack<string> ForceDrawStack = new();

        /// <summary>
        /// Information about the most recently played action card (for all-player notification).
        /// Cleared at the start of the next player's turn.
        /// </summary>
        public LastPlayedActionInfo? LastPlayedAction { get; set; }

        /// <summary>
        /// Set while a blockable action (Skim, TurnTheTable, Launder) is pending a reaction.
        /// </summary>
        public PendingReactionInfo? PendingReaction { get; set; }

        /// <summary>
        /// Set during a Feeling Lucky chain to indicate which player must respond.
        /// </summary>
        public string? FeelingLuckyTargetId { get; set; }

        /// <summary>
        /// Game configuration (tunable playtesting values).
        /// </summary>
        public GameConfig Config { get; set; } = new();
    }

    #region Enums

    public enum GamePhase
    {
        BuyIn,
        Playing,
        GameOver
    }

    /// <summary>
    /// Distinguishes the two card types tracked in <see cref="CardCounterGameState.ShoeCardCounts"/>.
    /// </summary>
    public enum CardType
    {
        Number,
        Operator
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

    #region Cards

    public abstract record BaseCard;

    /// <summary>A number card (digit 0–9) drawn into the player's pot.</summary>
    public record NumberCard(int Value) : BaseCard;

    /// <summary>An operator card that applies arithmetic to the player's pot and balance.</summary>
    public record OperatorCard(Operator Op) : BaseCard;

    /// <summary>An action card drawn from the separate action deck.</summary>
    public record ActionCard(ActionType Action) : BaseCard;

    #endregion

    #region Supporting Types

    /// <summary>Information about the most recently played action card, shown to all players.</summary>
    public record LastPlayedActionInfo(
        string PlayerId,
        string PlayerName,
        ActionType Action,
        string? TargetId,
        string? TargetName);

    /// <summary>Information about a pending blockable reaction (Skim, TurnTheTable, Launder).</summary>
    public record PendingReactionInfo(
        string SourceId,
        string SourceName,
        string TargetId,
        ActionCard PlayedCard);

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