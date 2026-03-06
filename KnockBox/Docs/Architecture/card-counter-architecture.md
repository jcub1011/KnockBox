# Card Counter — Game Architecture

## Overview

Card Counter is a multiplayer card game implemented as a game module within the KnockBox platform. Players manipulate a numeric balance through card draws, using concatenation, arithmetic operators, and action cards to steer their balance as close to zero as possible. This document describes how the game's rules, state, and UI map onto the platform's `AbstractGameEngine` base class and FSM (Finite State Machine) state system.

---

## Integration Point

Card Counter registers as a singleton `AbstractGameEngine` subclass. All game UI lives on a single Razor page at `/room/card-counter/{ObfuscatedRoomCode}`.

```
 ┌──────────────────────────────────────────────────────────┐
 │                  Platform (Shared)                       │
 │                                                          │
 │  LobbyService ──► LobbyRegistration ──► AbstractGameState│
 │                                                          │
 │  Game Engines (Singleton / DI)                           │
 │  ┌──────────────────┐ ┌──────────────────┐ ┌──────────┐ │
 │  │ DiceSimulator    │ │ CardCounter      │ │ Future...│ │
 │  │ GameEngine       │ │ GameEngine       │ │          │ │
 │  └──────────────────┘ └────────┬─────────┘ └──────────┘ │
 │                                │                         │
 └────────────────────────────────┼─────────────────────────┘
                                  │
                   ┌──────────────┼──────────────┐
                   ▼              ▼              ▼
             Lobby View    Gameplay View   GameOver View
             (single Razor page, phase-switched)
```

### Registration

```csharp
// In LogicRegistrations.RegisterLogic()
services.AddSingleton<CardCounterGameEngine>();
```

The engine is resolved by `LobbyService.CreateLobbyAsync` via a `GameType` switch:

```csharp
GameType.CardCounter => serviceProvider.GetService<CardCounterGameEngine>(),
```

---

## FSM Architecture

Card Counter uses a **Finite State Machine (FSM)** to manage game flow. All gameplay logic lives in discrete state classes rather than directly in the engine. The engine acts as a dispatcher — it receives commands from the Razor page, acquires the state lock, and delegates processing to the current FSM state.

### Core FSM Components

```
CardCounterGameEngine (Singleton)
│
├── ProcessCommand(context, command) ── routes to current FSM state
├── Tick(context, now)               ── drives time-based transitions
└── TransitionTo(context, nextState) ── calls OnEnter on the new state
        │
        ▼
CardCounterGameContext (per-game instance)
│
├── State           : CardCounterGameState   // underlying game data
├── Rng             : IRandomNumberService   // shared random number source
├── Logger          : ILogger
├── CurrentFsmState : ICardCounterGameState  // active FSM node
└── ResolutionStack : Stack<string>          // used for Feeling Lucky chain
```

### ICardCounterGameState Interface

Each FSM state implements:

```csharp
public interface ICardCounterGameState
{
    void OnEnter(CardCounterGameContext context);
    ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command);
    ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now);
}
```

`OnEnter` is called once when the FSM enters the state (sets phase, configures timers, etc.). `HandleCommand` processes a player command and returns the next state (or `null` to stay). `Tick` handles time-based transitions such as timeouts.

### FSM States

| State | Description |
|---|---|
| `BuyInState` | Collects each player's buy-in sign (positive / negative). Transitions to `RoundEndState` when all players have submitted. |
| `PlayerTurnState` | The standard turn loop. Active player may play action cards, fold, draw, or pass. |
| `WaitingForReactionState` | Waiting for the target of a blockable action (Skim, Turn The Table, Launder) to respond with Comp'd or accept. Has a server-side timeout. |
| `FeelingLuckyChainState` | Propagates a Feeling Lucky forced-draw chain clockwise. Target must draw, pass it on, or block with Comp'd. Has a server-side timeout. |
| `MakeMyLuckState` | Waiting for the acting player to submit a reorder of the top shoe cards revealed by Make My Luck. Has a server-side timeout. |
| `SkimState` | Waiting for the Skim source player to select which digits to swap. Has a server-side timeout. |
| `NotMyMoneyState` | Waiting for the active player to select a redirect target after drawing an operator with Not My Money active. |
| `RoundEndState` | Transient: deals action cards, then deals the next shoe (or ends the game). Immediately transitions; no player interaction. |
| `GameOverState` | Terminal state: sets `GamePhase.GameOver`. Accepts no further commands. |

---

## Command System

All player interactions are expressed as **typed command records**. The Razor page calls an engine method (e.g., `GameEngine.DrawCard(user, state)`), the engine creates the appropriate command, and `ProcessCommand` dispatches it to the current FSM state inside `state.Execute()`.

```csharp
public abstract record CardCounterCommand(string PlayerId);

public record DrawCardCommand(string PlayerId)                                   : CardCounterCommand(PlayerId);
public record PassTurnCommand(string PlayerId)                                   : CardCounterCommand(PlayerId);
public record FoldPotCommand(string PlayerId)                                    : CardCounterCommand(PlayerId);
public record SetBuyInCommand(string PlayerId, bool IsNegative)                  : CardCounterCommand(PlayerId);
public record PlayActionCardCommand(string PlayerId, int CardIndex,
                                    string? TargetPlayerId = null)               : CardCounterCommand(PlayerId);
public record SubmitReorderCommand(string PlayerId, int[] ReorderedIndices)      : CardCounterCommand(PlayerId);
public record AcceptPendingCommand(string PlayerId)                              : CardCounterCommand(PlayerId);
public record DiscardActionCardsCommand(string PlayerId, int[] CardIndices)      : CardCounterCommand(PlayerId);
public record SkimSelectCommand(string PlayerId,
                                int SourceDigitIndex, int TargetDigitIndex)     : CardCounterCommand(PlayerId);
public record DismissDrawnCardCommand(string PlayerId)                          : CardCounterCommand(PlayerId);
public record NotMyMoneySelectTargetCommand(string PlayerId,
                                            string TargetPlayerId)              : CardCounterCommand(PlayerId);
public record NotMyMoneyCancelCommand(string PlayerId)                          : CardCounterCommand(PlayerId);
```

---

## Game State Model

All mutable state lives on `CardCounterGameState : AbstractGameState`. The FSM context (`CardCounterGameContext`) holds a reference to this state and acts as a façade for helpers used by FSM states.

### CardCounterGameState

```
CardCounterGameState : AbstractGameState
│
│  Inherited from AbstractGameState:
│  ├── Host                     : User
│  ├── Players                  : IReadOnlyList<User>
│  ├── IsJoinable               : bool
│  ├── StateChangedEventManager : ThreadSafeEventManager
│  ├── Execute()                : serialized mutation + notify
│  ├── ScheduleCallback()       : delayed state transitions
│  └── PlayerUnregistered       : event Action<User>
│
│  Game-specific properties:
├── Context              : CardCounterGameContext?   // null until game starts
├── GamePhase            : GamePhase                 // BuyIn | Playing | GameOver
├── TurnOrder            : List<string>              // player IDs in turn order
├── CurrentPlayerIndex   : int                       // index into TurnOrder
├── GamePlayers          : ConcurrentDictionary<string, PlayerState>
│
│  Deck and shoe data:
├── MainDeck             : Stack<BaseCard>            // full shuffled deck
├── CurrentShoe          : Stack<BaseCard>            // active shoe subset
├── DiscardPile          : Stack<BaseCard>            // drawn/discarded cards
├── ShoeIndex            : int                        // incremented each shoe
├── ShoeCardCounts       : Dictionary<CardType, int>  // visible type counts
├── IsNewShoe            : bool                       // triggers shoe-deal animation
│
│  Interaction / reaction state:
├── PendingReaction      : PendingReactionInfo?       // blockable action in flight
├── FeelingLuckyTargetId : string?                    // current Feeling Lucky target
├── IsNotMyMoneySelecting: bool                       // operator redirect in progress
├── PendingNotMyMoneyOperator : Operator?             // operator being redirected
├── ForceDrawStack       : Stack<string>              // Feeling Lucky chain participants
│
│  UI / display state:
├── LastPlayedAction     : LastPlayedActionInfo?      // all-player action notification
├── LastDrawnCard        : LastDrawnCardInfo?         // most recently drawn shoe card
├── DiscardHistory       : List<DiscardHistoryEntry>  // visible discard timeline
│
└── Config               : GameConfig                 // tunable playtesting values
```

### GamePhase Enum

```csharp
public enum GamePhase
{
    BuyIn,    // Players choose positive or negative starting balance
    Playing,  // Active turn-based gameplay
    GameOver  // Main deck exhausted; final balances ranked
}
```

Note: `RoundEndState` is an internal FSM transition state only — it is not a user-visible `GamePhase` value. The phase remains `Playing` across shoe boundaries.

### PlayerState

```
PlayerState
├── PlayerId          : string
├── DisplayName       : string
├── Balance           : double
├── Pot               : List<int>         // ordered digit list
├── PotValue          : double             // computed: concatenated digits, ignoring leading zeros
├── PassesRemaining   : int
├── HasSetBuyIn       : bool
├── BuyInRoll         : int               // server-generated die result (1–6)
├── ActionHand        : List<ActionCard>  // player's hidden action cards
└── PrivateReveal     : List<BaseCard>?   // Make My Luck top-3 reveal (non-null during reorder)
```

### Card Representation

```csharp
public abstract record BaseCard;
public record NumberCard(int Value)    : BaseCard;   // digit 0–9
public record OperatorCard(Operator Op): BaseCard;   // arithmetic operator
public record ActionCard(ActionType Action) : BaseCard;

public enum Operator    { Add, Subtract, Multiply, Divide }

public enum ActionType
{
    FeelingLucky,   // Force Draw — pushes a forced draw to the next player
    MakeMyLuck,     // Alter the Future — reveals and reorders top 3 shoe cards
    Skim,           // Swap a Digit — swaps a digit between two pots
    Burn,           // Discard top card from shoe without applying it
    TurnTheTable,   // Flip pot — reverses the digit order in target's pot
    Compd,          // Shield / Block — counters a blockable action
    NotMyMoney,     // Redirect operator — redirects a drawn operator to a target
    Launder,        // Swap pots — exchanges pot contents between two players
    Tilt            // Redistribute — shuffles and evenly redistributes all pots
}
```

### Supporting Types

```csharp
public record LastPlayedActionInfo(
    string PlayerId,
    string PlayerName,
    ActionType Action,
    string? TargetId,
    string? TargetName);

public record PendingReactionInfo(
    string SourceId,
    string SourceName,
    string TargetId,
    ActionCard PlayedCard);

public record LastDrawnCardInfo(
    string DrawerId,
    string DrawerName,
    BaseCard Card,
    string? RedirectTargetId = null,
    string? RedirectTargetName = null);

public record DiscardHistoryEntry(
    string Description,
    string Symbol,
    string? PlayerName,
    bool IsActionCard);
```

---

## Control Flow

The following diagram shows the lifecycle of a single player action during gameplay.

```
  Player A (Razor Page)        CardCounterGameEngine      CardCounterGameState     Player B (Razor Page)
  ─────────────────────        ─────────────────────      ────────────────────     ─────────────────────
          │                             │                        │                        │
          │  engine.DrawCard(           │                        │                        │
          │    player, state)           │                        │                        │
          │ ───────────────────────────>│                        │                        │
          │                             │                        │                        │
          │                             │  state.Execute(() =>   │                        │
          │                             │  {                     │                        │
          │                             │    next = currentFSM   │                        │
          │                             │      .HandleCommand(   │                        │
          │                             │        ctx, cmd);      │                        │
          │                             │    if (next != null)   │                        │
          │                             │      TransitionTo(next)│                        │
          │                             │  })                    │                        │
          │                             │ ──────────────────────>│                        │
          │                             │                        │                        │
          │                             │               ┌────────────────────┐             │
          │                             │               │  Acquire lock      │             │
          │                             │               │  FSM.HandleCommand │             │
          │                             │               │  [optional]        │             │
          │                             │               │  TransitionTo(next)│             │
          │                             │               │  Release lock      │             │
          │                             │               │  NotifyChanged()   │             │
          │                             │               └────────────────────┘             │
          │                             │                        │                        │
          │            Result           │                        │                        │
          │ <───────────────────────────│                        │                        │
          │                             │                        │  callback: re-render   │
          │ callback: re-render <────────────────────────────────│───────────────────────>│
          │                             │                        │                        │
       [UI updates]                     │                        │                  [UI updates]
```

All state mutations are serialized through `state.Execute()` on `CardCounterGameState`. This means FSM state transitions, command processing, and notifications all flow through the same lock, preventing race conditions.

---

## Engine Methods

`CardCounterGameEngine` extends `AbstractGameEngine` and exposes public methods that the Razor page calls directly. Each public method creates a typed command and calls `ProcessCommand`.

### Lifecycle Methods (inherited contract)

```csharp
Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
```
Creates a `CardCounterGameState`, sets `IsJoinable = true`, subscribes `HandlePlayerLeft` to `PlayerUnregistered`.

```csharp
Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
```
Validates the caller is the host, closes joinability, creates the `CardCounterGameContext`, calls `InitializeGame`, then transitions to `BuyInState`.

### FSM Core Methods

```csharp
Result ProcessCommand(CardCounterGameContext context, CardCounterCommand command)
```
Calls `state.Execute(() => { next = currentFsmState.HandleCommand(ctx, cmd); if (next != null) TransitionTo(ctx, next); })`.

```csharp
Result Tick(CardCounterGameContext context, DateTimeOffset now)
```
Calls `state.Execute(() => { next = currentFsmState.Tick(ctx, now); if (next != null) TransitionTo(ctx, next); })`. The Razor page drives ticks via a 1-second `PeriodicTimer`.

### Game-Specific Public Methods

Each wraps a command creation and `ProcessCommand` call:

| Method | Command Created |
|---|---|
| `SetBuyIn(User, CardCounterGameState, bool isNegative)` | `SetBuyInCommand` |
| `DrawCard(User, CardCounterGameState)` | `DrawCardCommand` |
| `PassTurn(User, CardCounterGameState)` | `PassTurnCommand` |
| `FoldPot(User, CardCounterGameState)` | `FoldPotCommand` |
| `PlayActionCard(User, CardCounterGameState, int cardIndex, string? targetPlayerId)` | `PlayActionCardCommand` |
| `AcceptPending(User, CardCounterGameState)` | `AcceptPendingCommand` |
| `SubmitReorder(User, CardCounterGameState, int[] reorderedIndices)` | `SubmitReorderCommand` |
| `DiscardActionCards(User, CardCounterGameState, int[] cardIndices)` | `DiscardActionCardsCommand` |
| `SkimSelect(User, CardCounterGameState, int sourceDigitIndex, int targetDigitIndex)` | `SkimSelectCommand` |
| `NotMyMoneySelectTarget(User, CardCounterGameState, string targetPlayerId)` | `NotMyMoneySelectTargetCommand` |
| `NotMyMoneyCancel(User, CardCounterGameState)` | `NotMyMoneyCancelCommand` |
| `ResetGame(User host, CardCounterGameState)` | *(direct execute — resets state and re-runs InitializeGame)* |

---

## Game Phases

```
  ┌────────┐   all players      ┌───────────────────────────────┐
  │ BuyIn  │ ── set buy-in ───► │         Playing               │◄─── new shoe dealt
  └────────┘                    │  (PlayerTurnState FSM node)   │
                                └──────────────┬────────────────┘
                                               │
                                       shoe exhausted
                                               │
                                               ▼
                                  ┌───────────────────────┐
                                  │    RoundEndState       │  (internal FSM, not a GamePhase value)
                                  │  deals action cards,   │
                                  │  deals next shoe       │
                                  └─────────┬──────────────┘
                                            │
                               ┌────────────┴─────────────┐
                               │                           │
                          more shoes                  deck empty
                               │                           │
                               ▼                           ▼
                            Playing                 ┌───────────┐
                                                    │ GameOver  │
                                                    └───────────┘
```

### BuyIn Phase

Each player receives a server-generated die roll (1–6, stored as `BuyInRoll`), sees the result multiplied by 8, and chooses positive or negative as their starting balance. The engine waits for all players to submit via `SetBuyIn`. When all have set, `BuyInState` transitions to `RoundEndState`, which deals the first shoe and action cards, then transitions to `PlayerTurnState`.

### Playing Phase

The active phase where turns execute through `PlayerTurnState`. Sub-states (`WaitingForReactionState`, `FeelingLuckyChainState`, `MakeMyLuckState`, `SkimState`, `NotMyMoneyState`) handle multi-step interaction patterns. The `GamePhase` remains `Playing` throughout all sub-states and across shoe transitions.

### GameOver Phase

Triggered when `RoundEndState` finds the main deck exhausted. Balances are compared and the winner is determined.

---

## Turn Flow Detail

```
  Player's Turn Begins (PlayerTurnState.OnEnter)
         │
         ├── Clears: LastPlayedAction, FeelingLuckyTargetId, IsNewShoe
         ▼
  ┌─────────────────┐
  │ Play Action Card │◄──── (optional, repeatable — same FSM state)
  │  from hand       │
  └────────┬────────┘
           │
           ▼
  ┌─────────────────┐
  │ Fold? (optional) │──── consumes pass, clears pot, turn continues
  └────────┬────────┘
           │
           ▼
  ┌─────────────────────┐
  │ Draw OR Pass         │──── ends turn (advances CurrentPlayerIndex)
  └─────────┬───────────┘
            │
     ┌──────┴──────┐
     ▼             ▼
  Draw Card     Pass (costs 1 pass)
     │
     ├── NumberCard → ApplyNumberCard → append digit to pot
     │
     └── OperatorCard
            │
            ├── IsNotMyMoneySelecting → transition to NotMyMoneyState
            │
            ├── Pot empty → no-op
            │
            ├── Division by 0 → HandleDivisionByZero (random event)
            │
            └── Apply: Balance = Balance [op] PotValue
                       Clear pot
```

---

## Action Card Resolution

### Blockable Actions (Skim, TurnTheTable, Launder)

These actions transition to `WaitingForReactionState`. The target may respond with Comp'd or accept. A server-side timeout causes automatic acceptance.

1. Player A plays the card → `PlayActionCardCommand` → `PlayerTurnState.HandleCommand`
2. State transitions to `WaitingForReactionState(sourceId, targetId, pendingCard)`
3. `OnEnter` sets `PendingReaction` on the game state (signals the UI)
4. Target responds:
   - **Comp'd**: card is blocked, pot state unchanged, turn resumes
   - **Accept**: card effect resolves; for Skim specifically, transitions to `SkimState` for digit selection
5. Timeout: action resolves automatically

### Feeling Lucky Chain

Propagated via `FeelingLuckyChainState`. The originator's ID and current target are tracked on the FSM state instance. `ForceDrawStack` on the game state tracks all participants.

1. Player A plays `FeelingLucky` targeting Player B
2. Transitions to `FeelingLuckyChainState(originatorId=A, firstTarget=B)`
3. `OnEnter` sets `FeelingLuckyTargetId` on the game state
4. Player B may:
   - **Draw**: chain resolves; game resumes from originator A's turn (`PlayerTurnState`)
   - **Pass chain** (another Feeling Lucky): transitions to new `FeelingLuckyChainState` targeting C
   - **Comp'd**: chain blocked; previous player must draw or continue passing
5. Timeout: current target is forced to draw

### Make My Luck

Transitions to `MakeMyLuckState`. The top 3 cards of the current shoe are peeked and stored in the player's `PrivateReveal`.

1. Player plays Make My Luck → `MakeMyLuckState(playerId)` entered
2. `OnEnter` reveals top 3 shoe cards to the player via `PrivateReveal`
3. Player submits a reorder → `SubmitReorderCommand`
4. Engine replaces top 3 cards in the shoe accordingly, clears `PrivateReveal`
5. Transitions back to `PlayerTurnState`

### Division by Zero

When a `÷` operator is drawn and `PotValue == 0`, `CardCounterGameContext.HandleDivisionByZero` is called:

| Roll (0–3) | Effect |
|---|---|
| 0 | `PassesRemaining += 1` |
| 1 | `PassesRemaining -= 1` (floored at 0) |
| 2 | Deal a random action card (if under hand limit) |
| 3 | Remove a random action card from hand (no-op if empty) |

### Not My Money

When the active player has Not My Money in their hand and draws an operator:
1. Card is played before drawing → `PlayActionCardCommand`
2. State sets `IsNotMyMoneySelecting = true`; player draws an operator
3. After drawing an operator, FSM transitions to `NotMyMoneyState(pendingOperator)`
4. Player selects a target via `NotMyMoneySelectTargetCommand` → operator is redirected
5. Player cancels via `NotMyMoneyCancelCommand` → operator applies to self

---

## Deck and Shoe Management

### Deck Construction

Managed by `CardCounterGameContext.DealNextShoe()` and engine's `BuildAndShuffleDeck()`:

```
Total cards: DeckSize (default 52)

Number cards: DeckSize × (NumberToOperatorRatio / (NumberToOperatorRatio + 1))
  → Default: 52 × (4/5) ≈ 42 number cards (digits 0–9, evenly distributed)

Operator cards: DeckSize - NumberCardCount
  → Default: 10 operator cards
  → Add/Subtract vs Multiply/Divide split by AddSubToMulDivRatio (default 4:1)
  → Default: ~8 add/subtract, ~2 multiply/divide
```

The deck is built as `List<BaseCard>`, Fisher-Yates shuffled via `IRandomNumberService`, then pushed onto `Stack<BaseCard> MainDeck`.

### Shoe Partitioning

Shoes are popped sequentially from the main deck stack by `CardCounterGameContext.DealNextShoe()`:

```csharp
// Shoe size computation:
if (remaining <= min) return remaining;
int maxAllowed = Math.Min(max, remaining - min);
if (maxAllowed < min) return remaining;
return Rng.GetRandomInt(min, maxAllowed + 1, RandomType.Secure);
```

`ShoeIndex` is incremented on each shoe. `ShoeCardCounts` is recomputed from the new shoe contents. `IsNewShoe` is set to `true` to trigger the UI dealing animation.

### Card Count Visibility

`ShoeCardCounts` tracks `Number` and `Operator` card type counts for the current shoe. Counts decrement as cards are drawn via `CardCounterGameContext.DecrementShoeCount()`. The dictionary is part of the public game state — all players can see card type counts.

---

## Razor Page Structure

Card Counter uses a single routable page (`CardCounterLobby.razor`) that switches rendered content based on `GamePhase`. All game UI lives in one component tree.

### Page Setup

```csharp
@page "/room/card-counter/{ObfuscatedRoomCode}"

public partial class CardCounterLobby : DisposableComponent
{
    [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;
    [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
    [Inject] protected INavigationService NavigationService { get; set; } = default!;
    [Inject] protected IUserService UserService { get; set; } = default!;

    [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

    private IDisposable? _stateSubscription;
    private PeriodicTimer? _timer;
    protected CardCounterGameState? GameState { get; set; }
}
```

### Subscribe / Tick / Dispose

The Razor page drives FSM ticks via a `PeriodicTimer` in addition to subscribing to state changes:

```csharp
// OnInitializedAsync:
_stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
{
    // Handle shoe animation, clear transient UI, re-render
    await InvokeAsync(StateHasChanged);
});

_timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
_ = StartTimerAsync(); // calls GameEngine.Tick(context, DateTimeOffset.UtcNow) each second

// Dispose():
_stateSubscription?.Dispose();
_timer?.Dispose();
```

### Direct Engine Method Calls

The Razor page calls engine methods directly with typed parameters:

```csharp
GameEngine.SetBuyIn(UserService.CurrentUser!, GameState, isNegative);
GameEngine.DrawCard(UserService.CurrentUser!, GameState);
GameEngine.PassTurn(UserService.CurrentUser!, GameState);
GameEngine.FoldPot(UserService.CurrentUser!, GameState);
GameEngine.PlayActionCard(UserService.CurrentUser!, GameState, cardIndex, targetPlayerId);
GameEngine.AcceptPending(UserService.CurrentUser!, GameState);
GameEngine.SubmitReorder(UserService.CurrentUser!, GameState, reorderedIndices);
GameEngine.DiscardActionCards(UserService.CurrentUser!, GameState, cardIndices);
GameEngine.SkimSelect(UserService.CurrentUser!, GameState, sourceDigitIndex, targetDigitIndex);
GameEngine.NotMyMoneySelectTarget(UserService.CurrentUser!, GameState, targetPlayerId);
GameEngine.NotMyMoneyCancel(UserService.CurrentUser!, GameState);
GameEngine.ResetGame(UserService.CurrentUser!, GameState);
```

### Phase-Switched UI

```
/room/card-counter/{ObfuscatedRoomCode}
│
├── @phase == BuyIn
│   ├── Die roll animation / result
│   ├── Positive / Negative balance selector
│   └── Waiting indicator for other players
│
├── @phase == Playing
│   ├── Shared (always visible)
│   │   ├── Shoe card counts (live-updating)
│   │   ├── All player balances and pots
│   │   ├── Discard history timeline
│   │   └── Current turn indicator
│   │
│   ├── Active Player View (when it's your turn in PlayerTurnState)
│   │   ├── Action card hand (playable)
│   │   ├── Fold button (if passes > 0)
│   │   ├── Draw button
│   │   └── Pass button (if passes > 0)
│   │
│   ├── Reaction View (WaitingForReactionState / SkimState targets you)
│   │   ├── Block (Comp'd) button
│   │   └── Accept button
│   │
│   ├── Feeling Lucky View (FeelingLuckyChainState targets you)
│   │   ├── Draw (resolve chain)
│   │   ├── Pass it on (if you have Feeling Lucky)
│   │   └── Block (Comp'd)
│   │
│   ├── Make My Luck View (MakeMyLuckState — you are the actor)
│   │   └── Drag-and-drop reorder of revealed top-3 cards
│   │
│   ├── Not My Money View (NotMyMoneyState — you are the active player)
│   │   ├── Target player selector
│   │   └── Apply to self (cancel redirect)
│   │
│   ├── Discard View (when over hand limit)
│   │   └── Select cards to discard
│   │
│   └── Spectating View (non-active players)
│       └── Action card hand (display only, no play buttons)
│
└── @phase == GameOver
    ├── Final balances ranked by proximity to 0
    ├── Winner announcement
    ├── Tiebreaker display (if applicable)
    ├── Return to lobby button
    └── Play Again button (triggers ResetGame — host only)
```

### Private State Rendering

Action card hands are private. The Razor page compares the current circuit's player ID against each `PlayerState.PlayerId` and only renders the full hand UI for the matching player. Other players see a card-count badge only. The `Make My Luck` reveal (`PrivateReveal`) is similarly rendered only for the matching player.

---

## CardCounterGameContext Helpers

`CardCounterGameContext` provides helpers used by FSM states so the same logic is not duplicated across states:

| Helper | Description |
|---|---|
| `CurrentPlayerId` | Player ID of the active player (from `TurnOrder[CurrentPlayerIndex]`) |
| `IsCurrentPlayer(id)` | Whether the given player is the active player |
| `GetCurrentPlayer()` | Returns the active `PlayerState` |
| `GetPlayer(id)` | Looks up a `PlayerState` by ID |
| `AdvanceTurn()` | Wraps `CurrentPlayerIndex` to next player |
| `GetRandomActionCard()` | Picks a random `ActionCard` from the action type pool |
| `DealActionCards()` | Deals `ActionsDealtPerRound` cards to every player |
| `DealNextShoe()` | Pops the next shoe from `MainDeck`, updates counts. Returns `false` if deck is empty. |
| `RecalculateShoeCounts()` | Recomputes `ShoeCardCounts` from `CurrentShoe` |
| `DecrementShoeCount(drawn)` | Decrements a card type count as a card is drawn |
| `ApplyNumberCard(player, card)` | Appends digit to player's pot |
| `ApplyOperatorCard(player, card)` | Applies operator to balance, handles div/0 |
| `RecordDraw(player, card)` | Appends to `DiscardHistory`, sets `LastDrawnCard` |
| `RecordRedirectedDraw(drawer, target, card)` | Like `RecordDraw` but with redirect info |
| `RecordBurn(card)` | Appends a burned entry to `DiscardHistory` |
| `RecordActionCardPlay(player, card)` | Appends action card play to `DiscardHistory` |

---

## Player-Leave Handling

`CardCounterGameEngine.HandlePlayerLeft` is subscribed to `state.PlayerUnregistered`. It runs inside `state.Execute()` to:

1. Remove the leaving player from `TurnOrder` and `GamePlayers`.
2. Adjust `CurrentPlayerIndex` to remain pointed at the correct next player.
3. If no players remain → transition to `GameOverState`.
4. If the leaving player was the active player during `Playing` → transition immediately to `PlayerTurnState` for the next player.
5. If during `BuyIn` and all remaining players have now set buy-in → transition to `RoundEndState`.

---

## Concurrency Model

All mutations are serialized through `AbstractGameState._executeLock` (`SemaphoreSlim(1,1)`). Every engine operation calls `state.Execute()` which:
1. Acquires the lock
2. Calls the FSM command dispatch or direct mutation
3. Releases the lock
4. Calls `StateChangedEventManager.Notify()` (fire-and-forget, after lock release)

Server-side timeouts in FSM states use `ScheduleCallback()` on `AbstractGameState`, which executes the callback via `ExecuteAsync` when the delay elapses. All scheduled callbacks are automatically cancelled when the state is disposed. FSM states that have timeouts store the expiry time (`_expiresAt`) and check it in `Tick()`.

---

## Playtesting Configuration

```csharp
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
```

---

## File Structure

Source files are physically located in `KnockBox/` but compiled into the `KnockBox.CardCounter` class library assembly:

```
KnockBox/Services/Logic/Games/CardCounter/
├── CardCounterGameEngine.cs
└── FSM/
    ├── CardCounterCommand.cs         // command record hierarchy
    ├── CardCounterGameContext.cs     // per-game context and helpers
    ├── ICardCounterGameState.cs      // FSM state interface
    └── States/
        ├── BuyInState.cs
        ├── FeelingLuckyChainState.cs
        ├── GameOverState.cs
        ├── MakeMyLuckState.cs
        ├── NotMyMoneyState.cs
        ├── PlayerTurnState.cs
        ├── RoundEndState.cs
        ├── SkimState.cs
        └── WaitingForReactionState.cs

KnockBox/Services/State/Games/CardCounter/
├── CardCounterGameState.cs           // state + enums + card records + supporting types
└── Data/
    └── PlayerState.cs

KnockBox/Components/Pages/Games/CardCounter/
├── CardCounterLobby.razor
├── CardCounterLobby.razor.cs
└── CardCounterLobby.razor.css
```

---

## Constraints & Trade-offs

**FSM over direct dispatch.** Card Counter uses an explicit FSM rather than a flat set of `if`/`switch` statements in the engine. This makes multi-step interaction flows (Feeling Lucky chain, Skim digit selection, Make My Luck reorder, Not My Money redirect) first-class states with clear entry/exit semantics. Adding a new interaction pattern means adding a new state class without touching the core turn loop.

**Tick-driven timeouts.** Reaction timeouts are checked in `Tick()` rather than via `ScheduleCallback`. The Razor page drives a 1-second `PeriodicTimer` that calls `GameEngine.Tick(context, now)`. This keeps timeout resolution predictable and avoids the overhead of managing `CancellationTokenSource` objects per timeout. The tradeoff is that timeout resolution is coarse-grained (up to ~1 second late).

**Single page vs. multi-page.** Card Counter uses a single Razor page for all phases. Phase transitions happen frequently (every shoe) and the component tree is shared, which avoids redundant state subscription setup.

**Private state in shared GameState.** `PrivateReveal` and `ActionHand` live on the shared `CardCounterGameState`. The Razor page is responsible for only rendering each player's own private data. This is secure in Blazor Server because all rendering is server-side — the HTML sent to the client never includes other players' private data.

**Player-leave mid-interaction.** If the active player disconnects during a sub-state (e.g., `WaitingForReactionState`), `HandlePlayerLeft` transitions directly to `PlayerTurnState` for the next player, bypassing the pending interaction. This avoids stalls at the cost of abandoning the in-flight action.
