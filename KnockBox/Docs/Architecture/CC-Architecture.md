# Card Counter — Game Architecture

## Overview

Card Counter is a multiplayer card game implemented as a game module within the multi-game room platform. Players manipulate a numeric balance through card draws, using concatenation, arithmetic operators, and action cards to steer their balance as close to zero as possible. This document describes how the game's rules, state, and UI map onto the platform's `IGameEngine` plugin system.

---

## Integration Point

Card Counter registers as a single `IGameEngine` implementation. The engine ID is `"cardcounter"`, and all game pages live under `/room/cardcounter/{Code}`.

```
 ┌──────────────────────────────────────────────┐
 │            Platform (Existing)                │
 │                                               │
 │  RoomManager ──► Room ──► GameState           │
 │                    │                          │
 │                    ▼                          │
 │  IGameEngine Registry                         │
 │  ┌──────────┐ ┌──────────────┐ ┌───────────┐ │
 │  │ Trivia   │ │ Card Counter │ │ Future... │ │
 │  └──────────┘ └──────┬───────┘ └───────────┘ │
 │                      │                        │
 └──────────────────────┼────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        ▼               ▼               ▼
  Lobby Phase     Gameplay Phase   Results Phase
  (Razor Page)    (Razor Page)     (Razor Page)
```

### Registration

```csharp
builder.Services.AddSingleton<IGameEngine, CardCounterGameEngine>();
```

---

## Game State Model

All mutable state lives in the `Room.GameState` data dictionary. The `CardCounterGameEngine` reads and writes this state through typed accessors on an internal `CardCounterState` class that wraps the dictionary. No state is held on the engine itself.

### CardCounterState

```
CardCounterState
├── Phase                    : GamePhase (BuyIn, Playing, RoundEnd, GameOver)
├── MainDeck                 : List<Card>           // Full shuffled deck, built once
├── CurrentShoe              : List<Card>           // Active shoe (subset of deck)
├── ShoeIndex                : int                  // Which shoe we're on
├── ShoeCardCounts           : Dictionary<CardType, int>  // Visible card counts for current shoe
├── DiscardPile              : List<Card>           // Cards already drawn/discarded
├── ActionDeck               : List<ActionCard>     // Separate action card draw pile
├── CurrentPlayerIndex       : int                  // Index into TurnOrder
├── TurnOrder                : List<string>         // Player IDs in clockwise order
├── PendingChain             : ForcedDrawChain?     // Tracks "Feeling Lucky?" chain
│
├── Players : Dictionary<string, PlayerState>
│   └── PlayerState
│       ├── PlayerId         : string
│       ├── DisplayName      : string
│       ├── Balance          : int
│       ├── Pot              : List<int>            // Ordered digit list
│       ├── PotValue         : int                  // Computed: ignores leading zeros
│       ├── ActionHand       : List<ActionCard>     // Hidden from other players
│       ├── PassesRemaining  : int
│       └── IsHost           : bool
│
└── Config : GameConfig
    ├── DeckSize             : int       // Default: 52
    ├── NumberToOperatorRatio : float    // Default: 4:1
    ├── AddSubToMulDivRatio  : float    // Default: 4:1
    ├── ActionsDealtPerRound : int       // Default: 3
    ├── ActionHandLimit      : int       // Default: 6
    ├── TotalPassesPerPlayer : int       // TBD via playtesting
    ├── MinShoeSize          : int       // Default: 12
    └── MaxShoeSize          : int       // Default: 20
```

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

Each player rolls a virtual 6-sided die (server-generated random), sees the result multiplied by 8, and chooses positive or negative as their starting balance. The engine waits for all players to submit their buy-in before transitioning to `Playing`.

### Playing Phase

The active phase where turns execute sequentially. Each turn is a sequence of player actions submitted via `HandleActionAsync`.

### RoundEnd Phase

Triggered when the current shoe is exhausted. Action cards are dealt from the action deck (up to the per-round deal count), players over the hand limit discard, and the next shoe is dealt. Transitions back to `Playing`.

### GameOver Phase

Triggered when the final shoe is exhausted and the last card has been drawn. Balances are compared and the winner is determined.

---

## IGameEngine Implementation

### InitializeAsync

1. Build the main deck according to `GameConfig` ratios.
2. Shuffle the main deck using Fisher-Yates.
3. Build the action deck.
4. Partition the first shoe from the main deck.
5. Compute and store `ShoeCardCounts` for the first shoe.
6. Deal initial action cards (3 per player) from the action deck.
7. Assign turn order (clockwise, host goes first).
8. Set phase to `BuyIn`.
9. Return the initial `GameState`.

### HandleActionAsync

Receives a `PlayerAction` and dispatches based on `ActionKind`:

| ActionKind | Phase | Behavior |
|---|---|---|
| `SetBuyIn` | BuyIn | Validates die roll, records signed balance. Transitions to `Playing` when all set. |
| `PlayActionCard` | Playing | Validates it's the player's turn (or a response to a chain), removes card from hand, executes effect. |
| `DiscardExcess` | Playing / RoundEnd | Player discards action cards down to hand limit of 6. |
| `Fold` | Playing | Validates passes remaining > 0, clears pot, decrements passes. Does not end turn. |
| `Draw` | Playing | Draws top card from shoe, applies number/operator logic. Ends turn. |
| `Pass` | Playing | Validates passes remaining > 0, decrements passes. Ends turn. |
| `ChooseBuyInSign` | BuyIn | Player selects positive or negative for their starting balance. |

After every action, the engine checks:

- Is the shoe empty? → Transition to `RoundEnd`, deal action cards, deal next shoe (or `GameOver` if deck is exhausted).
- Has the turn ended? → Advance `CurrentPlayerIndex`.

The engine returns the updated `GameState`, and the platform's `Room.NotifyStateChanged` pushes it to all connected clients.

### IsGameOver

Returns `true` when `Phase == GamePhase.GameOver`.

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

---

## Action Card Resolution

Action cards introduce targeted interactions between players. The engine resolves them with a priority system.

### Targeting and Blocking

Cards that are marked **Blockable** in the design can be countered by the target playing `Comp'd`. The engine handles this with a **pending action** pattern:

1. Player A submits `PlayActionCard` targeting Player B.
2. Engine sets a `PendingAction` on the state with a target player ID and a short response window.
3. Player B may respond with `PlayActionCard(Compd)` to block.
4. If blocked, the action is negated (with special bounce-back logic for `Not My Money`).
5. If not blocked (Player B draws or plays a non-block card, or the response window expires), the action resolves.

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

The engine reveals the top 3 cards of the shoe to the acting player only. This is handled by including a `PrivateReveal` field in the game state keyed to the player ID, which the Razor page reads during rendering. The player submits a reorder action, and the engine replaces the top 3 cards accordingly.

### Division by Zero

When a player draws a ÷ operator with a pot value of 0, the engine selects one of four outcomes at random (uniform distribution):

1. `PassesRemaining += 1`
2. `PassesRemaining -= 1` (floored at 0)
3. Deal a random action card (trigger hand limit discard if over 6)
4. Remove a random action card from hand (no-op if hand is empty)

---

## Deck and Shoe Management

### Deck Construction

The main deck is built at `InitializeAsync` based on config ratios:

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

The deck is shuffled once. It is not reshuffled between shoes.

### Shoe Partitioning

Shoes are sliced sequentially from the shuffled deck:

```csharp
while (remainingCards.Count > 0)
{
    int shoeSize = Random.Shared.Next(config.MinShoeSize, config.MaxShoeSize + 1);
    shoeSize = Math.Min(shoeSize, remainingCards.Count);
    shoes.Add(remainingCards.Take(shoeSize).ToList());
    remainingCards = remainingCards.Skip(shoeSize).ToList();
}
```

If the remaining cards for the final shoe are fewer than `MinShoeSize`, they form the last shoe as-is.

### Card Count Visibility

At shoe start, the engine computes a frequency map of card types in the shoe and stores it in `ShoeCardCounts`. As cards are drawn, the counts decrement. This dictionary is part of the public game state — all players can see it. The card order remains hidden.

---

## Razor Page Structure

Card Counter uses a single routable page that switches rendered content based on `GamePhase`. This keeps all game UI in one component tree and simplifies state subscriptions.

```
/room/cardcounter/{Code}
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
│   ├── Target Response View (when PendingAction targets you)
│   │   ├── Block (Comp'd) button
│   │   └── Accept / Draw button
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

## Player Action Payloads

All player interactions route through `RoomManager.HandleActionAsync` with a `PlayerAction`. The `Data` dictionary carries action-specific parameters.

| ActionKind | Data Keys | Description |
|---|---|---|
| `SetBuyIn` | `DieRoll: int`, `IsNegative: bool` | Server validates die roll matches the generated value |
| `PlayActionCard` | `CardIndex: int`, `TargetPlayerId: string?`, `SwapPosition: int?`, `ReorderIndices: int[]?` | Varies by action card type |
| `DiscardExcess` | `DiscardIndices: int[]` | Indices of action cards to discard |
| `Fold` | *(none)* | Clears pot, costs a pass |
| `Draw` | *(none)* | Draws top card of shoe |
| `Pass` | *(none)* | Skips draw, costs a pass |

---

## Concurrency Considerations

The platform's per-room lock serializes all calls to `HandleActionAsync` for a given room. This means Card Counter's engine does not need its own locking — the room lock guarantees that only one action mutates state at a time.

The `PendingAction` / `PendingChain` pattern for action card responses relies on this serialization. When a blocking opportunity arises, the engine sets the pending state and returns. Subsequent `HandleActionAsync` calls from the target player resolve the pending action before any other mutations occur.

### Timeout Handling

If a target player does not respond to a `PendingAction` within a configurable timeout (e.g., 15 seconds), the Razor page submits an automatic "accept" action on their behalf. This is driven client-side by a timer in the Razor component, keeping the engine itself stateless with respect to wall-clock time.

---

## Validation Rules

The engine validates every action before applying it. Invalid actions return an error result without mutating state.

| Rule | Enforcement |
|---|---|
| Player must be in the room | Room-level check (platform) |
| Action must match current phase | Engine rejects mismatched phases |
| Draw/Pass/Fold only on your turn | Engine checks `CurrentPlayerIndex` |
| Pass/Fold requires passes > 0 | Engine checks `PassesRemaining` |
| Action card must be in hand | Engine checks `ActionHand` contains the card |
| Target player must exist in room | Engine validates `TargetPlayerId` |
| Comp'd only against blockable cards | Engine checks `PendingAction` target and card type |
| Hand limit discard must bring count ≤ 6 | Engine validates post-discard count |
| Buy-in die roll must match server-generated value | Engine compares against stored roll |

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
Games/
└── CardCounter/
    ├── CardCounterGameEngine.cs        // IGameEngine implementation
    ├── CardCounterState.cs             // Typed wrapper over GameState dictionary
    ├── Models/
    │   ├── Card.cs                     // Card, NumberCard, OperatorCard records
    │   ├── ActionCard.cs               // ActionCard record and ActionType enum
    │   ├── PlayerState.cs              // Per-player mutable state
    │   ├── GameConfig.cs               // Playtesting-tunable configuration
    │   └── ForcedDrawChain.cs          // Feeling Lucky? chain tracking
    ├── Services/
    │   ├── DeckBuilder.cs              // Deck construction and shoe partitioning
    │   ├── ActionResolver.cs           // Action card effect execution
    │   └── ScoringService.cs           // Final balance comparison and tiebreakers
    └── Pages/
        └── CardCounterRoom.razor       // Single routable page for all phases
            └── CardCounterRoom.razor.cs // Code-behind
```

---

## Constraints & Trade-offs

**Single page vs. multi-page.** Card Counter uses a single Razor page for all phases. The game flow is linear and phase transitions happen frequently (every shoe), so a single component tree avoids redundant state subscription setup. If the UI grows significantly more complex (e.g., team modes), splitting into sub-pages is a straightforward refactor.

**Client-driven timeouts.** Action card response timeouts are enforced by client-side timers, not server-side background tasks. This keeps the engine purely reactive and avoids coordinating timers across the engine and the platform's `BackgroundService`. The trade-off is that a disconnected client won't auto-resolve — the platform's circuit disconnect handler should trigger a default resolution for any pending actions involving the disconnected player.

**Private state in shared GameState.** The `PrivateReveal` and `ActionHand` fields exist in the shared `GameState` dictionary. The Razor page is responsible for only rendering a player's own private data. This is secure in Blazor Server because all rendering happens server-side — the client never receives other players' private state in the HTML. If the platform ever migrates to Blazor WebAssembly, private state would need to be segregated into per-circuit state or fetched via API.

**Action card complexity.** The `Feeling Lucky?` chain and the `PendingAction` blocking pattern add interaction complexity. The resolution logic is centralized in `ActionResolver` to keep `HandleActionAsync` clean. Each action card type is a discrete handler method, making it straightforward to add or modify cards during playtesting.

**Randomness.** All random decisions (die rolls, deck shuffle, division-by-zero outcomes) use server-side `Random.Shared`. The client never generates random values. Die roll results are stored in state and validated on submission to prevent tampering.