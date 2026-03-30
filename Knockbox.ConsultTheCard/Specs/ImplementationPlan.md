# Consult The Card - Implementation Plan

## Context

The `KnockBox.ConsultTheCard` project exists as a stub (empty `Class1.cs`). The game design document at `Specs/GameDesignDocument.md` fully specifies a social deduction party game for 4-8 players. This plan implements the full game: backend logic, Blazor UI, unit tests, and integration wiring — all following the CardCounter architecture pattern. Power Cards are excluded per the design doc.

---

## File Structure

```
KnockBox.ConsultTheCard/
  Services/
    Logic/Games/ConsultTheCard/
      ConsultTheCardGameEngine.cs
      Data/
        WordBank.cs                          # Loads word pairs from CSV
        WordPairs.csv                        # CSV file with variable-length word groups (2+ words per row, 2 selected at runtime)
      FSM/
        ConsultTheCardCommand.cs
        ConsultTheCardGameContext.cs
        IConsultTheCardGameState.cs
        States/
          SetupState.cs
          CluePhaseState.cs
          DiscussionPhaseState.cs
          VotePhaseState.cs
          RevealPhaseState.cs
          GameOverState.cs
    State/Games/ConsultTheCard/
      ConsultTheCardGameState.cs
      Data/
        ConsultTheCardPlayerState.cs

KnockBox/Components/Pages/Games/ConsultTheCard/
  ConsultTheCardLobby.razor              # Main page, routing, tick, phase switching
  ConsultTheCardLobby.razor.cs
  LobbyPhase.razor                       # Player list, host settings, start button
  LobbyPhase.razor.cs
  SetupPhase.razor                       # Shows player their secret word (or blank)
  SetupPhase.razor.cs
  CluePhase.razor                        # Turn-based clue submission + clue history
  CluePhase.razor.cs
  DiscussionPhase.razor                  # Clue display, guess button, advance button
  DiscussionPhase.razor.cs
  VotePhase.razor                        # Player selection grid for simultaneous voting
  VotePhase.razor.cs
  RevealPhase.razor                      # Eliminated player's role reveal + round summary
  RevealPhase.razor.cs
  GameOverPhase.razor                    # Final scores, winner, replay options
  GameOverPhase.razor.cs

KnockBox.ConsultTheCardTests/
  KnockBox.ConsultTheCardTests.csproj
  Unit/Logic/Games/ConsultTheCard/
    ConsultTheCardGameContextTests.cs
    ConsultTheCardGameEngineTests.cs
    ConsultTheCardGameEnginePlayerLeftTests.cs
    SetupStateTests.cs
    CluePhaseStateTests.cs
    DiscussionPhaseStateTests.cs
    VotePhaseStateTests.cs
    RevealPhaseStateTests.cs
    GameOverStateTests.cs
```

---

## Phase 0: Codebase Fixes

1. **Rename project**: Rename `Knockbox.ConsultTheCard` folder and project to `KnockBox.ConsultTheCard` to match the naming convention (`KnockBox.CardCounter`, `KnockBox.DiceSimulator`, etc.). Update `KnockBox.slnx` reference accordingly.
2. **Fix `IFininteStateMachine` typo**: Rename `IFininteStateMachine` to `IFiniteStateMachine` across the codebase:
   - `KnockBox.Core/Services/State/Games/Shared/IFiniteStateMachine.cs` (interface name)
   - `KnockBox.Core/Services/State/Games/Shared/FiniteStateMachine.cs` (implementation references)
   - `KnockBox.CardCounter/Services/Logic/Games/CardCounter/FSM/CardCounterGameContext.cs` (property type)
   - `KnockBox.DrawnToDress/Services/Logic/Games/DrawnToDress/DrawnToDressGameContext.cs` (property type)

---

## Phase 1: Project Scaffolding

1. Delete `KnockBox.ConsultTheCard/Class1.cs`
2. Update `KnockBox.ConsultTheCard.csproj`:
   - Add `<ProjectReference>` to `KnockBox.Core`
   - Add `<Using Include="Microsoft.Extensions.Logging" />`
   - Add `InternalsVisibleTo` for `KnockBox.ConsultTheCardTests`
3. Create `KnockBox.ConsultTheCardTests/` project:
   - .NET 10.0, MSTest (`Microsoft.VisualStudio.TestTools.UnitTesting`), Moq
   - References: `KnockBox.ConsultTheCard`, `KnockBox.Core`
   - Method-level parallelization via `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`
   - Add to solution file

---

## Phase 2: Data Models

### Enums (in `ConsultTheCardGameState.cs`)
- `ConsultTheCardGamePhase`: `Setup`, `CluePhase`, `Discussion`, `Voting`, `Reveal`, `GameOver`
- `Role`: `Agent`, `Insider`, `Informant`

### Records (in `ConsultTheCardGameState.cs`)
- `WordGroup(string[] Words)` — a thematic group of 2+ words; at runtime, 2 are selected and randomly assigned as Agent/Insider
- `ClueEntry(string PlayerId, string PlayerName, string Clue)`
- `VoteEntry(string VoterId, string VoterName, string TargetId, string TargetName)`
- `EliminationResult(string PlayerId, string PlayerName, Role Role, bool WasTie)`
- `InformantGuessResult(string PlayerId, string PlayerName, string GuessedWord, bool WasCorrect)`
- `WinConditionResult(bool GameOver, Role? WinningTeam, string Reason)`
- `EndGameVoteStatus(HashSet<string> VotedToEnd, int RequiredVotes)` — tracks Agent end-game votes

### ConsultTheCardPlayerState (separate file)
- `PlayerId`, `DisplayName`, `Role`, `SecretWord` (null for Informant)
- `IsEliminated`, `HasSubmittedClue`, `CurrentClue`, `VoteTargetId`, `HasVoted`
- `HasVotedToEndGame` — tracks whether this player has voted to end the game
- `Score`, `PreviousClues` (list, prevents reuse across rounds)

### ConsultTheCardGameConfig
- `SetupPhaseTimeoutMs` (5000), `CluePhaseTimeoutMs` (30000), `DiscussionPhaseTimeoutMs` (120000), `VotePhaseTimeoutMs` (15000), `RevealPhaseTimeoutMs` (10000)
- `EnableTimers`, `TotalGames` (for multi-game scoring, default 5)

### ConsultTheCardGameState (extends AbstractGameState)
- `Context`, `GamePhase`, `GamePlayers` (ConcurrentDictionary), `TurnOrder`
- `CurrentCluePlayerIndex`, `CurrentEliminationCycle`, `CurrentGameNumber`, `CurrentWordPair`
- `CurrentRoundClues`, `CurrentRoundVotes`
- `LastElimination`, `LastInformantGuess`, `WinResult`, `Config`
- `EndGameVoteStatus` — tracking for the "Agents vote to end" mechanic
- `GameScores` (Dictionary<string, int>) — cumulative scores across all games in a multi-game session

---

## Phase 3: FSM Infrastructure

### Commands (`ConsultTheCardCommand.cs`)
```
ConsultTheCardCommand(string PlayerId)               # abstract base
├── SubmitClueCommand(PlayerId, string Clue)          # CluePhase
├── InformantGuessCommand(PlayerId, string GuessedWord) # Discussion
├── AdvanceToVoteCommand(PlayerId)                    # Discussion (host only)
├── VoteToEndGameCommand(PlayerId)                    # Discussion (any player — Insider win if majority of alive players vote)
├── CastVoteCommand(PlayerId, string TargetPlayerId)  # Voting
├── StartNextGameCommand(PlayerId)                    # GameOver (host only, advances to next game in series)
└── ReturnToLobbyCommand(PlayerId)                    # GameOver (host only)
```

### Type Aliases (`IConsultTheCardGameState.cs`)
- `IConsultTheCardGameState : IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>`
- `ITimedConsultTheCardGameState : ITimedGameState<...>, IConsultTheCardGameState`

### Context (`ConsultTheCardGameContext.cs`)
References: `State`, `Rng`, `Logger`, `Fsm` (`IFiniteStateMachine<...>`), `UsedWordPairIndices` (HashSet)

Key helpers:
- **`AssignRoles()`** — Uses scaling table: 4p=3A/1I, 5p=3A/1I/1Inf, 6p=4A/1I/1Inf, 7p=4A/2I/1Inf, 8p=5A/2I/1Inf. Shuffles players randomly, assigns roles and words.
- **`SelectWordPair()`** — Picks a random unused `WordGroup` from `WordBank`, selects 2 words at random from the group, then randomly assigns which becomes the Agent word and which becomes the Insider word. A group with N words produces N*(N-1) possible configurations, dramatically increasing variety and discouraging memorization.
- **`GetAlivePlayers()`** / **`GetAlivePlayerCount()`**
- **`TallyVotes()`** — Returns player ID with most votes, or null on tie
- **`CheckWinConditions()`** — Agents win if no Insiders/Informant alive; Insiders win if <=2 alive OR if majority of alive players voted to end the game; otherwise continue
- **`ResetEliminationCycleState()`** — Clears per-cycle clue/vote/end-game-vote data for all alive players
- **`ApplyScoring()`** — Per design doc: +2 survived, +1 winning team, +3 Informant correct guess, -1 voted for Agent. Must be called **before** `ResetEliminationCycleState()` so vote data is still available.

---

## Phase 4: FSM States

### SetupState (timed, 5s timeout)
- `OnEnter`: Increment `CurrentEliminationCycle`, call `AssignRoles()` + `SelectWordPair()`, randomize clue order, set phase to `Setup`
- `Tick`: Auto-advance to `CluePhaseState` after `SetupPhaseTimeoutMs` (5s)
- `GetRemainingTime`: Returns countdown based on expiration set in `OnEnter`

### CluePhaseState (timed)
- Tracks whose turn it is to give a clue (skips eliminated players)
- `HandleCommand(SubmitClueCommand)`: Validate single word (no spaces), not the player's secret word, not previously used by this player in any prior cycle. Store clue, advance to next player. When all alive players have submitted, transition to `DiscussionPhaseState`.
- `Tick`: If `EnableTimers`, auto-submit "..." for timed-out player and advance. If timers disabled, no auto-action (players submit manually with no time pressure).
- **Known limitation:** The GDD prohibits "direct synonyms of the secret word." Synonym detection is not feasible to implement programmatically, so this rule is enforced socially (players call out violations). This is consistent with the in-person party game design.

### DiscussionPhaseState (timed)
- `HandleCommand(InformantGuessCommand)`: Any player can attempt. If guesser is Informant and correct: Informant wins, → `GameOverState`. If Informant and wrong: eliminate them, apply scoring, check win conditions, → `RevealPhaseState` or `GameOverState`. If not Informant: reject silently (no-op, preserves secrecy).
- `HandleCommand(VoteToEndGameCommand)`: Any alive player can vote to end the game. Track in `EndGameVoteStatus`. If majority of alive players have voted to end, Insiders win → `GameOverState`.
- `HandleCommand(AdvanceToVoteCommand)`: Host only. Transitions to `VotePhaseState`.
- `Tick`: If `EnableTimers`, auto-advance to `VotePhaseState` on timeout. If timers disabled, no auto-advance (host must explicitly advance).

### VotePhaseState (timed)
- `HandleCommand(CastVoteCommand)`: Validate not voting for self, target is alive. Mark as voted. When all alive players have voted, tally and transition to `RevealPhaseState`.
- `Tick`: If `EnableTimers`, abstain for non-voters on timeout, tally, transition. If timers disabled, waits indefinitely for all votes.

### RevealPhaseState (timed)
- `OnEnter`: If elimination occurred, reveal role. Call `ApplyScoring()` (while vote data is still intact). Check win conditions. If game over → chain to `GameOverState`. Otherwise, call `ResetEliminationCycleState()` to clear per-cycle data.
- `Tick`: If `EnableTimers`, transition to `CluePhaseState` (next cycle) or `GameOverState` on timeout. If timers disabled, auto-advance still occurs (reveal is always timed to keep the game moving).

### GameOverState
- `OnEnter`: Calculate final scores for this game, accumulate into `GameScores`. Set phase to `GameOver`.
- `HandleCommand(StartNextGameCommand)`: Host only. If `CurrentGameNumber < TotalGames`, increment `CurrentGameNumber`, reset game state (roles, words, elimination cycles), transition to `SetupState` for the next game. Otherwise, this is the final game — show cumulative scores.
- `HandleCommand(ReturnToLobbyCommand)`: Host only. Returns to lobby.

---

## Phase 5: Engine

`ConsultTheCardGameEngine` — singleton, extends `AbstractGameEngine`:

**Constructor** (primary constructor pattern):
```csharp
public class ConsultTheCardGameEngine(
    IRandomNumberService randomNumberService,
    ILogger<ConsultTheCardGameEngine> logger,
    ILogger<ConsultTheCardGameState> stateLogger) : AbstractGameEngine
```

**`CreateStateAsync(User host, CancellationToken ct)`**:
- Validate host is not null
- Create `ConsultTheCardGameState(host, stateLogger)`
- Set `UpdateJoinableStatus(true)`
- Wire event: `gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState)`
- Return state

**`StartAsync(User host, AbstractGameState state, CancellationToken ct)`**:
- Validate `state is ConsultTheCardGameState` (type check)
- Validate `host == gameState.Host` (only host can start)
- Validate `gameState.Players.Count >= 4` (host is NOT included in Players count — host is separate)
- Validate `gameState.Players.Count <= 8`
- Create `ConsultTheCardGameContext(gameState, randomNumberService, logger)`
- Create `FiniteStateMachine<ConsultTheCardGameContext, ConsultTheCardCommand>(logger)` and assign to `context.Fsm`
- Inside `gameState.Execute()`: set not joinable, assign context, initialize game, transition to `SetupState`

**`TryGetContext(ConsultTheCardGameState state, out ConsultTheCardGameContext ctx, out Result err)`**: Private helper — returns false with error if `state.Context` is null (game not started).

**`ProcessCommand(ConsultTheCardGameContext context, ConsultTheCardCommand command)`**: Wraps `context.Fsm.HandleCommand(context, command)` inside `context.State.Execute()`.

**`Tick(ConsultTheCardGameContext context, DateTimeOffset now)`**: Wraps `context.Fsm.Tick(context, now)` inside `context.State.Execute()`. Always called regardless of `EnableTimers` — individual states decide whether to respect the timer (e.g., auto-advance on timeout) or ignore it (e.g., still allow non-timer-based transitions).

**Public UI methods** (all follow `TryGetContext` → create command → `ProcessCommand` pattern):
- `SubmitClue(User player, ConsultTheCardGameState state, string clue)`
- `CastVote(User player, ConsultTheCardGameState state, string targetPlayerId)`
- `InformantGuess(User player, ConsultTheCardGameState state, string guessedWord)`
- `AdvanceToVote(User player, ConsultTheCardGameState state)`
- `VoteToEndGame(User player, ConsultTheCardGameState state)`
- `StartNextGame(User player, ConsultTheCardGameState state)`
- `ReturnToLobby(User host, ConsultTheCardGameState state)` — host only, sets joinable, clears context
- `ResetGame(User host, ConsultTheCardGameState state)` — host only, full reset for new session

**`HandlePlayerLeft(User player, ConsultTheCardGameState state)`**: Remove from turn order, adjust `CurrentCluePlayerIndex` if needed, mark as eliminated, check win conditions, auto-advance if the leaving player was the current clue giver or if all votes are now in.

---

## Phase 6: Integration (3 files in KnockBox project)

1. **`KnockBox/Services/Navigation/Games/GameTypes.cs`** — Add `ConsultTheCard` enum variant with `[Description("Consult The Card")]` and `[NavigationString("consult-the-card")]`
2. **`KnockBox/Services/Registrations/Logic/LogicRegistrations.cs`** — Add `services.AddSingleton<ConsultTheCardGameEngine>()`
3. **`KnockBox/Services/Logic/Games/Shared/LobbyService.cs`** — Add `GameType.ConsultTheCard => serviceProvider.GetService<ConsultTheCardGameEngine>()` to switch

---

## Phase 7: Word Bank

### Storage Format
Word groups are stored in a CSV file (`WordPairs.csv`) for easy editing and bulk additions. Each row contains 2 or more thematically related words (variable-length):
```csv
Ocean,Lake
Guitar,Violin,Cello,Harp
Castle,Fortress,Palace
Sunrise,Sunset,Dawn,Dusk
Astronaut,Pilot
Mountain,Hill,Peak,Ridge,Summit
```

No header row. Each row is a thematic group. At runtime, `SelectWordPair()` picks a random group, selects 2 words at random from it, then randomly assigns Agent/Insider roles. A row with N words produces N*(N-1) possible configurations (e.g., a 5-word row yields 20 configurations). This dramatically increases variety and makes memorizing the word list useless — even knowing the group doesn't tell you which two words are in play or which role you have.

### `WordBank.cs`
- Static class that loads and parses `WordPairs.csv` at startup (embedded resource or file read)
- Returns `IReadOnlyList<WordGroup>` where `WordGroup(string[] Words)` contains 2+ words
- Validates each row has at least 2 words on load
- Target: ~1000 word groups eventually. Initial implementation ships with 50-100 groups; the CSV format makes it trivial to add more over time. Groups with more words are especially high-value since they multiply configurations.
- Word groups follow the design philosophy: "thematically adjacent but distinct" — all words in a group should share some associations but diverge on others. Too similar (car/automobile) makes the game impossible for Agents; too different (banana/algebra) makes it trivial.

---

## Phase 8: Blazor UI

Following the CardCounter UI pattern exactly: one main lobby page (`DisposableComponent`) with phase-specific child components (`ComponentBase`).

### ConsultTheCardLobby (main page)
- **Route**: `@page "/room/consult-the-card/{ObfuscatedRoomCode}"`
- **Extends**: `DisposableComponent`
- **Injects**: `ConsultTheCardGameEngine`, `IGameSessionService`, `INavigationService`, `IUserService`, `ITickService`, `ILogger`
- **OnInitializedAsync**: Initialize user, get session, cast state to `ConsultTheCardGameState`, subscribe to `StateChangedEventManager`
- **Host tick**: Register tick callback via `TickService.RegisterTickCallback(() => GameEngine.Tick(...), tickInterval: TickService.TicksPerSecond)` — only host registers
- **Phase switching**: Render child component based on `GameState.GamePhase` and `GameState.IsJoinable`:
  ```
  if (IsJoinable) → <LobbyPhase />
  else if (Setup) → <SetupPhase />
  else if (CluePhase) → <CluePhase />
  else if (Discussion) → <DiscussionPhase />
  else if (Voting) → <VotePhase />
  else if (Reveal) → <RevealPhase />
  else if (GameOver) → <GameOverPhase />
  ```
- **Timer display**: If current FSM state implements `ITimedConsultTheCardGameState`, show countdown
- **Kicked player detection**: Check `GameState.KickedPlayers` in `OnAfterRender`
- **Dispose**: Unsubscribe state change listener, dispose tick registration

### LobbyPhase
- Player list with host badge, kick buttons (host only)
- Host settings drawer: timer toggles, timeout durations, total games count
- Start button (host only, enabled when 4-8 players joined)
- Calls `GameEngine.StartAsync()`

### SetupPhase
- Shows the player their secret word in a large card-like display
- Informant sees a blank/special "???" card
- Brief phase (5s) — auto-advances via timer
- No player actions needed (purely informational)

### CluePhase
- Shows whose turn it is to give a clue (highlighted player)
- Active player sees a text input + submit button
- Non-active players see a waiting indicator
- Clue history: list of submitted clues for this cycle (player name + clue word)
- Previously used clues shown as disabled hints
- Calls `GameEngine.SubmitClue()`

### DiscussionPhase
- All submitted clues displayed prominently in a grid/list
- Player list with role-unknown indicators
- "Guess the Agents' Word" button — opens a text input for any player to attempt
- "Vote to End Game" button — any player can vote; shows progress (X of Y needed)
- Host "Advance to Vote" button to end discussion early
- Timer countdown shown
- Calls `GameEngine.InformantGuess()`, `GameEngine.VoteToEndGame()`, or `GameEngine.AdvanceToVote()`
- If an Informant guess result occurs, show overlay with result (correct = game over, wrong = player eliminated)

### VotePhase
- Grid of alive players (excluding self) as vote targets
- Each player clicks one target to cast their vote
- Vote confirmation: selected player highlighted, confirm button
- Progress indicator: "X of Y votes cast"
- Once voted, show "Waiting for others..." with lock icon
- Calls `GameEngine.CastVote()`

### RevealPhase
- If elimination: dramatic reveal of eliminated player's name and role (Agent/Insider/Informant)
- If tie: "No elimination — tied vote" message
- Round summary: vote tally breakdown (who voted for whom)
- Score changes for this cycle
- Auto-advances via timer to next CluePhase or GameOver

### GameOverPhase
- Winner announcement (team or Informant) with reason
- Game scoreboard: all players ranked by score with role revealed
- Scoring breakdown per player
- Multi-game progress: "Game X of Y" indicator
- If more games remain: Host "Next Game" button → calls `GameEngine.StartNextGame()`
- If final game: cumulative scoreboard across all games, overall winner
- Host buttons: "Return to Lobby" or "Play Again" (resets game count)
- Calls `GameEngine.ReturnToLobby()` or `GameEngine.ResetGame()`

---

## Phase 9: Unit Tests

### Test Project: `KnockBox.ConsultTheCardTests`
- MSTest + Moq (matching CardCounter test patterns)
- Method-level parallelization

### Standard Test Setup Pattern
```
[TestInitialize]: Mock IRandomNumberService, ILogger, create host User,
create ConsultTheCardGameState, create ConsultTheCardGameContext
```

### Helper Methods
- `MakePlayer(id, name, role, secretWord)` — Create and register player in state
- `SetupGameWithPlayers(count)` — Add N players with proper role distribution
- `SetCurrentCluePlayer(index)` — Set active clue giver

### Test Files & Coverage

**ConsultTheCardGameContextTests.cs**
- `AssignRoles` produces correct distribution for each player count (4-8)
- `SelectWordPair` picks unused group, selects 2 words from group, randomly assigns Agent/Insider roles, handles exhaustion
- `SelectWordPair` works correctly with groups of varying sizes (2, 3, 5+ words)
- `TallyVotes` returns correct winner, handles ties
- `CheckWinConditions` — all Insiders+Informant eliminated → Agents win
- `CheckWinConditions` — 2 players remain → Insiders win
- `CheckWinConditions` — majority voted to end game → Insiders win
- `CheckWinConditions` — game continues when neither met
- `ResetEliminationCycleState` clears per-cycle data
- `GetAlivePlayers` excludes eliminated players
- `ApplyScoring` — correct scores for each outcome (+2 survived, +1 winning team, +3 Informant guess, -1 voted for Agent)
- `ApplyScoring` is called before reset so vote data is available

**ConsultTheCardGameEngineTests.cs**
- `CreateStateAsync` returns valid state with `PlayerUnregistered` wired
- `StartAsync` fails if not host
- `StartAsync` fails if <4 players (host not counted)
- `StartAsync` fails if >8 players
- `StartAsync` fails if state type is wrong
- `StartAsync` succeeds and transitions to SetupState
- `TryGetContext` returns error when game not started
- All public methods return error when context is null
- `ReturnToLobby` only works for host after GameOver
- `ResetGame` reinitializes state
- `Tick` always delegates to FSM regardless of `EnableTimers`

**ConsultTheCardGameEnginePlayerLeftTests.cs**
- Player leaves during CluePhase (was current clue giver) → advances to next
- Player leaves during VotePhase → rechecks if all voted
- Insider leaves → check if Agents now win
- All players leave → GameOver
- Player leaves during Discussion → adjusts alive count

**SetupStateTests.cs**
- `OnEnter` assigns roles matching scaling table
- `OnEnter` sets word pair on all players
- Informant gets null SecretWord
- `Tick` auto-advances to CluePhaseState after 5s timeout

**CluePhaseStateTests.cs**
- Valid clue submission stores clue and advances turn
- Clue with spaces rejected
- Clue matching secret word rejected
- Previously used clue rejected
- Non-active player's submission rejected
- All clues submitted → transitions to DiscussionPhaseState
- Timeout auto-submits "..." and advances

**DiscussionPhaseStateTests.cs**
- Informant guesses correctly → GameOverState (Informant wins)
- Informant guesses incorrectly → eliminated, scoring applied, check win conditions
- Non-Informant guess → rejected (no-op)
- VoteToEndGame — single vote recorded, game continues
- VoteToEndGame — majority reached → Insiders win → GameOverState
- Host advances to vote → VotePhaseState
- Non-host cannot advance to vote
- Timeout → VotePhaseState

**VotePhaseStateTests.cs**
- Valid vote recorded
- Cannot vote for self
- Cannot vote for eliminated player
- All votes in → transitions to RevealPhaseState
- Tie → EliminationResult.WasTie = true
- Majority → correct player eliminated
- Timeout → abstaining players skipped, tally proceeds

**RevealPhaseStateTests.cs**
- Elimination of last Insider → Agents win → GameOverState
- Elimination reduces to 2 players → Insiders win → GameOverState
- Tie (no elimination) → next CluePhaseState
- Scoring applied correctly before cycle reset
- -1 penalty applied to players who voted for an Agent
- Tick auto-advances

**GameOverStateTests.cs**
- `OnEnter` calculates final scores and accumulates into `GameScores`
- `StartNextGameCommand` advances to next game when games remain
- `StartNextGameCommand` rejected when all games complete
- `ReturnToLobbyCommand` returns to lobby
- Multi-game: cumulative scores tracked correctly across games

---

## Key Design Decisions

1. **Players don't know their role** — they see their word (or blank) but not whether it's the Agent or Insider word. Role is only revealed on elimination.
2. **Informant guess** — any player can attempt it during Discussion. Only the actual Informant gets the win; non-Informants are silently rejected (the command is a no-op). This preserves the secret that you don't know who the Informant is.
3. **Tie votes** — no elimination, new clue cycle begins.
4. **Player disconnect** — counts as elimination for win-condition purposes.
5. **End game vote** — any player can vote to end the game during Discussion. If a majority of alive players vote to end, Insiders win. This implements the GDD's "Agents vote to end the game" win condition as a democratic mechanism.
6. **Synonym checking** — the GDD prohibits direct synonyms as clues, but programmatic synonym detection is infeasible. This rule is enforced socially by players, consistent with the in-person party game design.
7. **Multi-game sessions** — `TotalGames` controls how many full games are played. Each game has its own role assignments, word pair, and elimination cycles. Scores accumulate across games. `CurrentEliminationCycle` tracks cycles within a single game; `CurrentGameNumber` tracks games within a session.

---

## Verification

1. `dotnet build` — solution builds successfully (including `IFiniteStateMachine` rename)
2. `dotnet test` — all unit tests pass
3. Manual: create lobby, join 4+ players, play through full game flow
4. Verify integration: `LobbyService` can create ConsultTheCard lobby, players can join via lobby code
5. Verify multi-game: play through TotalGames games, confirm cumulative scoring

---

## Critical Files to Modify (existing)
- `KnockBox.ConsultTheCard/KnockBox.ConsultTheCard.csproj` (after rename)
- `KnockBox/Services/Navigation/Games/GameTypes.cs`
- `KnockBox/Services/Registrations/Logic/LogicRegistrations.cs`
- `KnockBox/Services/Logic/Games/Shared/LobbyService.cs`
- `KnockBox.slnx` (rename project ref, add test project)
- `KnockBox.Core/Services/State/Games/Shared/IFiniteStateMachine.cs` (fix typo)
- `KnockBox.Core/Services/State/Games/Shared/FiniteStateMachine.cs` (fix typo)
- `KnockBox.CardCounter/Services/Logic/Games/CardCounter/FSM/CardCounterGameContext.cs` (fix typo ref)
- `KnockBox.DrawnToDress/Services/Logic/Games/DrawnToDress/DrawnToDressGameContext.cs` (fix typo ref)

## Critical Files to Reference (patterns)
- `KnockBox.CardCounter/Services/Logic/Games/CardCounter/CardCounterGameEngine.cs`
- `KnockBox.CardCounter/Services/State/Games/CardCounter/CardCounterGameState.cs`
- `KnockBox.CardCounter/Services/Logic/Games/CardCounter/FSM/CardCounterGameContext.cs`
- `KnockBox.Core/Services/Logic/Games/Engines/Shared/AbstractGameEngine.cs`
- `KnockBox.Core/Services/State/Games/Shared/IFiniteStateMachine.cs`
- `KnockBox/Components/Pages/Games/CardCounter/CardCounterLobby.razor.cs` (UI pattern)
- `KnockBox/Components/Shared/DisposableComponent.cs` (base class)
- `KnockBox.CardCounterTests/KnockBox.CardCounterTests.csproj` (test project setup)
