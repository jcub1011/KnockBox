# Dice Simulator — Game Module Architecture

## Overview

A pluggable game module for the KnockBox platform that provides a shared dice-rolling utility for tabletop RPG sessions. Players join a room, roll standard D&D dice with optional advantage/disadvantage, and see every roll appear on a real-time shared leaderboard. This is not a competitive game with win conditions — it is a persistent utility that runs until the host ends the session.

---

## Dice Types

The module supports the seven standard D&D dice:

| Die  | Sides | Common Use                        |
|------|-------|-----------------------------------|
| d4   | 4     | Dagger damage, healing spells     |
| d6   | 6     | Fireball, short sword damage      |
| d8   | 8     | Longsword, cure wounds            |
| d10  | 10    | Heavy crossbow, eldritch blast    |
| d12  | 12    | Greataxe damage                   |
| d20  | 20    | Attack rolls, ability checks      |
| d100 | 100   | Percentile rolls, wild magic      |

---

## Roll Expression Syntax

Rolls use standard D&D dice notation: `NdX+M`, where `N` is the number of dice (1–99, default 1), `X` is the die type, and `M` is an optional integer modifier (positive or negative). The modifier is applied once to the total, not per die.

| Expression | Meaning                                      |
|------------|----------------------------------------------|
| `d20`      | Roll one d20                                 |
| `2d6+4`    | Roll two d6, sum them, add 4                 |
| `4d6`      | Roll four d6, sum them                       |
| `1d20-1`   | Roll one d20, subtract 1                     |
| `8d6`      | Roll eight d6 (e.g., Fireball damage)        |

---

## Roll Modes

Each roll is made in one of three modes:

| Mode          | Behavior                                                                  |
|---------------|---------------------------------------------------------------------------|
| Normal        | Roll the dice as written and sum the results plus modifier.               |
| Advantage     | Roll the full expression **twice**, take the **higher** total.            |
| Disadvantage  | Roll the full expression **twice**, take the **lower** total.             |

Advantage and disadvantage apply to the entire roll expression. When rolling with advantage or disadvantage, both complete sets of individual die results are stored for transparency, but only the effective (kept) total is used for the leaderboard display.

---

## Data Model

### DiceType Enum

```csharp
public enum DiceType
{
    D4 = 4,
    D6 = 6,
    D8 = 8,
    D10 = 10,
    D12 = 12,
    D20 = 20,
    D100 = 100
}
```

### RollMode Enum

```csharp
public enum RollMode
{
    Normal,
    Advantage,
    Disadvantage
}
```

### DiceRollAction

The input payload passed to `DiceSimulatorGameEngine.RollDice`:

```csharp
public class DiceRollAction
{
    public DiceType DiceType { get; set; }
    public int DiceCount { get; set; } = 1;       // 1–99 (clamped in engine)
    public int Modifier { get; set; } = 0;         // Can be negative
    public RollMode Mode { get; set; }
}
```

### DiceRollEntry

A single immutable record representing one completed roll action.

```csharp
public sealed record DiceRollEntry
{
    public required Guid Id { get; init; }
    public required string PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public required DiceType DiceType { get; init; }
    public required int DiceCount { get; init; }        // N in NdX (1–99)
    public required int Modifier { get; init; }          // +M or -M (0 if none)
    public required RollMode Mode { get; init; }
    public required int Result { get; init; }            // The effective (kept) total
    public required int[] RawRolls { get; init; }        // Individual die results for the kept set
    public required int[]? AltRolls { get; init; }       // Second set for Adv/Disadv, null for Normal
    public required int AltTotal { get; init; }           // Total of the discarded set (0 for Normal)
    public required string Expression { get; init; }     // Display string, e.g., "2d6+4"
    public required DateTimeOffset Timestamp { get; init; }
}
```

`RawRolls` always contains the individual die values for the **kept** set (before modifier). `AltRolls` contains the individual die values for the discarded set when rolling with advantage or disadvantage. For a normal roll of `2d6+4` where the dice show 3 and 5, `RawRolls = [3, 5]`, `AltRolls = null`, and `Result = 12`.

### DiceSimulatorGameState

The full game state for a session. Extends `AbstractGameState`.

```csharp
public class DiceSimulatorGameState(User host, ILogger<DiceSimulatorGameState> logger)
    : AbstractGameState(host, logger)
{
    // Roll history — internal list protected by a lock; exposed as IReadOnlyList via snapshot
    public IReadOnlyList<DiceRollEntry> RollHistory { get; }    // snapshot on each read
    public IReadOnlyDictionary<string, PlayerStats> PlayerStats { get; }  // ConcurrentDictionary

    public void AddRoll(DiceRollEntry entry);
    public PlayerStats GetOrAddPlayerStats(string playerId, string playerName);
    public void ClearHistory();
}
```

`RollHistory` is backed by a `List<DiceRollEntry>` protected with `lock`. Each read returns a `ToList()` snapshot. `PlayerStats` is a `ConcurrentDictionary<string, PlayerStats>` for safe concurrent access.

### PlayerStats

Pre-computed per-player statistics kept in sync with the roll history to avoid recalculating on every render.

```csharp
public class PlayerStats
{
    public string PlayerName { get; set; } = string.Empty;
    public int TotalRolls { get; set; }
    public int TotalDiceRolled { get; set; }      // Sum of DiceCount across all rolls
    public int NatTwentyCount { get; set; }       // Natural 20s on single-d20 rolls
    public int NatOneCount { get; set; }           // Natural 1s on single-d20 rolls
    public int HighestResult { get; set; }         // Highest effective total across all rolls
    public string? HighestResultExpression { get; set; }  // Expression that produced it
    public int CumulativeTotal { get; set; }       // Running sum of all effective results
    public Dictionary<DiceType, int> RollCountByDie { get; set; } = new();
}
```

Natural 20 and natural 1 tracking applies only to rolls where a single d20 is rolled (`1d20`, `1d20+N`, or `1d20-N`). This matches the D&D meaning of a "natural" result — the unmodified value of a lone d20.

---

## Game Engine

### DiceSimulatorGameEngine : AbstractGameEngine

Registered as a singleton in DI. Stateless — all mutable data lives in `DiceSimulatorGameState`. The engine directly exposes typed action methods rather than using a generic `HandleActionAsync` dispatcher.

#### CreateStateAsync

Creates a fresh `DiceSimulatorGameState` with empty roll history and player stats. Sets `IsJoinable = true`.

```csharp
public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(
    User host, CancellationToken ct = default)
```

#### StartAsync

Validates the caller is the host, then calls `state.Execute(() => state.UpdateJoinableStatus(false))` to close the lobby and begin the session.

```csharp
public override async Task<Result> StartAsync(
    User host, AbstractGameState state, CancellationToken ct = default)
```

#### RollDice

The primary game action. Executed inside `state.Execute()` to ensure serialization and subscriber notification.

```csharp
public Result RollDice(User player, DiceSimulatorGameState state, DiceRollAction action)
```

Processing steps inside `state.Execute()`:

1. Clamp `DiceCount` between 1 and 99.
2. Generate `DiceCount` random rolls via `IRandomNumberService.GetRandomInt(1, sides + 1, RandomType.Fast)`.
3. If `Advantage` or `Disadvantage`, generate a second set of rolls.
4. Sum each set and apply the modifier to get totals.
5. For `Advantage`, keep the higher total (and swap `rawRolls`/`altRolls` if needed). For `Disadvantage`, keep the lower.
6. Build the display expression string (e.g., `"2d6+4"`).
7. Create a `DiceRollEntry` and call `state.AddRoll(entry)`.
8. Retrieve or create the player's `PlayerStats` via `state.GetOrAddPlayerStats(playerId, playerName)`.
9. Under a `lock(stats)`, update: `TotalRolls`, `TotalDiceRolled`, `RollCountByDie`, nat 20/1 tracking (single-d20 only), `HighestResult`, `CumulativeTotal`.

#### ClearHistory

Host-only action. Resets roll history and all player stats.

```csharp
public Result ClearHistory(User user, DiceSimulatorGameState state)
```

---

## Random Number Generation

All dice rolls use `IRandomNumberService.GetRandomInt` with `RandomType.Fast`. This uses a non-cryptographic fast RNG seeded at application start — suitable for dice simulation where statistical uniformity matters more than cryptographic unpredictability.

```csharp
// In RollDice, per die:
rawRolls[i] = randomNumberService.GetRandomInt(1, sides + 1, RandomType.Fast);
```

`IRandomNumberService` is a singleton registered in `KnockBox.Core` / `LogicRegistrations`. All games share the same service instance.

---

## Razor Page

### DiceSimulatorLobby.razor — `/room/dice-simulator/{ObfuscatedRoomCode}`

The game-owned routable page that controls the full user experience. Inherits `DisposableComponent`.

```csharp
@page "/room/dice-simulator/{ObfuscatedRoomCode}"

public partial class DiceSimulatorLobby : DisposableComponent
{
    [Inject] protected DiceSimulatorGameEngine GameEngine { get; set; } = default!;
    [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
    [Inject] protected INavigationService NavigationService { get; set; } = default!;
    [Inject] protected IUserService UserService { get; set; } = default!;
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;

    [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

    private IDisposable? _stateSubscription;
    protected DiceSimulatorGameState? GameState { get; set; }
}
```

#### Session Validation

In `OnInitializedAsync`, the page validates the user has an active session via `IGameSessionService`, that the session URI matches the current URL, and that the state is a `DiceSimulatorGameState`. If any check fails, the user is redirected home.

#### Subscribe / Dispose

```csharp
// OnInitializedAsync:
_stateSubscription = GameState.StateChangedEventManager.Subscribe(
    async () => await InvokeAsync(StateHasChanged));

// Dispose():
_stateSubscription?.Dispose();
```

#### Phase-Switched UI

The page handles all phases by switching on `IsJoinable` and game state:

```
/room/dice-simulator/{ObfuscatedRoomCode}
│
├── Lobby Phase (GameState.IsJoinable == true)
│   ├── Joined player list
│   └── Start Session button (host only)
│
└── Gameplay Phase (GameState.IsJoinable == false)
    ├── LEFT PANEL — Roll Controls
    │   ├── Dice type picker (d4, d6, d8, d10, d12, d20, d100)
    │   ├── Dice count stepper (1–99)
    │   ├── Modifier input (+/- integer)
    │   ├── Expression preview (e.g., "2d6+4")
    │   ├── Roll mode selector (Normal / Advantage / Disadvantage)
    │   └── Roll button (calls GameEngine.RollDice)
    │
    └── RIGHT PANEL — Leaderboard
        ├── Player Stats Table
        │   (Player | Rolls | Dice Rolled | Nat20 | Nat1 | Highest | Cumulative)
        ├── Recent Rolls Feed (reverse-chronological)
        │   └── Each entry: timestamp, player, expression=result, kept rolls, alt rolls
        └── Export CSV button (any player)
```

---

## Real-Time Update Flow

```
Player clicks ROLL
       │
       ▼
DiceSimulatorLobby.razor
  calls GameEngine.RollDice(player, state, action)
       │
       ▼
state.Execute(() => {
  // generate rolls, create DiceRollEntry,
  // call state.AddRoll(entry),
  // update PlayerStats
})
  → releases lock
  → StateChangedEventManager.Notify() (fire-and-forget)
       │
       ├──► Player A's circuit → InvokeAsync(StateHasChanged) → UI re-renders
       ├──► Player B's circuit → InvokeAsync(StateHasChanged) → UI re-renders
       └──► Player C's circuit → InvokeAsync(StateHasChanged) → UI re-renders

All players see the new roll on the leaderboard simultaneously.
```

---

## Session Export (CSV)

Any player can export the full roll history as a CSV file at any point during the session.

### Export Flow

1. Player clicks "Export CSV".
2. The page reads the current `DiceSimulatorGameState.RollHistory` snapshot.
3. `CsvExportService.GenerateCsv(rollHistory)` serializes the history into a UTF-8 BOM CSV byte array on the server.
4. The CSV bytes are delivered to the player's browser via `IJSRuntime.InvokeVoidAsync("downloadFile", fileName, base64Csv)`. No file is written to disk on the server.
5. The download does not notify other players or modify game state — it is a local, read-only operation.

### CsvExportService

A static service class in `KnockBox.DiceSimulator`:

```csharp
public static class CsvExportService
{
    public static byte[] GenerateCsv(IReadOnlyList<DiceRollEntry> rollHistory)
}
```

### CSV Schema

| Column      | Type   | Example                   | Description                                               |
|-------------|--------|---------------------------|-----------------------------------------------------------|
| `Timestamp` | string | `2026-02-26T19:45:03Z`    | UTC timestamp (ISO 8601)                                  |
| `Player`    | string | `Alice`                   | Player display name                                       |
| `Expression`| string | `2d6+4`                   | Full dice notation                                        |
| `Mode`      | string | `Advantage`               | `Normal`, `Advantage`, or `Disadvantage`                  |
| `Result`    | int    | `12`                      | Effective (kept) total                                    |
| `DiceType`  | string | `d6`                      | Die type used                                             |
| `DiceCount` | int    | `2`                       | Number of dice rolled                                     |
| `Modifier`  | int    | `4`                       | Flat modifier applied (0 if none)                         |
| `KeptRolls` | string | `3;5`                     | Semicolon-separated die results for the kept set          |
| `AltRolls`  | string | `2;3`                     | Semicolon-separated die results for discarded set (empty if Normal) |
| `AltTotal`  | int    | `9`                       | Total of the discarded set (0 if Normal)                  |
| `RollId`    | GUID   | `a1b2c3d4-...`            | Unique identifier for the roll                            |

---

## Registration

```csharp
// In LogicRegistrations.RegisterLogic():
services.AddSingleton<DiceSimulatorGameEngine>();

// In LobbyService.CreateLobbyAsync GameType switch:
GameType.DiceSimulator => serviceProvider.GetService<DiceSimulatorGameEngine>(),
```

No changes to the lobby service interface, join flow, navigation infrastructure, or any other game module.

---

## File Structure

Source files live in `KnockBox.DiceSimulator` and `KnockBox` (UI only):

```
KnockBox.DiceSimulator/
├── Services/Logic/Games/DiceSimulator/
│   ├── CsvExportService.cs
│   └── DiceSimulatorGameEngine.cs
└── Services/State/Games/DiceSimulator/
    ├── DiceSimulatorGameState.cs
    └── Data/
        ├── DiceRollAction.cs
        ├── DiceRollEntry.cs
        ├── DiceType.cs
        ├── PlayerStats.cs
        └── RollMode.cs

KnockBox/Components/Pages/Games/DiceSimulator/
├── DiceSimulatorLobby.razor
├── DiceSimulatorLobby.razor.cs
└── DiceSimulatorLobby.razor.css
```

---

## Constraints & Trade-offs

**No win condition.** The session runs until the host manually clears history or the lobby is closed. There is no `GameOver` phase or automatic end condition.

**Stats updated inside execute lock.** `PlayerStats` are updated inside `state.Execute()`, but the individual `PlayerStats` object is updated under its own `lock(stats)` rather than relying solely on the outer semaphore. This prevents a race between the roll entry being committed and the stats being updated if a future refactor moves stats updates outside of `Execute`.

**Roll history snapshot on read.** `RollHistory` returns a `ToList()` snapshot each time it is accessed. This guarantees a consistent view for a render pass at the cost of a small allocation per read. For sessions with hundreds of rolls this remains negligible.

**`IRandomNumberService` shared across all rooms.** Unlike a per-room seeded `Random`, all dice rolls across all sessions share the same `IRandomNumberService` singleton. The fast RNG is statistically uniform; this is sufficient for a dice roller. Games that require verifiable per-room randomness sequences would need a per-state RNG seed.

**CSV export is server-side + JS interop.** The CSV is built in memory on the server and pushed to the client via a JS blob download. This means large sessions (500+ rolls) build the CSV on the server before it reaches the browser, but in practice this is well under 1 MB.
