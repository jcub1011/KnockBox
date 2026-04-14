# Phase 3: Turn Mechanics (Spin, Move, Draw)

## Goal

Implement the core per-turn FSM states. After this phase, players can take complete turns: optionally play an event card, spin the spinner, move on the board, and draw/play curation cards. Collections update, positions update, and play history is recorded. Turn order advances correctly.

## Prerequisites

Phase 1 (data models) and Phase 2 (FSM infrastructure) must be complete. Key types used:
- `BoardGraph`, `BoardDefinitions` -- board traversal
- `CurationCardDefinitions` -- card pools and DrawThree
- `EventCardDefinitions`, `EventCard`, `EventCardType`
- `CollectionDefinitions`, `CollectionType`
- `HiddenAgendaGameContext` -- SpinSpinner, ApplyCollectionEffects, CheckRoundEndConditions, GetMaxTurnsPerPlayer
- `HiddenAgendaCommand` hierarchy -- SpinCommand, SelectDestinationCommand, SelectCurationCardCommand, etc.
- `HiddenAgendaGameState` -- all state properties including ReachableSpaces, DrawnCards, CurrentTaskPool, etc.
- `HiddenAgendaPlayerState` -- position, spin, movement/card history
- `ITimedHiddenAgendaGameState` -- type alias for timed FSM states
- `RoundSetupState` placeholder -- to be implemented in this phase
- `TaskDefinitions` -- GetPoolForPlayerCount, DrawTasks

---

## Platform Patterns

### Reference: Timed FSM state (SetupState from Codeword)

```csharp
public sealed class SetupState : ITimedCodewordGameState
{
    private DateTimeOffset _expiresAt;

    public ValueResult<IGameState<...>?> OnEnter(CodewordGameContext context)
    {
        // Initialize state
        context.State.SetPhase(CodewordGamePhase.Setup);
        _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.SetupPhaseTimeoutMs);
        return null; // Stay in this state
    }

    public Result OnExit(CodewordGameContext context) => Result.Success;
    public ValueResult<IGameState<...>?> HandleCommand(...) => null; // No commands
    public ValueResult<IGameState<...>?> Tick(CodewordGameContext context, DateTimeOffset now)
    {
        if (now < _expiresAt) return null;
        return new CluePhaseState(); // Auto-advance on timeout
    }
    public ValueResult<TimeSpan> GetRemainingTime(...) => _expiresAt - now;
}
```

### Key FSM state patterns:
- Return `null` to stay in current state
- Return `new SomeState()` to trigger transition (FSM calls OnExit -> OnEnter chain)
- Return `new ResultError("message")` for validation failures
- Call `context.State.SetPhase(...)` in `OnEnter` to update the UI-visible phase
- Check `cmd.PlayerId != context.State.TurnManager.CurrentPlayer` for current-player-only commands
- On timeout, auto-perform a default action (e.g., auto-spin, auto-select first card)
- Chain-transition from `OnEnter` by returning a new state (e.g., skip EventCardPhase if no event card held)

---

## FSM State Flow

```
RoundSetupState --timeout--> EventCardPhaseState
    |                              |
    |  (if no event card)          |  PlayCatalog -> next player turn (Catalog ends turn)
    |  chain-transition            |  PlayDetour -> SpinPhaseState (Detour applied after spin)
    |  SkipEventCard ---->         |
    v                              v
SpinPhaseState                     
    | SpinCommand / timeout
    v
MovePhaseState
    | SelectDestination / timeout
    v
DrawPhaseState
    | SelectCurationCard / SelectEventCardAction / timeout
    v
[FinishTurn logic]
    | check round-end conditions
    |-- if triggered --> FinalGuessState (Phase 6 placeholder)
    |-- if player hasn't guessed --> GuessPhaseState (Phase 5 placeholder)
    |-- otherwise --> next player's EventCardPhaseState
```

---

## Files to Create / Modify

### 1. Implement `FSM/States/RoundSetupState.cs` (replace Phase 2 placeholder)

Timed state at round start. Initializes the round.

**OnEnter:**
- Increment `context.State.CurrentRound`
- Generate task pool: `context.State.CurrentTaskPool = TaskDefinitions.GetPoolForPlayerCount(playerCount)`
- Draw 3 tasks per player via `context.DrawTasksForPlayer(playerId)` for each player
- Randomize turn order on first round (Fisher-Yates shuffle on TurnManager.TurnOrder using context.Rng)
- Initialize collection progress to 0 for all 5 collections (if round > 1, should already be done by ResetForNewRound)
- Place all players at a starting position (e.g., space 0)
- Set phase to `GamePhase.RoundSetup`
- Set expiry timer from config

**Tick:** After timeout, return `new EventCardPhaseState()` to start first player's turn.

**HandleCommand:** Return null (no commands during setup).

### 2. Create `FSM/States/EventCardPhaseState.cs`

First step of each player's turn. If the current player holds an event card, they can play it or skip.

**OnEnter:**
- Get current player from TurnManager
- If player has no event card (`HeldEventCard is null`), chain-transition immediately: `return new SpinPhaseState()`
- Otherwise, set phase to `GamePhase.EventCardPhase`, start timer

**HandleCommand:**
- `PlayCatalogCommand`: validate current player, validate holds Catalog, validate target is a different active player. Resolve: store target's `CardDrawHistory` (last entry's DrawnCards) on state so the UI can show it. Consume the card. **Catalog ends the turn** (per GDD) -- advance to next player.
- `PlayDetourCommand`: validate current player, validate holds Detour, validate target exists. Mark `player.DetourPending = true`. Consume card. Transition to `SpinPhaseState`.
- `SkipEventCardCommand`: validate current player. Transition to `SpinPhaseState`.

**Tick:** Auto-skip on timeout (transition to SpinPhaseState).

**AdvanceToNextPlayerTurn helper:** Increment turn counter, check round-end conditions. If triggered -> FinalGuessState. Otherwise advance TurnManager and return new EventCardPhaseState.

### 3. Create `FSM/States/SpinPhaseState.cs`

Player spins the spinner.

**OnEnter:** Set phase to `GamePhase.SpinPhase`, start timer.

**HandleCommand:**
- `SpinCommand`: validate current player. If `DetourPending`, use target player's `LastSpinResult` instead of spinning. Clear DetourPending. Store spin result on player and on state (`State.CurrentSpinResult`). Transition to `MovePhaseState`.

**Tick:** Auto-spin on timeout using `context.SpinSpinner()`. Clear Detour if pending. Transition to MovePhaseState.

### 4. Create `FSM/States/MovePhaseState.cs`

Player picks a destination from reachable spaces.

**OnEnter:** Calculate reachable spaces from current position with spin result distance via `context.Board.GetReachableSpaces(player.CurrentSpaceId, player.LastSpinResult)`. Store on `State.ReachableSpaces` for UI. Set phase to `GamePhase.MovePhase`, start timer.

**HandleCommand:**
- `SelectDestinationCommand`: validate current player. Validate destination is in `State.ReachableSpaces`. Update `player.CurrentSpaceId`. Record `MovementRecord` in player's MovementHistory. Clear reachable spaces and spin result from state. Transition to `DrawPhaseState`.

**Tick:** Auto-select a random reachable space on timeout. Same logic as HandleCommand.

### 5. Create `FSM/States/DrawPhaseState.cs`

Most complex turn state. Handles two spot types:

**Curation Spot:** Draw 3 cards from wing pool, player picks 1. Apply effects. Record play.
**Event Spot:** Draw random event card. If player already holds one, choose keep/swap. If not, auto-take.

**OnEnter:** Determine spot type from `context.Board.Spaces[player.CurrentSpaceId].SpotType`.
- If Curation: `CurationCardDefinitions.DrawThree(rng, wing)`, store on `State.DrawnCards`, also store as `CardDrawRecord` on player's `CardDrawHistory` (for Catalog). Set phase, start timer.
- If Event: draw random event card (50/50 Catalog or Detour). If player holds no card, auto-assign and call `FinishTurn`. If player holds one, store drawn card on state, set phase, start timer (player must choose).

**HandleCommand:**
- `SelectCurationCardCommand`: validate current player, validate index 0-2, validate DrawnCards exists. Get selected card. If Trade card and alternate effects exist, default to Effects (option A). Apply effects via `context.ApplyCollectionEffects`. Record `CardPlayRecord` on player and `TurnRecord` on `State.RoundPlayHistory`. Clear DrawnCards. Call `FinishTurn`.
- `SelectTradeOptionCommand`: validate current player. If the selected card was Trade, apply AlternateEffects instead. (Implementation: either store pending card index and resolve on this command, or combine with SelectCurationCard.)
- `SelectEventCardActionCommand`: validate current player, validate at Event Spot. If KeepNewCard, swap; else keep existing. Clear drawn event card from state. Call `FinishTurn`.

**Tick:** Auto-select first card (index 0) on timeout. Or auto-keep new event card.

**FinishTurn method (private):**
1. Increment `player.TurnsTakenThisRound` and `State.TotalTurnsTaken`
2. If guess countdown active and player hasn't guessed, decrement `player.GuessCountdownTurnsRemaining`
3. Check `context.CheckRoundEndConditions()`:
   - If not None -> transition to `FinalGuessState` (Phase 6 placeholder)
4. If player hasn't submitted guesses -> transition to `GuessPhaseState` (Phase 5 placeholder)
5. Else -> advance TurnManager.NextTurn(), return `new EventCardPhaseState()`

### 6. Create placeholder `FSM/States/GuessPhaseState.cs`

Phase 5 will implement this. For now, immediately skip to next player:

```csharp
public sealed class GuessPhaseState : ITimedHiddenAgendaGameState
{
    public ValueResult<...?> OnEnter(HiddenAgendaGameContext context)
    {
        // Placeholder: skip guessing, advance to next player
        context.State.TurnManager.NextTurn();
        return new EventCardPhaseState();
    }
    public Result OnExit(...) => Result.Success;
    public ValueResult<...?> HandleCommand(...) => null;
    public ValueResult<...?> Tick(...) => null;
    public ValueResult<TimeSpan> GetRemainingTime(...) => TimeSpan.Zero;
}
```

### 7. Create placeholder `FSM/States/FinalGuessState.cs`

Phase 6 will implement this. For now, just stay:

```csharp
public sealed class FinalGuessState : ITimedHiddenAgendaGameState
{
    public ValueResult<...?> OnEnter(HiddenAgendaGameContext context) => null;
    public Result OnExit(...) => Result.Success;
    public ValueResult<...?> HandleCommand(...) => null;
    public ValueResult<...?> Tick(...) => null;
    public ValueResult<TimeSpan> GetRemainingTime(...) => TimeSpan.Zero;
}
```

### 8. Add helper to `HiddenAgendaGameContext.cs`

```csharp
/// <summary>
/// Records a card play in both the player's history and the global round history.
/// </summary>
public void RecordCardPlay(string playerId, CurationCard card, int selectedIndex,
    IReadOnlyList<CurationCard> allDrawn, IReadOnlyList<CollectionEffect> appliedEffects)
{
    var player = GamePlayers[playerId];
    var affected = appliedEffects.Select(e => e.Collection).Distinct().ToArray();
    var record = new CardPlayRecord(
        player.TurnsTakenThisRound + 1,
        card,
        selectedIndex,
        affected,
        card.Type);
    player.CardPlayHistory.Add(record);

    var space = Board.Spaces[player.CurrentSpaceId];
    State.RoundPlayHistory.Add(new TurnRecord(
        State.TotalTurnsTaken + 1,
        playerId,
        record,
        player.CurrentSpaceId,
        space.Wing));
}
```

### 9. Add to `HiddenAgendaGameState.cs` (if not already from Phase 1)

Properties needed by turn states:
```csharp
public int? CurrentSpinResult { get; set; }
public EventCard? PendingDrawnEventCard { get; set; }  // Event card pending swap decision
public List<CurationCard>? CatalogRevealedCards { get; set; }  // Catalog result for current player
```

---

## Tests

### `Unit/Logic/Games/HiddenAgenda/States/RoundSetupStateTests.cs`

```
- OnEnter increments CurrentRound
- OnEnter generates task pool for correct player count
- OnEnter draws 3 tasks per player
- OnEnter randomizes turn order on first round
- OnEnter sets all players' starting positions
- OnEnter sets phase to RoundSetup
- Tick before timeout returns null
- Tick after timeout returns EventCardPhaseState
- HandleCommand returns null for any command
```

### `Unit/Logic/Games/HiddenAgenda/States/SpinPhaseStateTests.cs`

```
- OnEnter sets phase to SpinPhase
- SpinCommand from current player stores spin result (3-12)
- SpinCommand transitions to MovePhaseState
- SpinCommand from wrong player returns error
- Detour pending uses target's LastSpinResult
- Detour clears pending flag after use
- Tick auto-spins and transitions to MovePhaseState
```

### `Unit/Logic/Games/HiddenAgenda/States/MovePhaseStateTests.cs`

```
- OnEnter calculates reachable spaces from board graph
- OnEnter stores reachable spaces on state
- OnEnter sets phase to MovePhase
- Valid destination updates player position
- Valid destination records in MovementHistory
- Invalid destination (not in reachable set) returns error
- SelectDestination from wrong player returns error
- After move, reachable spaces cleared from state
- Transitions to DrawPhaseState
- Tick auto-selects a valid reachable space
```

### `Unit/Logic/Games/HiddenAgenda/States/DrawPhaseStateTests.cs`

```
Curation Spot:
- OnEnter draws 3 cards from wing pool
- OnEnter stores drawn cards on state
- OnEnter records CardDrawRecord on player
- SelectCurationCard (valid index) applies effects to collections
- SelectCurationCard records CardPlayRecord and TurnRecord
- SelectCurationCard clears DrawnCards from state
- Invalid card index returns error
- Wrong player returns error
- After card play, turn counter incremented

Event Spot:
- OnEnter with no held card auto-takes and finishes turn
- OnEnter with held card stores pending drawn card
- SelectEventCardAction KeepNewCard=true swaps held card
- SelectEventCardAction KeepNewCard=false keeps existing

Turn advancement:
- Normal play advances to next player's EventCardPhaseState
- Collection trigger (3 of 5 complete) transitions to FinalGuessState
- Max turns reached transitions to FinalGuessState
- Tick auto-selects first card on timeout
```

---

## Verification

1. `dotnet build` compiles cleanly
2. All tests pass
3. Full turn cycle works: RoundSetup -> EventCard -> Spin -> Move -> Draw -> next player
4. Collections update correctly when curation cards are played
5. Player positions update on move, recorded in MovementHistory
6. Turn order advances correctly through all players
7. Card play history recorded (CardPlayHistory, CardDrawHistory, RoundPlayHistory)
8. Spin results in valid range (3-12)
9. Board reachability works (only valid destinations accepted)
10. Event card play (Catalog, Detour) works correctly
