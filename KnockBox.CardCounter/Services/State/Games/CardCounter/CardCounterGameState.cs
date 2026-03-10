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
        /// Gets the id of the current player in the turn order.
        /// </summary>
        public string CurrentPlayer => TurnOrder[CurrentPlayerIndex];

        /// <summary>
        /// Gets the player state of the current player in the turn order. Null when the current player does not have a state defined.
        /// </summary>
        public PlayerState? CurrentPlayerState => GamePlayers.TryGetValue(CurrentPlayer, out var state) ? state : null;

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
        /// Ordered history of cards drawn or action cards played, for the discard pile display.
        /// Append-only; newest entry is at the end.
        /// </summary>
        public readonly List<DiscardHistoryEntry> DiscardHistory = [];

        /// <summary>
        /// True during the brief window when a new shoe has just been dealt.
        /// Used by the UI to trigger the shoe-dealing animation.
        /// </summary>
        public bool IsNewShoe { get; set; }

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
        /// The most recently drawn shoe card, shown to all players as the latest draw event.
        /// Remains visible until superseded by another draw/action event.
        /// </summary>
        public LastDrawnCardInfo? LastDrawnCard { get; set; }

        /// <summary>
        /// Set when the active player has drawn an operator and must choose a target for the
        /// Not My Money redirect. The UI should show target selection while this is true.
        /// </summary>
        public bool IsNotMyMoneySelecting { get; set; }

        /// <summary>
        /// The operator card currently being redirected by Not My Money.
        /// </summary>
        public Operator? PendingNotMyMoneyOperator { get; set; }

        /// <summary>
        /// Records the most recent operator application for the affected player to review.
        /// Set each time an operator card is applied to a player's balance.
        /// </summary>
        public OperatorResultInfo? LastOperatorResult { get; set; }

        /// <summary>
        /// Set when a Hedge Your Bet card has been played. Contains the ID of the player who
        /// played it; the next card drawn from the shoe will be converted to an Add operator
        /// if that player's balance is negative, or a Subtract operator otherwise.
        /// Cleared as soon as the next card is drawn.
        /// </summary>
        public string? HedgeYourBetPlayerId { get; set; }

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
        Launder,
        Tilt,
        HedgeYourBet,
        LetItRide
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
        ActionCard PlayedCard,
        int? SourceDigitIndex = null,
        int? TargetDigitIndex = null,
        Operator? NotMyMoneyOperator = null);

    /// <summary>
    /// Information about the most recently drawn shoe card, shown to all players as an overlay.
    /// </summary>
    public record LastDrawnCardInfo(
        string DrawerId,
        string DrawerName,
        BaseCard Card,
        string? RedirectTargetId = null,
        string? RedirectTargetName = null);

    /// <summary>A single entry in the visible discard pile history.</summary>
    public record DiscardHistoryEntry(
        string Description,
        string Symbol,
        string? PlayerName,
        bool IsActionCard);

    /// <summary>
    /// Records the result of an operator card being applied to a player's balance.
    /// Used to show the affected player an overlay with before/after balance.
    /// </summary>
    public record OperatorResultInfo(
        string PlayerId,
        string PlayerName,
        Operator Op,
        double BalanceBefore,
        double BalanceAfter);

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
        public int PlayerTurnTimeoutMs { get; set; } = 15000;
        public int BuyInTimeoutMs { get; set; } = 20000;
        public int RoundEndTimeoutMs { get; set; } = 20000;
        public int FeelingLuckyChainTimeoutMs { get; set; } = 12000;
        public int MakeMyLuckTimeoutMs { get; set; } = 12000;
        public int NotMyMoneyTimeoutMs { get; set; } = 12000;
        public int SkimTimeoutMs { get; set; } = 12000;
        public int WaitingForReactionTimeoutMs { get; set; } = 12000;
        public bool EnableActionTimer { get; set; } = true;
        public bool ShowMakeMyMoneyOperator { get; set; } = true;
        public bool FlipWinCondition { get; set; } = false;

        /// <summary>
        /// When true, players have no pot. Drawing a number card applies it directly to the
        /// player's balance using their Active Operator. Drawing an operator card replaces the
        /// player's Active Operator. Skim and Turn The Table are not distributed in this mode;
        /// Turn The Table is repurposed to reverse balance digits when played.
        /// </summary>
        public bool ActiveOperatorMode { get; set; } = false;

        // ── Action card deal-weights ─────────────────────────────────────────
        // Higher value → more likely to be dealt. 0 removes the card from the deal pool entirely.

        public int FeelingLuckyWeight { get; set; } = 10;
        public int MakeMyLuckWeight { get; set; } = 10;
        public int SkimWeight { get; set; } = 10;
        public int BurnWeight { get; set; } = 10;
        public int TurnTheTableWeight { get; set; } = 10;
        public int CompdWeight { get; set; } = 10;
        public int NotMyMoneyWeight { get; set; } = 10;
        public int LaunderWeight { get; set; } = 10;
        public int TiltWeight { get; set; } = 1;
        public int HedgeYourBetWeight { get; set; } = 10;
        public int LetItRideWeight { get; set; } = 10;
    }

    #endregion
}
