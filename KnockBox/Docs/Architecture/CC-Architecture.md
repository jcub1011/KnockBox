# Card Counter — Game Architecture

## Overview

Card Counter is a multiplayer card game implemented as a game module within the multi-game room platform. Players manipulate a numeric balance through card draws, using concatenation, arithmetic operators, and action cards to steer their balance as close to zero as possible. This document describes how the game's rules, state, and UI map onto the platform's `AbstractGameEngine` base class and `AbstractGameState` subclass system.

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
 │  └──────────────────┘ └────────┬─────────┘ └──────────┘ │
 │                                │                         │
 └────────────────────────────────┼─────────────────────────┘
                                  │
                   ┌──────────────┼──────────────┐
                   ▼              ▼              ▼
             Lobby View    Gameplay View   Results View
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

## Game State Model

All mutable state lives on `CardCounterGameState : AbstractGameState` as strongly-typed properties. The engine reads and writes this state through `state.Execute()`. No state is held on the engine itself.

### CardCounterGameState

```
CardCounterGameState : AbstractGameState
│
│  Inherited from AbstractGameState:
│  ├── Host               : User
│  ├── Players             : IReadOnlyList<User>
│  ├── IsJoinable          : bool
│  ├── EventManager        : IThreadSafeEventManager<int>
│  ├── Execute()           : serialized mutation + notify
│  ├── ScheduleCallback()  : delayed state transitions
│  └── SubscribeToStateChanged() : IDisposable subscription
│
│  Game-specific properties:
├── Phase                    : GamePhase (BuyIn, Playing, RoundEnd, GameOver)
├── MainDeck                 : List<Card>           // Full shuffled deck, built once
├── CurrentShoe              : List<Card>           // Active shoe (subset of deck)
├── ShoeIndex                : int                  // Which shoe we're on
├── ShoeCardCounts           : Dictionary<CardType, int>  // Visible card counts for current shoe
├── DiscardPile              : List<Card>           // Cards already drawn/discarded
├── ActionDeck               : List<ActionCard>     // Separate action card draw pile
├── CurrentPlayerIndex       : int                  // Index into TurnOrder
├── TurnOrder                : List<string>         // Player IDs in clockwise order
├── CurrentPendingAction     : PendingAction?       // Tracks targeted action card responses
├── PendingChain             : ForcedDrawChain?     // Tracks "Feeling Lucky?" chain
├── Config                   : GameConfig           // Playtesting-tunable values
│
└── GamePlayers : ConcurrentDictionary<string, PlayerState>
    └── PlayerState
        ├── PlayerId         : string
        ├── DisplayName      : string
        ├── Balance          : int
        ├── Pot              : List<int>            // Ordered digit list
        ├── PotValue         : int                  // Computed: ignores leading zeros
        ├── ActionHand       : List<ActionCard>     // Hidden from other players
        ├── PassesRemaining  : int
        ├── IsHost           : bool
        ├── HasSetBuyIn      : bool
        ├── BuyInRoll        : int                  // Server-generated die result
        └── PrivateReveal    : List<Card>?          // Make My Luck top-3 reveal
```

The state class has **no mutation methods** — only properties, computed properties (e.g., `PotValue`), and data accessor methods. All mutation logic lives on the engine.

### Card Representation

```csharp
public abstract record Card(CardType Type);
public record NumberCard(int Value) : Card(CardType.Number);           // 0–9
public record OperatorCard(Operator Op) : Card(CardType.Operator);    // Add, Subtract, Multiply, Divide

public enum Operator { Add, Subtract, Multiply, Divide }

public record ActionCard(ActionType Action);
public enum ActionType
{
    FeelingLucky,   // Force Draw
    MakeMyLuck,     // Alter the Future
    Skim,           // Swap a Digit
    Burn,           // Discard top card
    TurnTheTable,   // Flip pot
    Compd,          // Shield / Block
    NotMyMoney,     // Redirect operator
    Launder         // Swap pots
}
```

### Supporting Types

```csharp
public class ForcedDrawChain
{
    public string OriginatorId { get; set; }
    public string CurrentTargetId { get; set; }
    public List<string> ChainParticipants { get; set; }
}

public class PendingAction
{
    public string SourcePlayerId { get; set; }
    public string TargetPlayerId { get; set; }
    public ActionCard CardPlayed { get; set; }
    public ActionCard? CounteredBy { get; set; }
    public CancellationTokenSource? TimeoutCts { get; set; }
}
```

---

## Control Flow

The following diagram shows the lifecycle of a single player action during gameplay. The Razor page calls a method on the game engine (resolved via DI), the engine calls `state.Execute` which acquires the lock, runs the mutation, releases the lock, and then notifies all subscribers to re-render.

```
  Player A (Razor Page)          CardCounterGameEngine    CardCounterGameState     Player B (Razor Page)
  ─────────────────────          ─────────────────────    ────────────────────     ─────────────────────
          │                               │                        │                        │
          │  engine.DrawCard(             │                        │                        │
          │    player, state)             │                        │                        │
          │ ─────────────────────────────>│                        │                        │
          │                               │                        │                        │
          │                               │  state.Execute(() =>   │                        │
          │                               │    { ... })            │                        │
          │                               │ ──────────────────────>│                        │
          │                               │                        │                        │
          │                               │               ┌────────────────────┐             │
          │                               │               │  Acquire lock      │             │
          │                               │               │  Run mutation      │             │
          │                               │               │  Release lock      │             │
          │                               │               │  NotifyChanged()   │             │
          │                               │               └────────────────────┘             │
          │                               │                        │                        │
          │                 Result         │                        │                        │
          │ <─────────────────────────────│                        │                        │
          │                               │                        │                        │
          │                callback: re-render with updated state   │                        │
          │ <──────────────────────────────────────────────────────│                        │
          │                               │                        │                        │
          │                               │                        │  callback: re-render   │
          │                               │                        │  with updated state    │
          │                               │                        │ ──────────────────────>│
          │                               │                        │                        │
       [UI updates]                       │                        │                  [UI updates]
```

Key points: the `LobbyService` is not involved during gameplay. The Razor page injects `CardCounterGameEngine` via DI and holds a reference to `CardCounterGameState` obtained when joining the lobby. `Execute` acquires the lock, runs the mutation, releases the lock, and *then* notifies subscribers — keeping the lock held for the minimum duration and preventing reentrant deadlocks from listener callbacks. All fallible operations return `Result` or `Result<T>` rather than throwing exceptions.

---

## Game Phases

The engine uses a `GamePhase` enum to drive state transitions. The Razor page reads the current phase and renders the corresponding UI section.

```
  ┌────────┐     All players      ┌─────────┐
  │ BuyIn  │ ──── set balance ───►│ Playing  │◄──── new shoe dealt
  └────────┘                      └────┬─────┘
                                       │
                              shoe exhausted
                                       │
                                       ▼
                                 ┌───────────┐    more shoes
                                 │ RoundEnd  │ ─── remain ───► Playing
                                 └─────┬─────┘
                                       │
                                  deck empty
                                       │
                                       ▼
                                 ┌───────────┐
                                 │ GameOver  │
                                 └───────────┘
```

### BuyIn Phase

Each player rolls a virtual 6-sided die (server-generated via `IRandomNumberService`), sees the result multiplied by 8, and chooses positive or negative as their starting balance. The engine waits for all players to submit their buy-in via `SetBuyIn` before transitioning to `Playing`.

### Playing Phase

The active phase where turns execute sequentially. Each turn is a sequence of direct engine method calls from the active player's Razor page.

### RoundEnd Phase

Triggered when the current shoe is exhausted. Action cards are dealt from the action deck (up to the per-round deal count), players over the hand limit discard, and the next shoe is dealt. Transitions back to `Playing`.

### GameOver Phase

Triggered when the final shoe is exhausted and the last card has been drawn. Balances are compared and the winner is determined.

---

## Engine Methods

`CardCounterGameEngine` extends `AbstractGameEngine` and exposes game-specific methods that the Razor page calls directly. Each method receives a `User` and `CardCounterGameState` reference, performs validation, and calls `state.Execute()` to mutate. This follows the same pattern as `DiceSimulatorGameEngine`.

### Lifecycle Methods (inherited contract)

```csharp
public override Task<Result<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
```
Creates a `CardCounterGameState`, sets `IsJoinable = true`. The state is returned to the `LobbyService` which stores it in the `LobbyRegistration`.

```csharp
public override Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
```
Validates the caller is the host, casts to `CardCounterGameState`, then calls `state.Execute()` to set `IsJoinable = false` and initialize the game (build decks, deal cards, set phase to `BuyIn`).

### Game-Specific Methods

Each method calls `state.Execute()` internally to acquire the lock, mutate, release, and notify:

| Method | Parameters | Phase | Behavior |
|---|---|---|---|
| `SetBuyIn` | `User player, CardCounterGameState state, bool isNegative` | BuyIn | Records signed balance from server-generated die roll. Transitions to `Playing` when all players have set. |
| `DrawCard` | `User player, CardCounterGameState state` | Playing | Draws top card from shoe, applies number/operator logic. Ends turn. |
| `PassTurn` | `User player, CardCounterGameState state` | Playing | Validates passes remaining > 0, decrements passes. Ends turn. |
| `FoldPot` | `User player, CardCounterGameState state` | Playing | Validates passes remaining > 0, clears pot, decrements passes. Does not end turn. |
| `PlayActionCard` | `User player, CardCounterGameState state, int cardIndex, string? targetPlayerId = null` | Playing | Validates card in hand, removes card, executes effect. May set `CurrentPendingAction`. |
| `SubmitReorder` | `User player, CardCounterGameState state, int[] reorderedIndices` | Playing | Resolves Make My Luck reveal — reorders top cards of shoe. |
| `DiscardExcess` | `User player, CardCounterGameState state, int[] discardIndices` | Playing / RoundEnd | Discards action cards down to hand limit of 6. |
| `AcceptPending` | `User player, CardCounterGameState state` | Playing | Target accepts a pending action (does not block). |

### Internal Helper Methods

These are private methods on the engine, called within `state.Execute()` closures:

- `InitializeGame` — sets phase, assigns turn order, creates `PlayerState` entries, builds decks, deals initial shoe
- `BuildMainDeck` — constructs number and operator cards per `GameConfig` ratios
- `BuildActionDeck` — constructs action card draw pile
- `DealActionCards` — deals action cards to each player (up to per-round count)
- `DealNextShoe` — partitions next shoe from the main deck, updates `ShoeCardCounts`
- `EndTurn` — advances `CurrentPlayerIndex`, triggers shoe/round transitions if shoe is exhausted
- `Shuffle` — Fisher-Yates shuffle using `IRandomNumberService`
- `RecalculateShoeCounts` — recomputes visible card type counts for current shoe

---

## Turn Flow Detail

```
  Player's Turn Begins
         │
         ▼
  ┌─────────────────┐
  │ Play Action Card │◄──── (optional, repeatable)
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
  │ Draw OR Pass         │──── ends turn
  └─────────┬───────────┘
            │
     ┌──────┴──────┐
     ▼             ▼
  Draw Card     Pass (costs 1 pass)
     │
     ├── NumberCard → append digit to pot
     │
     └── OperatorCard
            │
            ├── Pot empty → no-op
            │
            ├── Division by 0 → random event
            │
            └── Apply: Balance = Balance [op] PotValue
                       Clear pot
```

The Razor page calls the corresponding engine method for each action:
- Play action card → `engine.PlayActionCard(user, state, cardIndex, targetPlayerId)`
- Fold → `engine.FoldPot(user, state)`
- Draw → `engine.DrawCard(user, state)`
- Pass → `engine.PassTurn(user, state)`

---

## Action Card Resolution

Action cards introduce targeted interactions between players. The engine resolves them with a priority system.

### Targeting and Blocking

Cards that are marked **Blockable** in the design can be countered by the target playing `Comp'd`. The engine handles this with a **pending action** pattern:

1. Player A calls `engine.PlayActionCard(user, state, cardIndex, targetPlayerId)`.
2. Engine calls `state.Execute()` to set `CurrentPendingAction` on the state with the target player ID.
3. Engine calls `state.ScheduleCallback()` to set a response timeout, storing the returned `CancellationTokenSource` on the `PendingAction`.
4. Player B may respond with `engine.PlayActionCard(user, state, compdIndex)` to block.
5. If blocked, the action is negated (with special bounce-back logic for `Not My Money`). The timeout is cancelled.
6. If not blocked (Player B calls `engine.AcceptPending(user, state)` or the timeout fires), the action resolves.

### Feeling Lucky? Chain

The `FeelingLucky` card creates a chain that propagates clockwise. The engine tracks this via `PendingChain`:

```csharp
public class ForcedDrawChain
{
    public string OriginatorId { get; set; }        // Who started the chain
    public string CurrentTargetId { get; set; }     // Who must respond
    public List<string> ChainParticipants { get; set; } // All who passed it along
}
```

Each target may respond with their own `FeelingLucky` (pushing it to the next player) or `Comp'd` (blocking — the previous player in the chain must draw or continue passing). The chain resolves when a player draws without countering. The round then resumes from the originator.

### Make My Luck (Alter the Future)

The engine reveals the top 3 cards of the shoe to the acting player only. This is handled by setting `PrivateReveal` on the player's `PlayerState`, which the Razor page reads during rendering and only renders for the matching player. The player calls `engine.SubmitReorder(user, state, reorderedIndices)`, and the engine replaces the top 3 cards accordingly within `state.Execute()`.

### Division by Zero

When a player draws a ÷ operator with a pot value of 0, the engine selects one of four outcomes at random (uniform distribution via `IRandomNumberService`):

1. `PassesRemaining += 1`
2. `PassesRemaining -= 1` (floored at 0)
3. Deal a random action card (trigger hand limit discard if over 6)
4. Remove a random action card from hand (no-op if hand is empty)

---

## Deck and Shoe Management

### Deck Construction

The main deck is built inside `InitializeGame` (called from `StartAsync`) based on config ratios:

```
Total cards: DeckSize (default 52)

Number cards: DeckSize × (NumberToOperatorRatio / (NumberToOperatorRatio + 1))
  → Default: 52 × (4/5) ≈ 42 number cards
  → Values 0–9 distributed evenly (with remainder distributed randomly)

Operator cards: DeckSize - NumberCardCount
  → Default: 10 operator cards
  → Add/Subtract vs Multiply/Divide split by AddSubToMulDivRatio
  → Default: 8 add/subtract, 2 multiply/divide
  → Add vs Subtract and Multiply vs Divide split evenly within each group
```

The deck is shuffled once using Fisher-Yates via `IRandomNumberService`. It is not reshuffled between shoes.

### Shoe Partitioning

Shoes are sliced sequentially from the shuffled deck:

```csharp
int shoeSize = _random.GetRandomInt(config.MinShoeSize, config.MaxShoeSize + 1, RandomType.Secure);
shoeSize = Math.Min(shoeSize, remainingCards.Count);
```

If the remaining cards for the final shoe are fewer than `MinShoeSize`, they form the last shoe as-is.

### Card Count Visibility

At shoe start, the engine computes a frequency map of card types in the shoe and stores it in `ShoeCardCounts`. As cards are drawn, the counts decrement. This dictionary is part of the public game state — all players can see it. The card order remains hidden.

---

## Razor Page Structure

Card Counter uses a single routable page (`CardCounterLobby.razor`) that switches rendered content based on `GamePhase`. This keeps all game UI in one component tree and simplifies state subscriptions.

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
    protected CardCounterGameState? GameState { get; set; }
}
```

### Session Validation

In `OnInitializedAsync`, the page validates the user has an active session via `IGameSessionService` and that the state is `CardCounterGameState`. If either check fails, the user is redirected home.

### Subscribe / Dispose

```csharp
// OnInitializedAsync:
_stateSubscription = GameState
    .SubscribeToStateChanged(async () => await InvokeAsync(StateHasChanged))
    .Value;

// Dispose():
_stateSubscription?.Dispose();
```

### Direct Engine Method Calls

The Razor page calls engine methods directly with typed parameters — no unified action class or dictionary payloads:

```csharp
GameEngine.SetBuyIn(UserService.CurrentUser!, GameState, isNegative);
GameEngine.DrawCard(UserService.CurrentUser!, GameState);
GameEngine.PassTurn(UserService.CurrentUser!, GameState);
GameEngine.FoldPot(UserService.CurrentUser!, GameState);
GameEngine.PlayActionCard(UserService.CurrentUser!, GameState, cardIndex, targetPlayerId);
GameEngine.SubmitReorder(UserService.CurrentUser!, GameState, reorderedIndices);
GameEngine.DiscardExcess(UserService.CurrentUser!, GameState, discardIndices);
GameEngine.AcceptPending(UserService.CurrentUser!, GameState);
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
│   │   └── Current turn indicator
│   │
│   ├── Active Player View
│   │   ├── Action card hand (playable)
│   │   ├── Fold button (if passes > 0)
│   │   ├── Draw button
│   │   └── Pass button (if passes > 0)
│   │
│   ├── Target Response View (when CurrentPendingAction targets you)
│   │   ├── Block (Comp'd) button
│   │   └── Accept button
│   │
│   └── Spectating View (non-active players)
│       └── Action card hand (display only, no play buttons)
│
├── @phase == RoundEnd
│   ├── Shoe summary
│   ├── New action cards dealt
│   └── Discard UI (if over hand limit)
│
└── @phase == GameOver
    ├── Final balances ranked by proximity to 0
    ├── Winner announcement
    ├── Tiebreaker display (if applicable)
    └── Return to lobby button
```

### Private State Rendering

Action card hands are private. The Razor page compares the current circuit's player ID against each `PlayerState.PlayerId` and only renders the detailed hand for the matching player. Other players see a card count badge only.

The `Make My Luck` reveal is rendered conditionally when `PrivateReveal` contains data for the current player.

---

## Action Payloads

Card Counter does **not** use a unified `PlayerAction` class or `Dictionary<string, object>` payloads. Each engine method takes exactly the parameters it needs, following the same pattern as `DiceSimulatorGameEngine.RollDice(User, DiceSimulatorGameState, DiceRollAction)`.

| Old ActionKind | New Engine Method | Parameters |
|---|---|---|
| `SetBuyIn` | `engine.SetBuyIn(...)` | `User player, CardCounterGameState state, bool isNegative` |
| `Draw` | `engine.DrawCard(...)` | `User player, CardCounterGameState state` |
| `Pass` | `engine.PassTurn(...)` | `User player, CardCounterGameState state` |
| `Fold` | `engine.FoldPot(...)` | `User player, CardCounterGameState state` |
| `PlayActionCard` | `engine.PlayActionCard(...)` | `User player, CardCounterGameState state, int cardIndex, string? targetPlayerId` |
| `ReorderMakeMyLuck` | `engine.SubmitReorder(...)` | `User player, CardCounterGameState state, int[] reorderedIndices` |
| `DiscardExcess` | `engine.DiscardExcess(...)` | `User player, CardCounterGameState state, int[] discardIndices` |
| `AcceptPending` | `engine.AcceptPending(...)` | `User player, CardCounterGameState state` |

---

## Concurrency Model

### State Lock

`AbstractGameState._executeLock` (`SemaphoreSlim(1, 1)`) serializes all mutations. Every engine method calls `state.Execute(() => { ... })` — the lock is acquired, the mutation runs, the lock is released, and `NotifyStateChanged()` fans out to all subscribers. The lock is never held during notification.

### Scheduled Callbacks

Pending action timeouts use `state.ScheduleCallback(TimeSpan, Func<Task>)` rather than client-side timers. This returns a `CancellationTokenSource` stored on the `PendingAction` so the timeout can be cancelled when the target responds. The scheduled callback executes via `ExecuteAsync` when the delay elapses, following the same locking and notification semantics as any player-driven mutation. All scheduled callbacks are automatically cancelled when the state is disposed.

### Subscriber Notification

`ThreadSafeEventManager<int>` manages the subscriber list with a copy-on-write array. `NotifyAsync` snapshots listeners and fans out concurrently via `Task.WhenAll`. Errors in individual listeners are swallowed and logged, providing error isolation.

### Thread-Safe Collections

`GamePlayers` uses `ConcurrentDictionary<string, PlayerState>` for safe concurrent access to player data.

---

## Validation Rules

The engine validates every action before calling `state.Execute()`. Invalid actions return `Result.FromError(...)` without mutating state.

| Rule | Enforcement |
|---|---|
| Player must be in the game | Engine checks `GamePlayers` contains the user's ID |
| Action must match current phase | Engine rejects mismatched phases |
| Draw/Pass/Fold only on your turn | Engine checks `CurrentPlayerIndex` against `TurnOrder` |
| Pass/Fold requires passes > 0 | Engine checks `PassesRemaining` |
| Action card must be in hand | Engine checks `ActionHand` contains the card at the given index |
| Target player must exist in game | Engine validates `targetPlayerId` against `GamePlayers` |
| Comp'd only against blockable cards | Engine checks `CurrentPendingAction` target and card type |
| Hand limit discard must bring count ≤ 6 | Engine validates post-discard count |
| Buy-in die roll matches server value | Engine compares against stored `BuyInRoll` on `PlayerState` |
| Make My Luck reorder valid | Engine validates indices match `PrivateReveal` count and are distinct |

---

## Playtesting Configuration

All values marked as playtesting-dependent in the design document are surfaced through `GameConfig`. During development, the host can adjust these values from the lobby before starting the game. In production, defaults will be locked based on playtesting results.

```csharp
public class GameConfig
{
    public int DeckSize { get; set; } = 52;
    public float NumberToOperatorRatio { get; set; } = 4.0f;
    public float AddSubToMulDivRatio { get; set; } = 4.0f;
    public int ActionsDealtPerRound { get; set; } = 3;
    public int ActionHandLimit { get; set; } = 6;
    public int TotalPassesPerPlayer { get; set; } = 3;     // Placeholder
    public int MinShoeSize { get; set; } = 12;
    public int MaxShoeSize { get; set; } = 20;
    public int ActionResponseTimeoutMs { get; set; } = 15000;
}
```

---

## File Structure

```
Components/Pages/Games/CardCounter/
├── CardCounterLobby.razor
├── CardCounterLobby.razor.cs
└── CardCounterLobby.razor.css

Services/Navigation/Games/CardCounter/
└── CardCounterGameEngine.cs

Services/State/Games/CardCounter/
├── CardCounterGameState.cs
└── Data/Models.cs
```

---

## Constraints & Trade-offs

**Single page vs. multi-page.** Card Counter uses a single Razor page (`CardCounterLobby.razor`) for all phases. The game flow is linear and phase transitions happen frequently (every shoe), so a single component tree avoids redundant state subscription setup. If the UI grows significantly more complex (e.g., team modes), splitting into sub-pages is a straightforward refactor.

**Server-side timeouts via ScheduleCallback.** Action card response timeouts use `state.ScheduleCallback()` rather than client-side timers. This ensures pending actions resolve even if the target player's circuit is disconnected. The `CancellationTokenSource` returned by `ScheduleCallback` is stored on the `PendingAction` and cancelled when the target responds, preventing stale timeouts from firing.

**Disconnect handling via LobbyCircuitHandler.** When a player's circuit drops, `LobbyCircuitHandler` starts a 30-second grace period. If the circuit reconnects within 30 seconds, the timer is cancelled. If it expires or the circuit closes permanently, the player is removed from the game state. Any `CurrentPendingAction` targeting the disconnected player should be resolved by the timeout callback.

**Private state in shared GameState.** The `PrivateReveal` and `ActionHand` fields exist on the shared `CardCounterGameState`. The Razor page is responsible for only rendering a player's own private data. This is secure in Blazor Server because all rendering happens server-side — the client never receives other players' private state in the HTML.

**Action card complexity.** The `Feeling Lucky?` chain and the `PendingAction` blocking pattern add interaction complexity. Each action card type is resolved by discrete logic within the engine's `PlayActionCard` method, keeping resolution centralized and straightforward to modify during playtesting.

**Randomness.** All random decisions (die rolls, deck shuffle, division-by-zero outcomes) use `IRandomNumberService` server-side. The client never generates random values. Die roll results are stored in `PlayerState.BuyInRoll` and validated on submission to prevent tampering.