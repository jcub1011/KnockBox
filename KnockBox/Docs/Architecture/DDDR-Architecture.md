# DnD Dice Roller — Game Module Architecture

## Overview

A pluggable game module for the Multi-Game Room Platform that provides a shared dice-rolling utility for tabletop RPG sessions. Players join a room, roll standard D&D dice with optional advantage/disadvantage, and see every roll appear on a real-time shared leaderboard. This is not a competitive game with win conditions — it is a persistent utility that runs until the host ends the session.

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

Advantage and disadvantage apply to the entire roll expression, not to individual dice within the expression. For example, `2d6+4` with advantage rolls `2d6+4` twice and keeps the higher total. This matches the D&D 5e rule where advantage/disadvantage applies to the d20 in an attack roll — but the module generalizes it to any expression so players can use it flexibly.

When rolling with advantage or disadvantage, both complete sets of individual die results are stored for transparency, but only the effective (kept) total is used for the leaderboard display.

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

### DiceRollerGameState

The full game state stored inside the room's `GameState` data dictionary. Because the platform stores game state as in-memory objects, there is no serialization cost.

```csharp
public class DiceRollerGameState
{
    public List<DiceRollEntry> RollHistory { get; set; } = new();
    public Dictionary<string, PlayerStats> PlayerStats { get; set; } = new();
}
```

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

### DiceRollerEngine : IGameEngine

```
ID:              "dice-roller"
Display Name:    "D&D Dice Roller"
Min Players:     1
Max Players:     20
```

This engine is registered as a singleton in DI and is stateless — all mutable data lives in `DiceRollerGameState` on the room. As a pure `IGameEngine` implementation, it is concerned only with game logic and state transitions and has no knowledge of UI components. All rendering decisions are owned by `DiceRollerPage.razor`.

#### InitializeAsync

Creates a fresh `DiceRollerGameState` with empty roll history and player stats. Sets the game phase to `"Rolling"`. Because this module has no rounds or win conditions, the game immediately enters its only phase.

#### HandleActionAsync

Accepts a `PlayerAction` with the following payload structure:

```csharp
public class DiceRollAction
{
    public DiceType DiceType { get; set; }
    public int DiceCount { get; set; } = 1;       // 1–99
    public int Modifier { get; set; } = 0;         // Can be negative
    public RollMode Mode { get; set; }
}
```

Processing steps:

1. Deserialize the action payload into `DiceRollAction`.
2. Validate `DiceCount` is between 1 and 99. Clamp or reject out-of-range values.
3. Generate `DiceCount` random rolls using the room's cryptographically seeded `Random` instance. If advantage or disadvantage, generate a second complete set of `DiceCount` rolls.
4. Sum each set and apply the modifier to get the totals.
5. For advantage, keep the higher total; for disadvantage, keep the lower total. For normal, there is only one total.
6. Build the display expression string (e.g., `"2d6+4"`, `"1d20"`, `"4d6-1"`).
7. Create a `DiceRollEntry` with the current UTC timestamp, storing both roll sets.
8. Append the entry to `DiceRollerGameState.RollHistory`.
9. Update the rolling player's `PlayerStats` — increment counts, check for nat 1/20 (only on single-d20 rolls), update highest result, add to cumulative total.
10. Return the updated `GameState`.

The room's `NotifyStateChanged` fires automatically after the handler returns, pushing the new roll to every connected player.

#### IsGameOver

Always returns `false`. The session runs until the host manually ends it or the room is cleaned up by the background service. A host-triggered `"EndSession"` action can optionally transition the room to `Finished`.

---

## Razor Pages & Components

### DiceRollerPage.razor — `/room/dice-roller/{Code}`

The game-owned routable page that controls the full user experience for this module. Following the platform convention, the `@page` directive uses the route segment `dice-roller`, which must match the `Id` property returned by `DiceRollerEngine`. The page handles all phases internally — lobby, gameplay, and session end — by switching on the room's current status.

**Lobby phase** (`Room.Status == Lobby`): Shows the list of joined players and a "Start Session" button visible only to the host. Minimal — the lobby for a utility like this just confirms who is in the room.

**Gameplay phase** (`Room.Status == InProgress`): The primary view, composed of two panels:

```
┌───────────────────────────────────────────────────┐
│                D&D Dice Roller                    │
├───────────────────────┬───────────────────────────┤
│                       │                           │
│   ROLL CONTROLS       │   ROLL LEADERBOARD        │
│                       │                           │
│  ┌─────────────────┐  │   Player Stats Summary    │
│  │  Dice Picker    │  │   ────────────────────    │
│  │  d4 d6 d8 d10  │  │   Name | Rolls | Nat20   │
│  │  d12 d20 d100  │  │   ────────────────────    │
│  └─────────────────┘  │                           │
│                       │   Recent Rolls Feed       │
│  ┌─────────────────┐  │   ────────────────────    │
│  │  Count: [2]     │  │   [2s ago] Alice          │
│  │  Modifier: [+4] │  │   2d6+4 = 12             │
│  │  ──────────     │  │   (rolls: 3, 5 + 4)      │
│  │  Expression:    │  │                           │
│  │    2d6+4        │  │   [15s ago] Bob           │
│  └─────────────────┘  │   1d20 = 17 (Adv)        │
│                       │   (kept: 17 | alt: 4)     │
│  ┌─────────────────┐  │                           │
│  │  Roll Mode      │  │   [1m ago] Alice          │
│  │  ○ Normal       │  │   8d6 = 28               │
│  │  ○ Advantage    │  │   (rolls: 4,3,6,2,5,     │
│  │  ○ Disadvant.   │  │    1,3,4)                 │
│  └─────────────────┘  │                           │
│                       │                           │
│  ┌─────────────────┐  │                           │
│  │   [ ROLL! ]     │  │                           │
│  └─────────────────┘  │                           │
│                       │  ┌─────────────────────┐  │
│  Last: 2d6+4 = 12    │  │  [ Export CSV ▼ ]   │  │
│                       │  └─────────────────────┘  │
├───────────────────────┴───────────────────────────┤
│  Room: ABCD  │  Players: 4  │  Total Rolls: 47   │
└───────────────────────────────────────────────────┘
```

#### Roll Controls Panel (Left)

- **Dice Picker**: A button group for selecting the die type. Each button displays the die name (d4, d6, etc.). The selected die is visually highlighted. Defaults to d20.
- **Dice Count**: A numeric input (1–99) for setting how many dice to roll. Defaults to 1. Controlled via a stepper (−/+ buttons) and direct text entry.
- **Modifier**: A numeric input for the flat modifier applied after summing the dice. Accepts positive and negative integers. Defaults to 0. Displayed with its sign (e.g., `+4`, `-1`).
- **Expression Preview**: A live-updating read-only display that combines the current selections into standard notation (e.g., `2d6+4`, `1d20`, `4d6-1`). Updates instantly as the player changes any control, giving them confirmation of what they are about to roll.
- **Roll Mode**: A radio button group for Normal / Advantage / Disadvantage.
- **Roll Button**: Submits a `DiceRollAction` via `RoomManager.HandleActionAsync`. Disabled briefly after each roll to prevent accidental double-rolls (300ms debounce).
- **Last Roll Display**: Shows the current player's most recent roll result with the full expression and total, providing immediate local feedback.

#### Leaderboard Panel (Right)

This panel is the core of the module — a real-time feed shared by all players.

**Player Stats Summary Table**

| Player  | Rolls | Dice Rolled | Nat 20 | Nat 1 | Highest         | Cumulative |
|---------|-------|-------------|--------|-------|-----------------|------------|
| Alice   | 12    | 34          | 2      | 1     | 48 (8d6)        | 187        |
| Bob     | 8     | 10          | 0      | 3     | 24 (1d20+4)     | 94         |

Sorted by total rolls descending. Updates in real time as rolls come in from any player. "Dice Rolled" is the total number of individual dice the player has thrown (sum of `DiceCount` across all rolls), while "Rolls" is the number of roll actions taken.

**Recent Rolls Feed**

A reverse-chronological scrollable list of all `DiceRollEntry` records. Each entry displays:

- Timestamp (formatted as relative time, e.g., "2s ago", "1m ago")
- Player name
- Full expression and effective total, e.g., `2d6+4 = 12`
- Individual die results, e.g., `(rolls: 3, 5 + 4)`
- For advantage/disadvantage: both totals and which was kept, e.g., `(Adv — kept: 12 [3,5+4] | alt: 9 [2,3+4])`

The feed auto-scrolls to the latest entry when a new roll arrives. A configurable cap (default: last 200 entries displayed) prevents DOM bloat in long sessions. The full history remains in `DiceRollerGameState` for stats computation.

---

## Real-Time Update Flow

The update mechanism uses the platform's existing subscribe/notify pattern — no additional infrastructure required.

```
Player A clicks ROLL
       │
       ▼
DiceRollerPage.razor
  calls RoomManager.HandleActionAsync(roomCode, playerAction)
       │
       ▼
RoomManager acquires per-room lock
  delegates to DiceRollerEngine.HandleActionAsync
       │
       ▼
DiceRollerEngine:
  1. Generates roll(s)
  2. Creates DiceRollEntry
  3. Appends to RollHistory
  4. Updates PlayerStats
  5. Returns updated GameState
       │
       ▼
Room.NotifyStateChanged fires
       │
       ├──► Player A's circuit → InvokeAsync(StateHasChanged) → UI re-renders
       ├──► Player B's circuit → InvokeAsync(StateHasChanged) → UI re-renders
       ├──► Player C's circuit → InvokeAsync(StateHasChanged) → UI re-renders
       └──► Player D's circuit → InvokeAsync(StateHasChanged) → UI re-renders

All players see the new roll on the leaderboard simultaneously.
```

---

## Random Number Generation

Fairness is critical for a dice roller. Each room initializes its own `Random` instance seeded from `System.Security.Cryptography.RandomNumberGenerator` at creation time. This provides statistically uniform distribution without the performance overhead of calling the crypto RNG on every roll.

```csharp
// At room creation
var seed = new byte[4];
RandomNumberGenerator.Fill(seed);
var roomRandom = new Random(BitConverter.ToInt32(seed));

// On each roll — generate DiceCount individual results
int sides = (int)diceType;
int[] rolls = new int[diceCount];
for (int i = 0; i < diceCount; i++)
{
    rolls[i] = roomRandom.Next(1, sides + 1);
}
int total = rolls.Sum() + modifier;
```

All rolls within a room go through this single `Random` instance, which is safe because access is serialized by the per-room lock that already exists in the platform.

---

## Action Definitions

| Action Type     | Payload                                         | Who Can Trigger | Description                              |
|-----------------|-------------------------------------------------|-----------------|------------------------------------------|
| `RollDice`      | `{ DiceType, DiceCount, Modifier, RollMode }`  | Any player      | Perform a dice roll                      |
| `ExportCsv`     | `{}`                                             | Any player      | Generate and download session roll history as CSV |
| `ClearHistory`  | `{}`                                             | Host only       | Reset all roll history and stats         |
| `EndSession`    | `{}`                                             | Host only       | Transition room to Finished status       |

---

## Registration

Following the platform's three-step plugin process:

1. **Implement `IGameEngine`** — `DiceRollerEngine` defines game metadata and the initialize/action/game-over logic as a stateless state machine.
2. **Create Razor page** — `DiceRollerPage.razor` with `@page "/room/dice-roller/{Code}"` handles the lobby, gameplay, and session end phases.
3. **Register in DI** — one line in `Program.cs`:

```csharp
builder.Services.AddSingleton<IGameEngine, DiceRollerEngine>();
```

No changes to the room manager, join page, routing infrastructure, or any other game module.

---

## Lifecycle Considerations

**Session Duration**: D&D sessions can run 3–5+ hours, significantly longer than a typical party game round. The room cleanup background service should treat dice roller rooms with connected players as active regardless of the "last action" timestamp. The inactivity threshold only applies after all players have disconnected.

**Roll History Growth**: A typical session might produce 200–500 rolls. With multi-die support, each `DiceRollEntry` is larger due to the `RawRolls` and `AltRolls` arrays, but even a worst-case entry (99 dice with advantage) is under 1 KB. A 500-roll session stays comfortably under 500 KB per room — negligible memory impact even with dozens of concurrent rooms.

**Reconnection**: If a player's circuit drops and they rejoin the same room, they see the full current leaderboard and roll history immediately because all state is held in the room object. No catch-up synchronization is needed.

---

## Session Export (CSV)

Any player can export the full roll history as a CSV file at any point during the session. This is triggered by the "Export CSV" button in the lower-right of the leaderboard panel.

### Export Flow

1. Player clicks "Export CSV".
2. The page reads the current `DiceRollerGameState.RollHistory` in memory.
3. A `CsvExportService` (scoped or static helper) serializes the roll history into a CSV byte array entirely on the server.
4. The CSV is delivered to the player's browser via Blazor's `IJSRuntime` using a JavaScript interop call that creates a temporary Blob URL and triggers a download. No file is written to disk on the server.
5. The download does not notify other players or modify game state — it is a local, read-only operation.

### CSV Schema

Each row represents one roll action. Columns are ordered for readability in a spreadsheet.

| Column               | Type            | Example               | Description                                                  |
|----------------------|-----------------|-----------------------|--------------------------------------------------------------|
| `Timestamp`          | ISO 8601 string | `2026-02-26T19:45:03Z`| UTC timestamp of the roll                                    |
| `Player`             | string          | `Alice`               | Player display name                                          |
| `Expression`         | string          | `2d6+4`               | Full dice notation                                           |
| `Mode`               | string          | `Advantage`           | `Normal`, `Advantage`, or `Disadvantage`                     |
| `Result`             | int             | `12`                  | Effective (kept) total                                       |
| `DiceType`           | string          | `d6`                  | Die type used                                                |
| `DiceCount`          | int             | `2`                   | Number of dice rolled                                        |
| `Modifier`           | int             | `4`                   | Flat modifier applied (0 if none)                            |
| `KeptRolls`          | string          | `3;5`                 | Semicolon-separated individual die results for the kept set  |
| `AltRolls`           | string          |  `2;3`                | Semicolon-separated die results for discarded set (empty if Normal) |
| `AltTotal`           | int             | `9`                   | Total of the discarded set (0 if Normal)                     |
| `RollId`             | GUID            | `a1b2c3d4-...`        | Unique identifier for the roll                               |

### Example CSV Output

```csv
Timestamp,Player,Expression,Mode,Result,DiceType,DiceCount,Modifier,KeptRolls,AltRolls,AltTotal,RollId
2026-02-26T19:45:03Z,Alice,2d6+4,Normal,12,d6,2,4,3;5,,0,a1b2c3d4-e5f6-7890-abcd-ef1234567890
2026-02-26T19:45:18Z,Bob,1d20,Advantage,17,d20,1,0,17,4,4,b2c3d4e5-f6a7-8901-bcde-f12345678901
2026-02-26T19:46:01Z,Alice,8d6,Normal,28,d6,8,0,4;3;6;2;5;1;3;4,,0,c3d4e5f6-a7b8-9012-cdef-123456789012
```

### CSV Generation

```csharp
public static class CsvExportService
{
    public static byte[] GenerateCsv(List<DiceRollEntry> rollHistory)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(withBOM: true));

        writer.WriteLine("Timestamp,Player,Expression,Mode,Result,DiceType,DiceCount,Modifier,KeptRolls,AltRolls,AltTotal,RollId");

        foreach (var entry in rollHistory)
        {
            var keptRolls = string.Join(";", entry.RawRolls);
            var altRolls = entry.AltRolls is not null
                ? string.Join(";", entry.AltRolls)
                : string.Empty;

            writer.WriteLine(string.Join(",",
                entry.Timestamp.ToString("o"),
                CsvEscape(entry.PlayerName),
                entry.Expression,
                entry.Mode,
                entry.Result,
                $"d{(int)entry.DiceType}",
                entry.DiceCount,
                entry.Modifier,
                keptRolls,
                altRolls,
                entry.AltTotal,
                entry.Id
            ));
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
```

The file is named `DnD-Rolls-{RoomCode}-{ExportTimestamp}.csv` (e.g., `DnD-Rolls-ABCD-20260226T194630Z.csv`). UTF-8 BOM is included so Excel opens the file correctly without an import wizard.

### JavaScript Interop for Download

```javascript
// wwwroot/js/fileExport.js
window.downloadCsvFile = (fileName, base64Data) => {
    const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));
    const blob = new Blob([bytes], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
```

The Blazor page calls this via `IJSRuntime.InvokeVoidAsync("downloadCsvFile", fileName, base64Csv)`. This is the only JavaScript interop in the entire module.

---

## Future Extensions

These are not part of the initial implementation but are natural additions the architecture supports without structural changes.

- **Roll Labels**: Optional free-text label on a roll (e.g., "Attack roll", "Fireball damage") for context in the feed. Would add a `Label` column to the CSV export.
- **Sound Effects**: Client-side dice roll sounds triggered by a CSS animation on new entries in the feed.
- **Stat Highlights**: Visual callouts for notable events — nat 20s, nat 1s, streaks, highest roll of the session.
- **Quick Roll Presets**: Saveable shortcuts per player (e.g., "Greatsword: 2d6+5", "Eldritch Blast: 1d10+4") for one-tap rolling of frequently used expressions.
