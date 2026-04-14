# Phase 1: Data Models, Board Graph, and Static Definitions

## Goal

Establish all pure data types and the board graph that every subsequent phase depends on. These are pure C# classes with no FSM or engine dependencies, so they can be tested in total isolation.

## Prerequisites

- None. This is the foundation layer.

---

## Platform Context

KnockBox is a Blazor Server app with a plugin architecture. Each game is a Razor Class Library that references only `KnockBox.Core`. The Hidden Agenda project already exists at `KnockBox.HiddenAgenda/` with a stub module, engine, state, and lobby page. The GDD is at `KnockBox.HiddenAgenda/Specs/HiddenAgendaGDD.md`.

All paths below are relative to `KnockBox.HiddenAgenda/` unless otherwise noted.

---

## Files to Create

### 1. `Services/Logic/Games/Data/BoardGraph.cs`

The board as a graph data structure.

**Types to define:**

```csharp
public enum Wing { GrandHall, ModernWing, SculptureGarden, RestorationRoom, Corridor }
public enum SpotType { Curation, Event }

public record BoardSpace(int Id, string Name, Wing Wing, SpotType SpotType);
```

**`BoardGraph` class:**
- `IReadOnlyDictionary<int, BoardSpace> Spaces` -- all spaces keyed by ID
- `IReadOnlyDictionary<int, IReadOnlyList<int>> Adjacency` -- bidirectional edges (each space lists its neighbors)
- `List<BoardSpace> GetReachableSpaces(int fromSpaceId, int maxDistance)` -- BFS returning all spaces reachable within 1..N steps. Must handle forks (multiple adjacencies at each node). Returns only spaces at distance >= 1 (can't stay in place). The UI will present these as clickable destinations.
- `int GetShortestDistance(int from, int to)` -- BFS shortest path length between two spaces.

**BFS implementation notes:**
- Standard BFS from `fromSpaceId` tracking visited nodes and distance.
- At each node, explore all adjacent nodes not yet visited.
- Collect all nodes with distance >= 1 and distance <= maxDistance.
- Fork handling is natural: BFS explores all branches.

### 2. `Services/Logic/Games/Data/BoardDefinitions.cs`

Static factory that builds "The Grand Circuit" layout per the GDD.

**Layout (from GDD Section "The Gallery"):**
- Main loop: ~24 spaces passing through 4 wings in order: Grand Hall -> Modern Wing -> Sculpture Garden -> Restoration Room -> back to Grand Hall.
- Each wing: 4 Curation Spots + 1 Event Spot = 5 spaces per wing section on the main loop.
- Two shortcut corridors crossing the center: Grand Hall <-> Sculpture Garden (3-4 spaces), Modern Wing <-> Restoration Room (3-4 spaces).

**Suggested space layout (adjust exact counts in playtesting):**

Main loop (20 spaces, 5 per wing):
- Grand Hall: spaces 0-4 (4 Curation, 1 Event)
- Modern Wing: spaces 5-9 (4 Curation, 1 Event)
- Sculpture Garden: spaces 10-14 (4 Curation, 1 Event)
- Restoration Room: spaces 15-19 (4 Curation, 1 Event)

Shortcuts (4 corridor spaces):
- GH-SG shortcut: spaces 20-21 (Curation spots in Corridor wing), connects space 2 <-> space 12
- MW-RR shortcut: spaces 22-23 (Curation spots in Corridor wing), connects space 7 <-> space 17

Main loop adjacency: 0-1, 1-2, 2-3, 3-4, 4-5, 5-6, 6-7, 7-8, 8-9, 9-10, 10-11, 11-12, 12-13, 13-14, 14-15, 15-16, 16-17, 17-18, 18-19, 19-0

Shortcut adjacency: 2-20, 20-21, 21-12 (GH-SG) and 7-22, 22-23, 23-17 (MW-RR)

**Static method:** `static BoardGraph CreateGrandCircuit()` that builds and returns the full graph.

### 3. `Services/Logic/Games/Data/CollectionDefinitions.cs`

**Types:**

```csharp
public enum CollectionType
{
    RenaissanceMasters,
    ContemporaryShowcase,
    ImpressionistGallery,
    MarbleAndBronze,
    EmergingArtists
}

public record CollectionDefinition(CollectionType Type, string Name, int TargetValue, Wing PrimaryWing);
```

**Static data (from GDD):**

| Collection | Target | Wing |
|---|---|---|
| Renaissance Masters | 12 | Grand Hall |
| Contemporary Showcase | 10 | Grand Hall |
| Impressionist Gallery | 10 | Modern Wing |
| Marble & Bronze | 8 | Sculpture Garden |
| Emerging Artists | 8 | Sculpture Garden |

**Static list:** `static IReadOnlyList<CollectionDefinition> All` containing all 5.

**Helper:** `static CollectionDefinition Get(CollectionType type)`.

### 4. `Services/Logic/Games/Data/CurationCardDefinitions.cs`

**Types:**

```csharp
public enum CurationCardType { Acquire, Remove, Trade }

public record CollectionEffect(CollectionType Collection, int Delta);

public record CurationCard(
    CurationCardType Type,
    string Description,
    IReadOnlyList<CollectionEffect> Effects,
    IReadOnlyList<CollectionEffect>? AlternateEffects = null  // For Trade cards: player picks Effects or AlternateEffects
);
```

**Card pool design (from GDD):**

Each wing has a local pool weighted toward its associated collections:
- 50-60% Acquire cards
- 15-20% Remove cards
- 20-30% Trade cards

**Grand Hall pool examples:**
- Acquire: "+2 Renaissance Masters", "+1 Renaissance Masters, +1 Contemporary Showcase", "+3 to any Grand Hall collection"
- Remove: "-1 Contemporary Showcase", "-2 Renaissance Masters"
- Trade: "+2 Renaissance Masters OR +1 Impressionist Gallery and +1 Emerging Artists"

**Each wing** should have ~15-20 cards in its pool, weighted toward that wing's collections but including some cross-wing effects (especially in Restoration Room, which applies to any collection at lower values).

**`CurationCardPool` class:**
- `static IReadOnlyList<CurationCard> GetPool(Wing wing)` -- returns the full card pool for a wing
- `static List<CurationCard> DrawThree(IRandomNumberService rng, Wing wing)` -- draws 3 random cards from the wing's pool (with replacement, since the pool represents available card types, not a finite deck)

### 5. `Services/Logic/Games/Data/EventCardDefinitions.cs`

```csharp
public enum EventCardType { Catalog, Detour }

public record EventCard(EventCardType Type, string Description);
```

**Static instances:**
- `Catalog`: "View another player's last 3 drawn Curation Cards (including what they discarded). The target knows they were Cataloged but not what you learned."
- `Detour`: "After spinning, use another player's last movement (their previous spinner result and destination) instead of your own."

### 6. `Services/Logic/Games/Data/TaskDefinitions.cs`

**Types:**

```csharp
public enum TaskCategory { Devotion, Style, Movement, Neglect, Rivalry }
public enum TaskDifficulty { Easy, Medium, Hard }

public record SecretTask(
    string Id,
    TaskCategory Category,
    TaskDifficulty Difficulty,
    string Description,
    string ObservablePattern,
    int PointValue
);
```

**All 31 tasks from GDD:**

Devotion (Easy, 1pt): D1-D7
- D1: "Play a card that adds progress to Renaissance Masters on at least 4 separate turns."
- D2: "Play a card that adds progress to Contemporary Showcase on at least 4 separate turns."
- D3: "Play a card that adds progress to Impressionist Gallery on at least 4 separate turns."
- D4: "Play a card that adds progress to Marble & Bronze on at least 4 separate turns."
- D5: "Play a card that adds progress to Emerging Artists on at least 4 separate turns."
- D6: "Play cards affecting Grand Hall collections on at least 5 separate turns."
- D7: "Play cards affecting Sculpture Garden collections on at least 5 separate turns."

Neglect (Hard, 3pt): N1-N6
- N1: "Never play an Acquire card on Renaissance Masters for the entire round."
- N2: "Never play an Acquire card on Contemporary Showcase for the entire round."
- N3: "Never play an Acquire card on Impressionist Gallery for the entire round."
- N4: "Never enter the Grand Hall for the entire round."
- N5: "Never enter the Modern Wing for the entire round."
- N6: "Never play a Remove card for the entire round."

Style (Medium, 2pt): Y1-Y6
- Y1: "Play a Remove card on at least 3 separate turns."
- Y2: "Play cards affecting at least 4 different collections across the round."
- Y3: "Play a card affecting the same collection at least 3 turns in a row."
- Y4: "Alternate between Acquire and Remove cards for at least 4 consecutive turns."
- Y5: "Play the highest-value card in your hand on at least 4 turns."
- Y6: "Visit an Event Spot at least 3 times during the round."

Movement (Medium, 2pt): M1-M6
- M1: "Visit all four wings at least once during the round."
- M2: "Spend at least 4 turns in the same wing."
- M3: "End your turn on the same spot as another player at least 3 times."
- M4: "Take the longest available path at every fork for at least 4 consecutive turns."
- M5: "Change wings every turn for at least 4 consecutive turns."
- M6: "Return to the same spot at least 3 times during the round."

Rivalry (Hard, 3pt): R1-R6
- R1: "Play an Acquire card on a collection immediately after another player plays a Remove card on that same collection, at least 3 times."
- R2: "Play a card affecting the same collection that the player immediately before you affected, on at least 4 turns."
- R3: "Never play a card affecting the same collection as the player immediately before you, for at least 5 consecutive turns."
- R4: "Play a Remove card on a collection that is currently the highest-progress collection, at least 3 times."
- R5: "Play an Acquire card on a collection that is currently the lowest-progress collection, at least 3 times."
- R6: "Be in the same wing as a specific other player (assigned randomly at task draw) on at least 4 turns."

**`TaskPool` class:**
- `static IReadOnlyList<SecretTask> AllTasks` -- all 31 tasks
- `static IReadOnlyList<SecretTask> GetPoolForPlayerCount(int playerCount)` -- returns subset per GDD: 25 for 3 players, 30 for 4+
- `static List<SecretTask> DrawTasks(IRandomNumberService rng, IReadOnlyList<SecretTask> pool, int count)` -- draws N unique tasks from pool without replacement

### 7. `Services/State/Games/Data/HiddenAgendaPlayerState.cs`

Per-player mutable state tracking everything needed for gameplay and task evaluation.

```csharp
public class HiddenAgendaPlayerState
{
    public string PlayerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Position
    public int CurrentSpaceId { get; set; }

    // Secret tasks (3 per round)
    public List<SecretTask> SecretTasks { get; set; } = [];

    // Event card (hold max 1)
    public EventCard? HeldEventCard { get; set; }

    // Guess submission
    public bool HasSubmittedGuess { get; set; }
    // Key: opponent player ID, Value: list of 3 guessed task IDs
    public Dictionary<string, List<string>>? GuessSubmission { get; set; }

    // Scoring
    public int RoundScore { get; set; }
    public int CumulativeScore { get; set; }

    // Turn tracking
    public int TurnsTakenThisRound { get; set; }
    public int GuessCountdownTurnsRemaining { get; set; } // Set when countdown triggers

    // Spin/movement history
    public int LastSpinResult { get; set; }
    public int? LastMoveDestination { get; set; }
    public List<MovementRecord> MovementHistory { get; set; } = [];

    // Card history (for task evaluation and Catalog event)
    public List<CardPlayRecord> CardPlayHistory { get; set; } = [];
    public List<CardDrawRecord> CardDrawHistory { get; set; } = [];
}

public record MovementRecord(int TurnNumber, int SpaceId, Wing Wing);

public record CardPlayRecord(
    int TurnNumber,
    CurationCard Card,
    int SelectedIndex,           // Which of the 3 drawn cards was selected
    CollectionType[] AffectedCollections,
    CurationCardType CardType
);

public record CardDrawRecord(
    int TurnNumber,
    List<CurationCard> DrawnCards  // All 3 drawn cards (for Catalog reveals)
);
```

### 8. `Services/State/Games/Data/HiddenAgendaGameConfig.cs`

```csharp
public enum TaskPoolRotation { Full, Partial, Fixed }

public class HiddenAgendaGameConfig
{
    public int TotalRounds { get; set; } = 4;
    public int RoundSetupTimeoutMs { get; set; } = 10000;
    public int EventCardPhaseTimeoutMs { get; set; } = 10000;
    public int SpinPhaseTimeoutMs { get; set; } = 10000;
    public int MovePhaseTimeoutMs { get; set; } = 15000;
    public int DrawPhaseTimeoutMs { get; set; } = 15000;
    public int GuessPhaseTimeoutMs { get; set; } = 60000;
    public int FinalGuessTimeoutMs { get; set; } = 45000;
    public int RevealTimeoutMs { get; set; } = 15000;
    public bool EnableTimers { get; set; } = true;
    public TaskPoolRotation PoolRotation { get; set; } = TaskPoolRotation.Partial;
}
```

---

## File to Modify

### `Services/State/Games/HiddenAgendaGameState.cs`

Expand the existing stub to support all the state needed by the game.

**Current state (stub):**
```csharp
public class HiddenAgendaGameState(User host, ILogger<HiddenAgendaGameState> logger)
    : AbstractGameState(host, logger),
      IPhasedGameState<GamePhase>
{
    public GamePhase Phase { get; private set; }
    public void SetPhase(GamePhase phase) { Phase = phase; }
}

public enum GamePhase { Lobby, Playing, GameOver }
```

**Changes:**

1. Expand `GamePhase` enum:
```csharp
public enum GamePhase
{
    Lobby,
    RoundSetup,
    EventCardPhase,
    SpinPhase,
    MovePhase,
    DrawPhase,
    GuessPhase,
    FinalGuess,
    Reveal,
    RoundOver,
    MatchOver
}
```

2. Add interfaces (following the Codeword pattern from `KnockBox.Codeword/Services/State/Games/CodewordGameState.cs`):
```csharp
public class HiddenAgendaGameState(User host, ILogger<HiddenAgendaGameState> logger)
    : AbstractGameState(host, logger),
      IPhasedGameState<GamePhase>,
      IConfigurableGameState<HiddenAgendaGameConfig>,
      IPlayerTrackedGameState<HiddenAgendaPlayerState>,
      IFsmContextGameState<HiddenAgendaGameContext>
```

The interfaces are defined in `KnockBox.Core/Services/State/Games/Shared/Interfaces/`:
- `IPhasedGameState<TPhase>` -- Phase property + SetPhase
- `IConfigurableGameState<TConfig>` -- Config property
- `IPlayerTrackedGameState<TPlayerState>` -- ConcurrentDictionary<string, TPlayerState> GamePlayers
- `IFsmContextGameState<TContext>` -- Context property

3. Add properties:
```csharp
// FSM context (set when game starts)
public HiddenAgendaGameContext? Context { get; set; }

// Configuration
public HiddenAgendaGameConfig Config { get; set; } = new();

// Player state
public ConcurrentDictionary<string, HiddenAgendaPlayerState> GamePlayers { get; } = new();

// Turn management
public TurnManager TurnManager { get; } = new();

// Board
public BoardGraph BoardGraph { get; set; } = null!;

// Collection progress (mutable, reset each round)
public Dictionary<CollectionType, int> CollectionProgress { get; } = new();

// Round tracking
public int CurrentRound { get; set; }

// Guess countdown
public bool GuessCountdownActive { get; set; }
public string? FirstGuessPlayerId { get; set; }

// Task pool for current round
public IReadOnlyList<SecretTask> CurrentTaskPool { get; set; } = [];

// Global play history for cross-player task evaluation (Rivalry tasks)
public List<TurnRecord> RoundPlayHistory { get; } = [];

// Reachable spaces for current player during MovePhase (set by FSM state)
public List<BoardSpace>? ReachableSpaces { get; set; }

// Current player's drawn cards during DrawPhase (set by FSM state)
public List<CurationCard>? DrawnCards { get; set; }
```

4. Add `TurnRecord`:
```csharp
public record TurnRecord(
    int TurnNumber,
    string PlayerId,
    CardPlayRecord? CardPlay,
    int SpaceId,
    Wing Wing
);
```

5. Update `SetPhase` to call `NotifyStateChanged()` (matching Codeword pattern):
```csharp
public void SetPhase(GamePhase phase)
{
    Phase = phase;
    NotifyStateChanged();
}
```

---

## Tests to Write

All test files go in `KnockBox.HiddenAgendaTests/Unit/Logic/Games/HiddenAgenda/Data/`.

### `BoardGraphTests.cs`

```
- Graph construction: Verify all 24 spaces exist with correct Wing and SpotType
- Each wing has exactly 4 Curation + 1 Event spot
- Adjacency: main loop is connected (can reach any space from any other)
- GetReachableSpaces with maxDistance=1 returns only immediate neighbors
- GetReachableSpaces with maxDistance=3 from a fork point returns spaces on both branches
- GetReachableSpaces does not include the starting space
- Shortcut corridors provide alternate paths (verify shorter distance via shortcut than main loop)
- GetShortestDistance: adjacent spaces = 1, opposite sides of loop uses shortcut
```

### `CollectionTests.cs`

```
- All 5 collections defined
- Correct target values: 12, 10, 10, 8, 8
- Correct wing assignments
- No duplicate collection types
```

### `CurationCardTests.cs`

```
- Each wing has a card pool
- Pool composition: verify Acquire cards are 50-60%, Remove 15-20%, Trade 20-30% (approximate)
- DrawThree returns exactly 3 cards
- Grand Hall pool cards primarily affect Renaissance Masters / Contemporary Showcase
- Trade cards have non-null AlternateEffects
- Acquire card effects all have positive deltas
- Remove card effects all have negative deltas
```

### `TaskDefinitionTests.cs`

```
- AllTasks contains exactly 31 tasks
- 7 Devotion tasks (D1-D7), 6 Neglect (N1-N6), 6 Style (Y1-Y6), 6 Movement (M1-M6), 6 Rivalry (R1-R6)
- Devotion tasks: difficulty Easy, 1 point
- Neglect tasks: difficulty Hard, 3 points
- Style tasks: difficulty Medium, 2 points
- Movement tasks: difficulty Medium, 2 points
- Rivalry tasks: difficulty Hard, 3 points
- All task IDs are unique
- GetPoolForPlayerCount(3) returns 25 tasks
- GetPoolForPlayerCount(4) returns 30 tasks
- GetPoolForPlayerCount(5) returns 30 tasks
- GetPoolForPlayerCount(6) returns 30 tasks
- DrawTasks draws correct count without replacement
```

---

## Verification

- `dotnet build KnockBox.HiddenAgenda/KnockBox.HiddenAgenda.csproj` succeeds
- `dotnet test KnockBox.HiddenAgendaTests/KnockBox.HiddenAgendaTests.csproj` -- all new tests pass
- Board graph can be traversed from any space to any other space
- Card pools generate correct distributions
- Task pools produce correct subsets for all player counts
