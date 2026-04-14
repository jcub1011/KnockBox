# Phase 2: FSM Infrastructure (Context, Commands, State Aliases)

## Goal

Set up the Finite State Machine plumbing: a context class, command hierarchy, type aliases, and engine wiring. After this phase, the engine can create a context, instantiate the FSM, and transition into an initial state -- but no individual game states are implemented yet (that's Phase 3+).

## Prerequisites

Phase 1 must be complete. The following Phase 1 types are used:
- `BoardGraph`, `BoardDefinitions`, `BoardSpace`, `Wing`, `SpotType`
- `CollectionType`, `CollectionDefinition`, `CollectionDefinitions`
- `CurationCard`, `CollectionEffect`, `CurationCardType`, `CurationCardDefinitions`
- `EventCard`, `EventCardType`
- `SecretTask`, `TaskCategory`, `TaskDifficulty`, `TaskDefinitions`
- `HiddenAgendaPlayerState`, `CardPlayRecord`, `MovementRecord`
- `HiddenAgendaGameConfig`, `TaskPoolRotation`
- `HiddenAgendaGameState` (expanded with GamePhase enum, all properties)

---

## Platform Patterns

### FSM Interfaces (`KnockBox.Core/Services/State/Games/Shared/IFiniteStateMachine.cs`)

```csharp
public interface IGameState<TContext, TCommand>
{
    ValueResult<IGameState<TContext, TCommand>?> OnEnter(TContext context);
    Result OnExit(TContext context);
    ValueResult<IGameState<TContext, TCommand>?> HandleCommand(TContext context, TCommand command);
}

public interface ITimedGameState<TContext, TCommand> : IGameState<TContext, TCommand>
{
    ValueResult<TimeSpan> GetRemainingTime(TContext context, DateTimeOffset now);
    ValueResult<IGameState<TContext, TCommand>?> Tick(TContext context, DateTimeOffset now);
}

public interface IFiniteStateMachine<TContext, TCommand>
{
    IGameState<TContext, TCommand>? CurrentState { get; }
    Result TransitionTo(TContext context, IGameState<TContext, TCommand> state);
    ValueResult<IGameState<TContext, TCommand>?> HandleCommand(TContext context, TCommand command);
    ValueResult<IGameState<TContext, TCommand>?> Tick(TContext context, DateTimeOffset now);
}
```

Concrete FSM: `FiniteStateMachine<TContext, TCommand>` from `KnockBox.Core/Services/State/Games/Shared/FiniteStateMachine.cs`.

### Reference Patterns from Codeword

**Command hierarchy** (`KnockBox.Codeword/Services/Logic/Games/FSM/CodewordCommand.cs`):
```csharp
public abstract record CodewordCommand(string PlayerId);
public record SubmitClueCommand(string PlayerId, string Clue) : CodewordCommand(PlayerId);
public record CastVoteCommand(string PlayerId, string TargetPlayerId) : CodewordCommand(PlayerId);
```

**Context class** (`KnockBox.Codeword/Services/Logic/Games/FSM/CodewordGameContext.cs`):
```csharp
public class CodewordGameContext(CodewordGameState state, IRandomNumberService rng, ILogger logger)
{
    public CodewordGameState State { get; } = state;
    public IRandomNumberService Rng { get; } = rng;
    public ILogger Logger { get; } = logger;
    public IFiniteStateMachine<CodewordGameContext, CodewordCommand> Fsm { get; set; } = null!;
    public ConcurrentDictionary<string, CodewordPlayerState> GamePlayers => State.GamePlayers;
    // Game logic helpers...
}
```

**Engine pattern** (`KnockBox.Codeword/Services/Logic/Games/CodewordGameEngine.cs`):
```csharp
public class CodewordGameEngine(...) : AbstractGameEngine(minPlayerCount: 4, maxPlayerCount: 8)
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct)
    {
        var gameState = new CodewordGameState(host, stateLogger);
        gameState.UpdateJoinableStatus(true);
        gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState);
        return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
    }

    public override Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct)
    {
        if (state is not CodewordGameState gameState) return error;
        if (host != gameState.Host) return error;

        var context = new CodewordGameContext(gameState, randomNumberService, logger);
        var fsm = new FiniteStateMachine<CodewordGameContext, CodewordCommand>(logger);
        context.Fsm = fsm;

        var executeResult = gameState.Execute(() =>
        {
            gameState.UpdateJoinableStatus(false);
            gameState.Context = context;
            foreach (var user in gameState.Players)
            {
                gameState.GamePlayers[user.Id] = new CodewordPlayerState { PlayerId = user.Id, DisplayName = user.Name };
                gameState.TurnManager.TurnOrder.Add(user.Id);
            }
            fsm.TransitionTo(context, new SetupState());
        });
        ...
    }

    internal Result ProcessCommand(CodewordGameContext context, CodewordCommand command)
    {
        var executeResult = context.State.Execute(() =>
        {
            var fsmResult = context.Fsm.HandleCommand(context, command);
            if (fsmResult.TryGetFailure(out var err))
                return Result.FromError(err.PublicMessage, err.InternalMessage);
            return Result.Success;
        });
        if (!executeResult.IsSuccess) return executeResult.Error.Error;
        return executeResult.Value;
    }

    public Result Tick(CodewordGameContext context, DateTimeOffset now)
    {
        // Same pattern as ProcessCommand but calls Fsm.Tick
    }

    // UI-facing methods delegate to ProcessCommand:
    public Result SubmitClue(User player, CodewordGameState state, string clue)
    {
        if (!TryGetContext(state, out var ctx, out var err)) return err;
        return ProcessCommand(ctx, new SubmitClueCommand(player.Id, clue));
    }
}
```

`IRandomNumberService` is from `KnockBox.Core.Services.Logic.RandomGeneration`.
`TurnManager` is from `KnockBox.Core.Services.State.Games.Shared.Components`.

---

## Files to Create

### 1. `Services/Logic/Games/FSM/IHiddenAgendaGameState.cs`

Type aliases that simplify FSM state signatures.

```csharp
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM
{
    public interface IHiddenAgendaGameState
        : IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>;

    public interface ITimedHiddenAgendaGameState
        : ITimedGameState<HiddenAgendaGameContext, HiddenAgendaCommand>;
}
```

### 2. `Services/Logic/Games/FSM/HiddenAgendaCommand.cs`

All player commands. Every command carries `PlayerId` for permission validation in FSM states.

**Commands organized by phase:**

```
Event Card Phase:
  PlayCatalogCommand(PlayerId, TargetPlayerId)      -- Use Catalog on another player
  PlayDetourCommand(PlayerId, TargetPlayerId)        -- Use Detour (applied after spin)
  SkipEventCardCommand(PlayerId)                     -- Skip playing event card

Spin Phase:
  SpinCommand(PlayerId)                              -- Spin the spinner

Move Phase:
  SelectDestinationCommand(PlayerId, DestinationSpaceId: int)

Draw Phase:
  SelectCurationCardCommand(PlayerId, CardIndex: int)           -- Pick 1 of 3 drawn cards
  SelectTradeOptionCommand(PlayerId, UseAlternate: bool)        -- For Trade cards: pick option A or B
  SelectEventCardActionCommand(PlayerId, KeepNewCard: bool)     -- At Event Spot: keep new or existing

Guess Phase:
  SubmitGuessCommand(PlayerId, Guesses: Dictionary<string, List<string>>)   -- opponentId -> 3 task IDs
  SkipGuessCommand(PlayerId)

Final Guess Phase:
  SubmitFinalGuessCommand(PlayerId, Guesses: Dictionary<string, List<string>>)
  SkipFinalGuessCommand(PlayerId)

Round Over:
  StartNextRoundCommand(PlayerId)                    -- Host only

Match Over:
  ReturnToLobbyCommand(PlayerId)                     -- Host only
  PlayAgainCommand(PlayerId)                         -- Host only
```

### 3. `Services/Logic/Games/FSM/HiddenAgendaGameContext.cs`

Per-game context created when the game starts, stored on `HiddenAgendaGameState.Context`.

**Constructor:** `(HiddenAgendaGameState state, IRandomNumberService rng, ILogger logger)`

**Core properties:**
- `State` (HiddenAgendaGameState)
- `Rng` (IRandomNumberService)
- `Logger` (ILogger)
- `Fsm` (IFiniteStateMachine<HiddenAgendaGameContext, HiddenAgendaCommand>) -- set after construction

**Convenience accessors:**
- `GamePlayers` -> `State.GamePlayers`
- `Board` -> `State.BoardGraph`
- `CollectionProgress` -> `State.CollectionProgress`

**Helper methods to implement now:**

```csharp
// Spinner (3-12 inclusive, uniform)
int SpinSpinner()

// Apply card effects to collections. Delta can be positive (Acquire) or negative (Remove).
// Clamp at 0 (never negative).
void ApplyCollectionEffects(IReadOnlyList<CollectionEffect> effects)

// Draw 3 tasks from current pool and assign to player. Avoid duplicating already-assigned tasks.
void DrawTasksForPlayer(string playerId)

// Count collections at or above their target value.
int GetCompletedCollectionCount()

// Max turns per player based on player count: 3->12, 4->11, 5->10, 6->9
int GetMaxTurnsPerPlayer()

// Check all round-end conditions. Returns trigger type or None.
// Conditions: (1) 3 of 5 collections completed, (2) guess countdown expired, (3) max turns
RoundEndTrigger CheckRoundEndConditions()

// Reset for new round: collections to 0, clear histories, clear player round state (keep cumulative scores),
// rotate task pool per config.
// Task pool rotation:
//   Full: re-call GetPoolForPlayerCount (shuffles and selects fresh subset)
//   Partial: shuffle all 31 tasks, take first N (where N = pool size for player count).
//            This naturally rotates some tasks in/out each round while keeping most.
//   Fixed: reuse the same pool from round 1.
void ResetForNewRound()
```

**Shared turn-advancement helper:**
```csharp
/// Advances to the next player's turn or triggers round end.
/// Used by EventCardPhaseState (Catalog ends turn), DrawPhaseState (FinishTurn),
/// and GuessPhaseState (after guess/skip). Centralizes turn-advancement logic
/// to avoid duplication across FSM states.
public IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>? AdvanceToNextPlayerOrEndRound()
{
    var trigger = CheckRoundEndConditions();
    if (trigger != RoundEndTrigger.None)
        return new FinalGuessState();
    State.TurnManager.NextTurn();
    return new EventCardPhaseState();
}
```

**Enum:**
```csharp
public enum RoundEndTrigger { None, CollectionTrigger, GuessCountdown, MaxTurns }
```

### 4. Placeholder: `Services/Logic/Games/FSM/States/RoundSetupState.cs`

Phase 3 will implement this properly. For now, create a stub so the engine compiles:

```csharp
public sealed class RoundSetupState : ITimedHiddenAgendaGameState
{
    public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(
        HiddenAgendaGameContext context) => null;
    public Result OnExit(HiddenAgendaGameContext context) => Result.Success;
    public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(
        HiddenAgendaGameContext context, HiddenAgendaCommand command) => null;
    public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(
        HiddenAgendaGameContext context, DateTimeOffset now) => null;
    public ValueResult<TimeSpan> GetRemainingTime(
        HiddenAgendaGameContext context, DateTimeOffset now) => TimeSpan.Zero;
}
```

---

## Files to Modify

### `Services/Logic/Games/HiddenAgendaGameEngine.cs`

Replace the current stub entirely with the full engine. Key changes:
- Set `base(minPlayerCount: 3, maxPlayerCount: 6)`
- Constructor takes `IRandomNumberService`, `ILogger<HiddenAgendaGameEngine>`, `ILogger<HiddenAgendaGameState>`
- `CreateStateAsync`: same pattern as current but no changes needed
- `StartAsync`: create context + FSM, snapshot players, initialize board + collections, transition to `RoundSetupState`
- Add `ProcessCommand(context, command)` -- wraps `context.Fsm.HandleCommand` inside `context.State.Execute`
- Add `Tick(context, now)` -- wraps `context.Fsm.Tick` inside `context.State.Execute`
- Add `TryGetContext` private helper
- Add all public UI-facing methods that delegate to `ProcessCommand`:
  - `Spin`, `SelectDestination`, `SelectCurationCard`, `SelectTradeOption`
  - `PlayCatalog`, `PlayDetour`, `SkipEventCard`, `SelectEventCardAction`
  - `SubmitGuess`, `SkipGuess`, `SubmitFinalGuess`, `SkipFinalGuess`
  - `StartNextRound`, `ReturnToLobby`, `PlayAgain`
- `HandlePlayerLeft`: remove from turn order, advance if current player

---

## Tests

### `Unit/Logic/Games/HiddenAgenda/HiddenAgendaGameContextTests.cs` (new)

```
SpinSpinner:
- Returns values in range [3, 12] (run many times)
- Produces varied results (not always same value)

ApplyCollectionEffects:
- +2 to collection at 0 -> collection is 2
- -3 to collection at 1 -> clamps to 0
- Multiple effects in one call update all listed collections
- Initializes missing collections to 0 before applying

GetCompletedCollectionCount:
- All at 0 -> returns 0
- One collection at target -> returns 1
- Three at target -> returns 3 (trigger threshold)
- Collection at target-1 does not count

GetMaxTurnsPerPlayer:
- 3 players -> 12
- 4 players -> 11
- 5 players -> 10
- 6 players -> 9

CheckRoundEndConditions:
- No conditions met -> None
- 3 collections completed -> CollectionTrigger
- Countdown active + all expired -> GuessCountdown
- All players at max turns -> MaxTurns
```

### `Unit/Logic/Games/HiddenAgenda/HiddenAgendaGameEngineTests.cs` (update existing)

```
- CreateStateAsync returns valid state with joinable=true
- CreateStateAsync with null host returns error
- StartAsync with non-host returns error
- StartAsync sets joinable=false
- StartAsync creates context and FSM on state
- StartAsync snapshots all players into GamePlayers with correct IDs and names
- StartAsync adds all players to TurnOrder
- StartAsync initializes BoardGraph (not null)
- StartAsync initializes all 5 collections to 0
- StartAsync transitions to RoundSetupState
- Min player count is 3, max is 6
```

---

## Verification

- `dotnet build KnockBox.HiddenAgenda/KnockBox.HiddenAgenda.csproj` compiles
- `dotnet test KnockBox.HiddenAgendaTests/KnockBox.HiddenAgendaTests.csproj` passes
- Engine creates state -> starts game -> creates context/FSM -> transitions to RoundSetupState (placeholder)
- Context helper methods work correctly in isolation
- All command types constructible with correct parameters
