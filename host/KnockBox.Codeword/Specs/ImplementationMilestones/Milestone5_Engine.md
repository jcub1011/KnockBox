# Milestone 5: Engine

## Objective
Implement `CodewordGameEngine` as the singleton entry point that wires up state, context, FSM, and exposes public methods for the UI layer.

---

## Action Items

### 5.1 Engine Class Definition
- Singleton, extends `AbstractGameEngine`
- Primary constructor pattern with `IRandomNumberService`, `ILogger<CodewordGameEngine>`, `ILogger<CodewordGameState>`
- `MinPlayerCount` = 4, `MaxPlayerCount` = 8

### 5.2 Host Role
- Host is a **spectator only** -- manages lobby (start, kick, advance phases) but does not receive a role, give clues, or vote
- `Players` (from `AbstractGameState`) contains only participating players; host is excluded
- Host-only commands (`AdvanceToVoteCommand`, `StartNextGameCommand`, `ReturnToLobbyCommand`) use the host's user ID as `PlayerId`; FSM state handlers validate via `command.PlayerId == context.State.Host.Id`

### 5.3 `CreateStateAsync(User host, CancellationToken ct)`
- Validate host is not null
- Create `CodewordGameState(host, stateLogger)`
- Set `UpdateJoinableStatus(true)`
- Wire event: `gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState)`
- Return state

### 5.4 `StartAsync(User host, AbstractGameState state, CancellationToken ct)`
- Validate `state is CodewordGameState`
- Validate `host == gameState.Host`
- Player count validated by `CanStartAsync()` via inherited min/max
- Create `CodewordGameContext` and `FiniteStateMachine`
- Inside `gameState.Execute()`: set not joinable, assign context, transition to `SetupState`

### 5.5 Private Helpers
- `TryGetContext(state, out ctx, out err)` -- returns false with error if context is null
- `ProcessCommand(context, command)` -- wraps FSM command inside `state.Execute()`
- `Tick(context, now)` -- wraps FSM tick inside `state.Execute()`, always called regardless of `EnableTimers`

### 5.6 Public UI Methods
Most follow `TryGetContext` -> create command -> `ProcessCommand` pattern:
- `SubmitClue(User player, CodewordGameState state, string clue)`
- `CastVote(User player, CodewordGameState state, string targetPlayerId)`
- `InformantGuess(User player, CodewordGameState state, string guessedWord)`
- `AdvanceToVote(User player, CodewordGameState state)`
- `VoteToEndGame(User player, CodewordGameState state)`
- `StartNextGame(User player, CodewordGameState state)`

**Exceptions** (handle logic inline because they null out or recreate the context, making the FSM command pattern inapplicable):
- `ReturnToLobby(User host, CodewordGameState state)` -- host only, sets joinable, clears context
- `ResetGame(User host, CodewordGameState state)` -- host only, full reset with fresh context/FSM

### 5.7 `HandlePlayerLeft(User player, CodewordGameState state)`
- Remove from turn order
- Adjust `CurrentCluePlayerIndex` if needed
- Mark as eliminated
- If during VotePhase: void any votes cast for the disconnected player (reset `VoteTargetId`/`HasVoted` for voters who targeted this player)
- Check win conditions
- Auto-advance if leaving player was current clue giver or if all votes are now in

### 5.8 Semaphore Requirement
All state mutations (including `ReturnToLobby` and `ResetGame`) must be wrapped inside `context.State.Execute()` to acquire the semaphore and notify state change listeners.

---

## Acceptance Criteria
- [ ] `CodewordGameEngine` is a singleton extending `AbstractGameEngine`
- [ ] `CreateStateAsync` returns a valid state with `PlayerUnregistered` event wired
- [ ] `StartAsync` validates host, player count, state type, and transitions to `SetupState`
- [ ] All public UI methods follow the `TryGetContext` -> command -> `ProcessCommand` pattern
- [ ] All public methods return errors when context is null (game not started)
- [ ] `HandlePlayerLeft` correctly adjusts game state and checks win conditions
- [ ] All state mutations are wrapped in `state.Execute()` for thread safety
- [ ] `Tick` always delegates to FSM regardless of `EnableTimers`
- [ ] `ReturnToLobby` sets joinable and clears context
- [ ] `dotnet build` succeeds
