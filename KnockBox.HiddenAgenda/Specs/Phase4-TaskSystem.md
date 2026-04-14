# Phase 4: Task System (Tracking and Completion Evaluation)

## Goal

Implement task completion evaluation logic. Each of the 31 secret tasks has specific behavioral criteria that are evaluated against a player's recorded history at the end of each round. This phase adds the evaluation functions -- pure logic over play history data. **This phase can be developed in parallel with Phase 3** since it only depends on Phase 1 data models.

## Prerequisites

Phase 1 (data models) must be complete. Phase 2's context class will be modified. Phase 3 provides the play history recording, but the evaluation functions can be tested with manually-constructed history data.

Key types used:
- `SecretTask` (Id, Category, Description) from `Services/Logic/Games/Data/TaskDefinitions.cs`
- `TaskCategory` enum (Devotion, Style, Movement, Neglect, Rivalry)
- `CardPlayRecord` (TurnNumber, Card, AffectedCollections, CardType) from `Services/State/Games/Data/HiddenAgendaPlayerState.cs`
- `MovementRecord` (TurnNumber, SpaceId, Wing) from same file
- `CardDrawRecord` (TurnNumber, DrawnCards) from same file
- `HiddenAgendaPlayerState` -- CardPlayHistory, MovementHistory
- `HiddenAgendaGameState` -- RoundPlayHistory (global turn log), CollectionProgress
- `CollectionType`, `CurationCardType`, `Wing` enums
- `TurnRecord` (TurnNumber, PlayerId, CardPlay, SpaceId, Wing)
- `HiddenAgendaGameContext` -- where evaluation methods will live

---

## Game Design Context

From the GDD, each task creates a **behavioral pattern** that opponents can observe. Task evaluation checks whether the player's recorded actions match the required pattern. All evaluation happens at round end.

### Task Categories and Evaluation Logic

**Devotion (D1-D7, Easy, 1pt):** Player must repeatedly favor a specific collection or wing.
- D1-D5: Count turns where player played a card adding progress to the specified collection. Threshold: >= 4 separate turns.
- D6: Count turns where player played cards affecting either Grand Hall collection (Renaissance Masters or Contemporary Showcase). Threshold: >= 5 turns.
- D7: Count turns affecting Sculpture Garden collections (Marble & Bronze or Emerging Artists). Threshold: >= 5 turns.

**Evaluation approach:** Scan `player.CardPlayHistory`, for each record check if `AffectedCollections` contains the target collection(s) and the card type was Acquire (positive delta). Count distinct turns.

**Neglect (N1-N6, Hard, 3pt):** Player must avoid a specific action for the entire round.
- N1-N3: Never play an Acquire card on the specified collection. Check that no CardPlayRecord has CardType == Acquire AND AffectedCollections contains the target.
- N4-N5: Never enter the specified wing. Check that no MovementRecord has Wing == target.
- N6: Never play a Remove card. Check that no CardPlayRecord has CardType == Remove.

**Evaluation approach:** Scan relevant history, return true if the prohibited action is completely absent.

**Style (Y1-Y6, Medium, 2pt):** Player must establish a visible pattern.
- Y1: Play a Remove card on >= 3 separate turns. Count CardPlayRecords where CardType == Remove.
- Y2: Play cards affecting >= 4 different collections across the round. Count distinct CollectionTypes in CardPlayHistory.
- Y3: Play a card affecting the same collection >= 3 turns in a row. Find max consecutive turns where the same collection appears in AffectedCollections.
- Y4: Alternate Acquire and Remove for >= 4 consecutive turns. Find max alternating streak in CardPlayHistory.
- Y5: Play the highest-value card in hand >= 4 turns. Requires access to all 3 drawn cards per turn (from CardDrawRecord). For each turn, check if selected card had the highest total absolute delta among the 3. Count qualifying turns.
- Y6: Visit an Event Spot >= 3 times during the round. Count MovementRecords where the space's SpotType == Event.

**Movement (M1-M6, Medium, 2pt):** Player must move in recognizable ways.
- M1: Visit all 4 wings at least once. Check distinct wings in MovementHistory (excluding Corridor).
- M2: Spend >= 4 turns in the same wing. Find max count of any single wing in MovementHistory.
- M3: End turn on same spot as another player >= 3 times. Cross-reference with other players' positions each turn (use RoundPlayHistory to find other players' positions per turn).
- M4: Take the longest available path at every fork for >= 4 consecutive turns. **Complex:** Would need to know what reachable spaces were available and whether the player chose the farthest. Consider simplifying to: "moved the maximum distance (full spin result) for >= 4 consecutive turns" or mark this as deferred.
- M5: Change wings every turn for >= 4 consecutive turns. Check consecutive MovementRecords for distinct Wings.
- M6: Return to the same spot >= 3 times. Find max count of any single SpaceId in MovementHistory.

**Rivalry (R1-R6, Hard, 3pt):** Player's actions relate to other players' actions.
- R1: Play Acquire on a collection immediately after another player plays Remove on that same collection, >= 3 times. Use RoundPlayHistory: for each of this player's turns, check if the previous turn (any other player) played Remove on a collection, and this player played Acquire on that same collection.
- R2: Play a card affecting the same collection as the player immediately before you, >= 4 turns. Use TurnOrder and RoundPlayHistory to find the previous player's affected collections, check if current player's affected collections overlap.
- R3: Never affect the same collection as the player immediately before you, for >= 5 consecutive turns. Inverse of R2 but requires a consecutive streak.
- R4: Play Remove on the highest-progress collection >= 3 times. For each Remove play, check if the targeted collection was the highest-progress at that moment.
- R5: Play Acquire on the lowest-progress collection >= 3 times. Similar to R4 but for lowest.
- R6: Be in the same wing as a specific randomly-assigned player >= 4 turns. At task draw time, a target player is randomly assigned. Check MovementHistory wing overlap per turn.

**Note on R4/R5:** These require knowing collection progress at the time of play, not end-of-round. Either: (a) evaluate based on the global RoundPlayHistory replaying progress forward, or (b) store a snapshot of collection progress with each CardPlayRecord. Option (b) is simpler.

**Note on R6:** The random target assignment happens at task draw time. Store the target player ID on the task assignment (either as part of SecretTask or as a field on HiddenAgendaPlayerState).

---

## Files to Create / Modify

### 1. Modify `Services/Logic/Games/FSM/HiddenAgendaGameContext.cs`

Add task evaluation methods:

```csharp
/// <summary>
/// Evaluates whether a specific task was completed by a player this round.
/// </summary>
public bool EvaluateTaskCompletion(string playerId, SecretTask task)
{
    return task.Id switch
    {
        "D1" => EvaluateDevotionCollection(playerId, CollectionType.RenaissanceMasters, 4),
        "D2" => EvaluateDevotionCollection(playerId, CollectionType.ContemporaryShowcase, 4),
        "D3" => EvaluateDevotionCollection(playerId, CollectionType.ImpressionistGallery, 4),
        "D4" => EvaluateDevotionCollection(playerId, CollectionType.MarbleAndBronze, 4),
        "D5" => EvaluateDevotionCollection(playerId, CollectionType.EmergingArtists, 4),
        "D6" => EvaluateDevotionWing(playerId, Wing.GrandHall, 5),
        "D7" => EvaluateDevotionWing(playerId, Wing.SculptureGarden, 5),
        "N1" => EvaluateNeglectCollection(playerId, CollectionType.RenaissanceMasters),
        // ... etc for all 31 tasks
        _ => false
    };
}
```

**Private evaluation helpers to implement:**

```csharp
// Devotion: count turns where player's card play added progress to specified collection
private bool EvaluateDevotionCollection(string playerId, CollectionType collection, int threshold)
{
    var player = GamePlayers[playerId];
    int count = player.CardPlayHistory
        .Where(r => r.CardType == CurationCardType.Acquire
                  && r.AffectedCollections.Contains(collection))
        .Select(r => r.TurnNumber)
        .Distinct()
        .Count();
    return count >= threshold;
}

// Devotion (wing): count turns affecting any collection in the specified wing
private bool EvaluateDevotionWing(string playerId, Wing wing, int threshold)
{
    var wingCollections = CollectionDefinitions.All
        .Where(c => c.PrimaryWing == wing)
        .Select(c => c.Type)
        .ToHashSet();
    var player = GamePlayers[playerId];
    int count = player.CardPlayHistory
        .Where(r => r.AffectedCollections.Any(c => wingCollections.Contains(c)))
        .Select(r => r.TurnNumber)
        .Distinct()
        .Count();
    return count >= threshold;
}

// Neglect: verify player never played Acquire on specified collection
private bool EvaluateNeglectCollection(string playerId, CollectionType collection)
{
    var player = GamePlayers[playerId];
    return !player.CardPlayHistory.Any(r =>
        r.CardType == CurationCardType.Acquire
        && r.AffectedCollections.Contains(collection));
}

// Neglect (wing): verify player never entered specified wing
private bool EvaluateNeglectWing(string playerId, Wing wing)
{
    var player = GamePlayers[playerId];
    return !player.MovementHistory.Any(m => m.Wing == wing);
}

// Neglect (card type): verify player never played specified card type
private bool EvaluateNeglectCardType(string playerId, CurationCardType type)
{
    var player = GamePlayers[playerId];
    return !player.CardPlayHistory.Any(r => r.CardType == type);
}

// Style Y1: Remove card on >= N turns
private bool EvaluateStyleRemoveCount(string playerId, int threshold)
{
    var player = GamePlayers[playerId];
    int count = player.CardPlayHistory
        .Where(r => r.CardType == CurationCardType.Remove)
        .Select(r => r.TurnNumber)
        .Distinct()
        .Count();
    return count >= threshold;
}

// Style Y2: cards affecting >= N different collections
private bool EvaluateStyleCollectionVariety(string playerId, int threshold)
{
    var player = GamePlayers[playerId];
    var distinct = player.CardPlayHistory
        .SelectMany(r => r.AffectedCollections)
        .Distinct()
        .Count();
    return distinct >= threshold;
}

// Style Y3: same collection >= N turns in a row
private bool EvaluateStyleConsecutiveCollection(string playerId, int threshold)
{
    var player = GamePlayers[playerId];
    var ordered = player.CardPlayHistory.OrderBy(r => r.TurnNumber).ToList();
    // Find max consecutive streak where AffectedCollections overlap
    // For each pair of consecutive records, check if they share any collection
    // Track the best streak
    ...
}

// Style Y4: alternate Acquire/Remove for >= N consecutive turns
private bool EvaluateStyleAlternating(string playerId, int threshold)
{
    var player = GamePlayers[playerId];
    var ordered = player.CardPlayHistory
        .Where(r => r.CardType != CurationCardType.Trade) // Only Acquire/Remove
        .OrderBy(r => r.TurnNumber)
        .ToList();
    // Find max alternating streak
    ...
}

// Movement M1: visit all 4 wings
private bool EvaluateMovementAllWings(string playerId)
{
    var player = GamePlayers[playerId];
    var visited = player.MovementHistory
        .Select(m => m.Wing)
        .Where(w => w != Wing.Corridor)
        .Distinct()
        .Count();
    return visited >= 4;
}

// Movement M2: >= N turns in same wing
private bool EvaluateMovementCamping(string playerId, int threshold)
{
    var player = GamePlayers[playerId];
    return player.MovementHistory
        .Where(m => m.Wing != Wing.Corridor)
        .GroupBy(m => m.Wing)
        .Any(g => g.Count() >= threshold);
}

// Movement M3: same spot as another player >= N times
private bool EvaluateMovementSameSpot(string playerId, int threshold)
{
    // Cross-reference with RoundPlayHistory to find other players' positions each turn
    var player = GamePlayers[playerId];
    int count = 0;
    foreach (var move in player.MovementHistory)
    {
        // Find other players' positions on this turn
        var otherPositions = State.RoundPlayHistory
            .Where(r => r.TurnNumber == move.TurnNumber && r.PlayerId != playerId)
            .Select(r => r.SpaceId);
        if (otherPositions.Contains(move.SpaceId))
            count++;
    }
    return count >= threshold;
}

// Movement M5: change wings every turn for >= N consecutive turns
private bool EvaluateMovementWingHopping(string playerId, int threshold)
{
    var player = GamePlayers[playerId];
    var ordered = player.MovementHistory.OrderBy(m => m.TurnNumber).ToList();
    // Find max consecutive streak where each wing differs from previous
    ...
}

// Rivalry R1: Acquire after another player's Remove on same collection, >= N times
private bool EvaluateRivalryRescue(string playerId, int threshold)
{
    var player = GamePlayers[playerId];
    int count = 0;
    foreach (var play in player.CardPlayHistory.Where(r => r.CardType == CurationCardType.Acquire))
    {
        // Find the previous turn in global history (any other player)
        var prevTurn = State.RoundPlayHistory
            .Where(r => r.TurnNumber == play.TurnNumber - 1 && r.PlayerId != playerId)
            .FirstOrDefault();
        if (prevTurn?.CardPlay is not null
            && prevTurn.CardPlay.CardType == CurationCardType.Remove
            && prevTurn.CardPlay.AffectedCollections.Intersect(play.AffectedCollections).Any())
        {
            count++;
        }
    }
    return count >= threshold;
}

// Rivalry R2: same collection as player immediately before you, >= N turns
private bool EvaluateRivalryEcho(string playerId, int threshold)
{
    // "Immediately before you" = previous player in turn order
    var turnOrder = State.TurnManager.TurnOrder;
    int myIndex = turnOrder.IndexOf(playerId);
    int prevIndex = (myIndex - 1 + turnOrder.Count) % turnOrder.Count;
    string prevPlayerId = turnOrder[prevIndex];

    var player = GamePlayers[playerId];
    int count = 0;
    foreach (var play in player.CardPlayHistory)
    {
        // Find the previous player's play on the same turn cycle
        // (their turn # would be play.TurnNumber - 1 within same round)
        var prevPlay = GamePlayers[prevPlayerId].CardPlayHistory
            .FirstOrDefault(r => r.TurnNumber == play.TurnNumber);
        if (prevPlay is not null
            && play.AffectedCollections.Intersect(prevPlay.AffectedCollections).Any())
        {
            count++;
        }
    }
    return count >= threshold;
}

// ... Additional evaluation functions for remaining tasks
```

### 2. Modify `Services/State/Games/Data/HiddenAgendaPlayerState.cs`

Add field for R6 rivalry task target:
```csharp
/// <summary>
/// For Rivalry task R6: the randomly assigned player to shadow.
/// Set at task draw time if R6 is in the player's tasks.
/// </summary>
public string? RivalryTargetPlayerId { get; set; }
```

### 3. Modify `CardPlayRecord` to include collection progress snapshot

For R4/R5 evaluation (highest/lowest progress at time of play):
```csharp
public record CardPlayRecord(
    int TurnNumber,
    CurationCard Card,
    int SelectedIndex,
    CollectionType[] AffectedCollections,
    CurationCardType CardType,
    Dictionary<CollectionType, int>? CollectionProgressSnapshot = null  // snapshot at time of play
);
```

Update Phase 3's `RecordCardPlay` to include the snapshot.

### 4. Update `DrawTasksForPlayer` in context

When drawing tasks, if R6 is drawn, assign a random target player:
```csharp
public void DrawTasksForPlayer(string playerId)
{
    // Draw 3 tasks...
    var drawn = TaskDefinitions.DrawTasks(Rng, State.CurrentTaskPool, 3);
    var player = GamePlayers[playerId];
    player.SecretTasks = drawn.ToList();

    // If R6 is drawn, assign random rival target
    if (drawn.Any(t => t.Id == "R6"))
    {
        var otherPlayers = GamePlayers.Keys.Where(id => id != playerId).ToList();
        player.RivalryTargetPlayerId = otherPlayers[Rng.GetRandomInt(0, otherPlayers.Count)];
    }
}
```

---

## Tests

### `Unit/Logic/Games/HiddenAgenda/TaskEvaluationTests.cs` (new)

Create a test helper that builds a `HiddenAgendaGameContext` with mock state containing constructed history data.

**Devotion tests:**
```
- D1: 4 turns adding to Renaissance Masters -> true
- D1: 3 turns adding to Renaissance Masters -> false
- D6: 5 turns affecting Grand Hall collections -> true
- D6: 4 turns affecting Grand Hall collections -> false
```

**Neglect tests:**
```
- N1: No Acquire on Renaissance Masters all round -> true
- N1: One Acquire on Renaissance Masters -> false
- N4: Never entered Grand Hall -> true
- N4: Entered Grand Hall once -> false
- N6: No Remove cards played -> true
- N6: One Remove card played -> false
```

**Style tests:**
```
- Y1: 3 Remove card turns -> true, 2 -> false
- Y2: 4 different collections affected -> true, 3 -> false
- Y3: Same collection 3 turns in a row -> true, 2 -> false
- Y4: Acquire-Remove alternating 4 consecutive turns -> true, 3 -> false
```

**Movement tests:**
```
- M1: All 4 wings visited -> true, only 3 -> false
- M2: 4 turns in same wing -> true, 3 -> false
- M3: Same spot as another player 3 times -> true, 2 -> false
- M5: Wing change every turn for 4 consecutive -> true, 3 -> false
- M6: Same spot 3 times -> true, 2 -> false
```

**Rivalry tests:**
```
- R1: Acquire after another's Remove on same collection, 3 times -> true, 2 -> false
- R2: Same collection as previous player, 4 turns -> true, 3 -> false
- R4: Remove on highest-progress collection 3 times -> true, 2 -> false
- R5: Acquire on lowest-progress collection 3 times -> true, 2 -> false
- R6: Same wing as target player 4 turns -> true, 3 -> false
```

**Edge cases:**
```
- Empty history -> all tasks return false (except Neglect tasks which return true)
- Single-turn history -> only tasks with threshold 1 can succeed
- Player with no card plays but movement -> Movement tasks can still succeed
```

---

## Verification

1. `dotnet build` compiles
2. All tests pass
3. Task evaluation returns correct results for all 5 categories with both passing and failing scenarios
4. Neglect tasks correctly return true for empty histories
5. Rivalry tasks correctly cross-reference global play history
