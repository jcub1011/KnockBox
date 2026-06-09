using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Components;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.CardCounter.Services.State.Games
{
    public class CardCounterGameState(
        User host,
        ILogger<CardCounterGameState> logger)
        : AbstractGameState(host, logger),
          IPhasedGameState<GamePhase>,
          IPlayerTrackedGameState<PlayerState>,
          IFsmContextGameState<CardCounterGameContext>
    {
        /// <summary>
        /// The FSM context for this game instance. Set when the game starts.
        /// </summary>
        public CardCounterGameContext? Context { get; set; }

        /// <summary>
        /// The current phase of the game.
        /// </summary>
        public GamePhase Phase { get; private set; }

        /// <summary>
        /// Updates the current phase. Notification is intentionally NOT raised here —
        /// callers run inside <c>Execute</c>/<c>ExecuteAsync</c>, which fires
        /// <c>NotifyStateChanged</c> exactly once after the lock is released.
        /// Calling Notify inline would run subscribers while the executeLock is held
        /// and can deadlock the Blazor dispatcher (see arch doc:
        /// "Notify outside the lock").
        /// </summary>
        public void SetPhase(GamePhase phase) => Phase = phase;

        /// <summary>
        /// Manages turn order and active player tracking.
        /// </summary>
        public TurnManager TurnManager { get; } = new();

        /// <summary>
        /// Gets the id of the current player in the turn order.
        /// </summary>
        public Guid? CurrentPlayer => TurnManager.CurrentPlayer;

        /// <summary>
        /// Gets the player state of the current player in the turn order. Null when the current player does not have a state defined.
        /// </summary>
        public PlayerState? CurrentPlayerState => CurrentPlayer is { } cp && GamePlayers.TryGetValue(cp, out var state) ? state : null;

        /// <summary>
        /// All player states, keyed by player ID.
        /// </summary>
        public ConcurrentDictionary<Guid, PlayerState> GamePlayers { get; } = new();

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
        public readonly Stack<Guid> ForceDrawStack = new();

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
        public Guid? FeelingLuckyTargetId { get; set; }

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
        /// Records the most recent operator card draw in Active Operator Mode.
        /// Set each time a player's active operator changes due to drawing an operator card.
        /// </summary>
        public OperatorChangeInfo? LastOperatorChange { get; set; }

        /// <summary>
        /// Set when a Hedge Your Bet card has been played. Contains the ID of the player who
        /// played it; the next card drawn from the shoe will be converted to an Add operator
        /// if that player's balance is negative, or a Subtract operator otherwise.
        /// Cleared as soon as the next card is drawn.
        /// </summary>
        public Guid? HedgeYourBetPlayerId { get; set; }

        /// <summary>
        /// Host-configurable match rules. Always replaced atomically via UpdateSettings;
        /// the setter is private so callers can't bypass the lock. Persisted to the host's
        /// browser localStorage by the lobby page so preferred rules survive across sessions.
        /// </summary>
        public CardCounterSettings Settings { get; private set; } = new();

        /// <summary>
        /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s result
        /// inside <see cref="AbstractGameState.Execute(Action)"/>, so subscribers observe a
        /// single consistent transition and notification fires once after the lock releases.
        /// </summary>
        public Result UpdateSettings(Func<CardCounterSettings, CardCounterSettings> mutate) =>
            Execute(() => { Settings = mutate(Settings); });
    }

    // The game's enums, card hierarchy, and notification POCOs moved to
    // KnockBox.CardCounter.Contracts (shared with the WASM client). They resolve here
    // via the project-wide global using declared in KnockBox.CardCounter.csproj.
}
