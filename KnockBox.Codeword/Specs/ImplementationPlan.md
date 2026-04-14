# Codeword - Implementation Plan

## Context

The `KnockBox.Codeword` project exists as a stub (empty `Class1.cs`). The game design document at `Specs/GameDesignDocument.md` fully specifies a social deduction party game for 4-8 players. This plan implements the full game: backend logic, Blazor UI, unit tests, and integration wiring — all following the CardCounter architecture pattern. Power Cards are excluded per the design doc.

---

## File Structure

```
KnockBox.Codeword/
  Services/
    Logic/Games/Codeword/
      CodewordGameEngine.cs
      Data/
        WordBank.cs                          # Loads word pairs from CSV
        WordPairs.csv                        # CSV file with variable-length word groups (2+ words per row, 2 selected at runtime)
      FSM/
        CodewordCommand.cs
        CodewordGameContext.cs
        ICodewordGameState.cs
        States/
          SetupState.cs
          CluePhaseState.cs
          DiscussionPhaseState.cs
          VotePhaseState.cs
          RevealPhaseState.cs
          GameOverState.cs
    State/Games/Codeword/
      CodewordGameState.cs
      Data/
        CodewordPlayerState.cs

KnockBox/Components/Pages/Games/Codeword/
  CodewordLobby.razor              # Main page, routing, tick, phase switching
  CodewordLobby.razor.cs
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

KnockBox.CodewordTests/
  KnockBox.CodewordTests.csproj
  Unit/Logic/Games/Codeword/
    CodewordGameContextTests.cs
    CodewordGameEngineTests.cs
    CodewordGameEnginePlayerLeftTests.cs
    SetupStateTests.cs
    CluePhaseStateTests.cs
    DiscussionPhaseStateTests.cs
    VotePhaseStateTests.cs
    RevealPhaseStateTests.cs
    GameOverStateTests.cs
    WordBankTests.cs
```

---

## Phase 0: Codebase Fixes

1. **Rename project**: Rename `Knockbox.Codeword` folder and project to `KnockBox.Codeword` to match the naming convention (`KnockBox.CardCounter`, `KnockBox.DiceSimulator`, etc.). Update `KnockBox.slnx` reference accordingly.
2. **Fix `IFininteStateMachine` typo**: Rename `IFininteStateMachine` to `IFiniteStateMachine` across the codebase:
   - `KnockBox.Core/Services/State/Games/Shared/IFiniteStateMachine.cs` (interface name)
   - `KnockBox.Core/Services/State/Games/Shared/FiniteStateMachine.cs` (implementation references)
   - `KnockBox.CardCounter/Services/Logic/Games/CardCounter/FSM/CardCounterGameContext.cs` (property type)
   - `KnockBox.DrawnToDress/Services/Logic/Games/DrawnToDress/DrawnToDressGameContext.cs` (property type)

---

## Phase 1: Project Scaffolding

1. Delete `KnockBox.Codeword/Class1.cs`
2. Update `KnockBox.Codeword.csproj`:
   - Add `<ProjectReference>` to `KnockBox.Core`
   - Add `<Using Include="Microsoft.Extensions.Logging" />`
   - Add `InternalsVisibleTo` for `KnockBox.CodewordTests`
3. Create `KnockBox.CodewordTests/` project:
   - .NET 10.0, MSTest (`Microsoft.VisualStudio.TestTools.UnitTesting`), Moq
   - References: `KnockBox.Codeword`, `KnockBox.Core`
   - Method-level parallelization via `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`
   - Add to solution file

---

## Phase 2: Data Models

### Enums (in `CodewordGameState.cs`)
- `CodewordGamePhase`: `Setup`, `CluePhase`, `Discussion`, `Voting`, `Reveal`, `GameOver`
- `Role`: `Agent`, `Insider`, `Informant`

### Records (in `CodewordGameState.cs`)
- `WordGroup(string[] Words)` — a thematic group of 2+ words; at runtime, 2 are selected and randomly assigned as Agent/Insider
- `ClueEntry(string PlayerId, string PlayerName, string Clue)`
- `VoteEntry(string VoterId, string VoterName, string TargetId, string TargetName)`
- `EliminationResult(string PlayerId, string PlayerName, Role Role, bool WasTie)`
- `InformantGuessResult(string PlayerId, string PlayerName, string GuessedWord, bool WasCorrect)`
- `WinConditionResult(bool GameOver, Role? WinningTeam, string Reason)`
- `EndGameVoteStatus(HashSet<string> VotedToEnd, int RequiredVotes)` — tracks player votes to end the game

### CodewordPlayerState (separate file)
- `PlayerId`, `DisplayName`, `Role`, `SecretWord` (null for Informant)
- `IsEliminated`, `HasSubmittedClue`, `CurrentClue`, `VoteTargetId`, `HasVoted`
- `HasVotedToEndGame` — tracks whether this player has voted to end the game this elimination cycle (reset each cycle, one vote per cycle per player to prevent spam)
- `Score`

### CodewordGameConfig
- `SetupPhaseTimeoutMs` (5000), `CluePhaseTimeoutMs` (30000), `DiscussionPhaseTimeoutMs` (120000), `VotePhaseTimeoutMs` (15000), `RevealPhaseTimeoutMs` (10000), `InformantGuessTimeoutMs` (15000)
- `EnableTimers`, `TotalGames` (for multi-game scoring, default 5)

### CodewordGameState (extends AbstractGameState)
- `Context`, `GamePhase`, `GamePlayers` (ConcurrentDictionary), `TurnOrder`
- `CurrentCluePlayerIndex`, `CurrentEliminationCycle` (int, initialized to 0), `CurrentGameNumber` (int, initialized to 1), `CurrentWordPair`
- `CurrentRoundClues`, `CurrentRoundVotes`
- `LastElimination`, `LastInformantGuess`, `AwaitingInformantGuess`, `WinResult`, `Config`
- `EndGameVoteStatus` — tracking for the "vote to end game" mechanic
- `UsedClues` (HashSet<string>) — all clue words used by any player in the current game (prevents reuse across players and cycles)
- `GameScores` (Dictionary<string, int>) — cumulative scores across all games in a multi-game session

---

## Phase 3: FSM Infrastructure

### Commands (`CodewordCommand.cs`)
```
CodewordCommand(string PlayerId)               # abstract base
├── SubmitClueCommand(PlayerId, string Clue)          # CluePhase
├── AdvanceToVoteCommand(PlayerId)                    # Discussion (host only)
├── VoteToEndGameCommand(PlayerId)                    # Discussion (any player, once per cycle — majority ends game, win conditions evaluated)
├── CastVoteCommand(PlayerId, string TargetPlayerId)  # Voting
├── InformantGuessCommand(PlayerId, string GuessedWord) # Reveal (only if eliminated player is Informant, one attempt)
├── StartNextGameCommand(PlayerId)                    # GameOver (host only, advances to next game in series)
└── ReturnToLobbyCommand(PlayerId)                    # GameOver (host only)
```

### Type Aliases (`ICodewordGameState.cs`)
- `ICodewordGameState : IGameState<CodewordGameContext, CodewordCommand>`
- `ITimedCodewordGameState : ITimedGameState<...>, ICodewordGameState`

### Context (`CodewordGameContext.cs`)
References: `State`, `Rng`, `Logger`, `Fsm` (`IFiniteStateMachine<...>`), `UsedWordPairIndices` (HashSet)

Key helpers:
- **`AssignRoles()`** — Uses scaling table: 4p=3A/1I, 5p=3A/1I/1Inf, 6p=4A/1I/1Inf, 7p=4A/2I/1Inf, 8p=5A/2I/1Inf. Shuffles players randomly, assigns roles and words.
- **`SelectWordPair()`** — Picks a random unused `WordGroup` from `WordBank`, selects 2 words at random from the group, then randomly assigns which becomes the Agent word and which becomes the Insider word. A group with N words produces N*(N-1) possible configurations, dramatically increasing variety and discouraging memorization.
- **`GetAlivePlayers()`** / **`GetAlivePlayerCount()`**
- **`TallyVotes()`** — Returns player ID with most votes, or null on tie
- **`CheckWinConditions()`** — The game auto-ends **only** when <=2 players remain or when a majority of alive players voted to end the game. The game does **not** auto-end when all Insiders/Informant are eliminated (this would leak role information). When the game ends, evaluate winners in priority order: (1) If the Informant is alive → Informant wins. (2) Else if any Insider is alive → Insiders win. (3) Else → Agents win. Returns `WinConditionResult(GameOver, WinningTeam, Reason)` or `continue` if neither end trigger is met.
- **`ResetEliminationCycleState()`** — Clears per-cycle clue/vote/end-game-vote data for all alive players
- **`ApplyCycleScoring()`** — Per-cycle penalties only: -1 for each player who voted for an Agent. Must be called **before** `ResetEliminationCycleState()` so vote data is still available.
- **`ApplyEndOfGameScoring()`** — End-of-game bonuses: +2 for each player who survived, +1 for each player on the winning team, +3 if the Informant correctly guessed the Agents' word. Called once in `GameOverState.OnEnter` when the game ends.

---

## Phase 4: FSM States

### SetupState (timed, 5s timeout)
- `OnEnter`: Increment `CurrentEliminationCycle` (initial value is 0, so first cycle becomes 1; reset to 0 by `StartNextGameCommand` between games), call `AssignRoles()` + `SelectWordPair()`, randomize clue order, set phase to `Setup`
- `Tick`: Auto-advance to `CluePhaseState` after `SetupPhaseTimeoutMs` (5s)
- `GetRemainingTime`: Returns countdown based on expiration set in `OnEnter`

### CluePhaseState (timed)
- `OnEnter`: Advance `CurrentCluePlayerIndex` past any eliminated players to find the next alive player (wraps around `TurnOrder`). This produces a **rotating start player** — each elimination cycle picks up from where the previous one left off, so no player always goes first. If no alive players remain, transition to `GameOverState`.
- Tracks whose turn it is to give a clue (skips eliminated players on advancement)
- `HandleCommand(SubmitClueCommand)`: Validate single word (no spaces), not the player's secret word, not previously used by any player in the current game (checked against game-level `UsedClues`). Store clue, add to `UsedClues`, advance to next player. When all alive players have submitted, transition to `DiscussionPhaseState`.
- `Tick`: If `EnableTimers`, auto-submit "..." for timed-out player and advance. If timers disabled, no auto-action (players submit manually with no time pressure).
- **Known limitation:** The GDD prohibits "direct synonyms of the secret word." Synonym detection is not feasible to implement programmatically, so this rule is enforced socially (players call out violations). This is consistent with the in-person party game design.

### DiscussionPhaseState (timed)
- `HandleCommand(VoteToEndGameCommand)`: Any alive player can vote to end the game, **once per elimination cycle** (to prevent spam). Track in `EndGameVoteStatus`. If majority of alive players have voted to end, game ends → evaluate win conditions → `GameOverState`.
- `HandleCommand(AdvanceToVoteCommand)`: Host only. Transitions to `VotePhaseState`.
- `Tick`: If `EnableTimers`, auto-advance to `VotePhaseState` on timeout. If timers disabled, no auto-advance (host must explicitly advance).

### VotePhaseState (timed)
- `HandleCommand(CastVoteCommand)`: Validate not voting for self, target is alive. Mark as voted. When all alive players have voted, call `TallyVotes()`. If a player has the most votes (no tie), mark them as `IsEliminated = true` and set `LastElimination`. If tie, set `LastElimination` with `WasTie = true`. Transition to `RevealPhaseState`.
- `Tick`: If `EnableTimers`, abstain for non-voters on timeout, tally (same elimination logic as above), transition. If timers disabled, waits indefinitely for all votes.

### RevealPhaseState (timed)
- `OnEnter`: Call `ApplyCycleScoring()` (per-cycle penalties while vote data is still intact — applies regardless of whether elimination occurred or was a tie). Then:
  - **If elimination occurred** (not a tie): If the eliminated player is the **Informant**, set `AwaitingInformantGuess = true` and pause auto-advance — the Informant gets one chance to guess the Agents' word (this is the only case where a role is revealed during gameplay). If **not** the Informant, call `CheckWinConditions()` — if game should end (≤2 players remain), transition to `GameOverState`; otherwise call `ResetEliminationCycleState()`.
  - **If tie** (no elimination): Call `ResetEliminationCycleState()`. Will auto-advance to next `CluePhaseState` via Tick.
- Roles are **not** revealed during gameplay. The Informant's identity is revealed only through the guess mechanic. All other eliminated players' roles remain hidden until `GameOverPhase`.
- `HandleCommand(InformantGuessCommand)`: Only accepted if `AwaitingInformantGuess == true` and command sender is the eliminated Informant. If correct: Informant wins → `GameOverState`. If wrong: set `AwaitingInformantGuess = false`, record result in `LastInformantGuess`, call `CheckWinConditions()` — if game should end, transition to `GameOverState`; otherwise call `ResetEliminationCycleState()` and continue to next cycle. Only one guess attempt is permitted.
- `Tick`: If `AwaitingInformantGuess`, use a separate `InformantGuessTimeoutMs` (default 15000ms) — if the Informant doesn't guess in time, treat as forfeited (same flow as wrong guess: check win conditions, reset cycle state). Otherwise, auto-advance to `CluePhaseState` (next cycle) on timeout (reveal is always timed to keep the game moving).

### GameOverState
- `OnEnter`: Call `ApplyEndOfGameScoring()` (+2 survived, +1 winning team, +3 Informant correct guess). Accumulate game scores into `GameScores`. Set phase to `GameOver`.
- `HandleCommand(StartNextGameCommand)`: Host only. If `CurrentGameNumber < TotalGames`, increment `CurrentGameNumber` and reset per-game state:
    - Clear on all players: `Role`, `SecretWord`, `IsEliminated`, `HasSubmittedClue`, `CurrentClue`, `VoteTargetId`, `HasVoted`, `HasVotedToEndGame`, `Score`
    - Reset game-level state: `CurrentEliminationCycle` to 0, `CurrentCluePlayerIndex` to 0, `CurrentWordPair` to null, `CurrentRoundClues` (clear), `CurrentRoundVotes` (clear), `UsedClues` (clear)
    - Clear: `LastElimination`, `LastInformantGuess`, `AwaitingInformantGuess`, `WinResult`, `EndGameVoteStatus`
    - Re-randomize `TurnOrder`
    - **Preserve**: `UsedWordPairIndices` (avoid repeat word groups across games), `GameScores` (cumulative)
    - Transition to `SetupState` for the next game.
  Otherwise, this is the final game — show cumulative scores.
- `HandleCommand(ReturnToLobbyCommand)`: Host only. Returns to lobby.

---

## Phase 5: Engine

`CodewordGameEngine` — singleton, extends `AbstractGameEngine`:

**Properties**:
- `MinPlayerCount` → 4
- `MaxPlayerCount` → 8

**Host Role**: The host is a **spectator only**. They manage the lobby (start game, kick players, advance phases) but do not receive a role, give clues, or vote. The host observes the game state and sees all public information. `Players` (from `AbstractGameState`) contains only the participating players — the host is excluded. Host-only commands (`AdvanceToVoteCommand`, `StartNextGameCommand`, `ReturnToLobbyCommand`) use the host's user ID as `PlayerId`; FSM state handlers validate the sender by checking `command.PlayerId == context.State.Host.Id`.

**Constructor** (primary constructor pattern):
```csharp
public class CodewordGameEngine(
    IRandomNumberService randomNumberService,
    ILogger<CodewordGameEngine> logger,
    ILogger<CodewordGameState> stateLogger) : AbstractGameEngine
```

**`CreateStateAsync(User host, CancellationToken ct)`** → `Task<ValueResult<AbstractGameState>>`:
- Validate host is not null
- Create `CodewordGameState(host, stateLogger)`
- Set `UpdateJoinableStatus(true)`
- Wire event: `gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState)`
- Return state

**`StartAsync(User host, AbstractGameState state, CancellationToken ct)`** → `Task<Result>`:
- Validate `state is CodewordGameState` (type check)
- Validate `host == gameState.Host` (only host can start)
- Player count validated by `CanStartAsync()` via inherited `MinPlayerCount` (4) and `MaxPlayerCount` (8) — the host is a **spectator only** and does not participate in the game (no role, no clues, no votes)
- Create `CodewordGameContext(gameState, randomNumberService, logger)`
- Create `FiniteStateMachine<CodewordGameContext, CodewordCommand>(logger)` and assign to `context.Fsm`
- Inside `gameState.Execute()`: set not joinable, assign context, initialize game, transition to `SetupState`

**`TryGetContext(CodewordGameState state, out CodewordGameContext ctx, out Result err)`**: Private helper — returns false with error if `state.Context` is null (game not started).

**`ProcessCommand(CodewordGameContext context, CodewordCommand command)`**: Wraps `context.Fsm.HandleCommand(context, command)` inside `context.State.Execute()`.

**`Tick(CodewordGameContext context, DateTimeOffset now)`**: Wraps `context.Fsm.Tick(context, now)` inside `context.State.Execute()`. Always called regardless of `EnableTimers` — individual states decide whether to respect the timer (e.g., auto-advance on timeout) or ignore it (e.g., still allow non-timer-based transitions).

**Public UI methods** (all follow `TryGetContext` → create command → `ProcessCommand` pattern):
- `SubmitClue(User player, CodewordGameState state, string clue)`
- `CastVote(User player, CodewordGameState state, string targetPlayerId)`
- `InformantGuess(User player, CodewordGameState state, string guessedWord)` — only valid during RevealPhase when eliminated player is the Informant
- `AdvanceToVote(User player, CodewordGameState state)`
- `VoteToEndGame(User player, CodewordGameState state)`
- `StartNextGame(User player, CodewordGameState state)`
- `ReturnToLobby(User host, CodewordGameState state)` — host only, sets joinable, clears context
- `ResetGame(User host, CodewordGameState state)` — host only, full reset for new session

**Important**: All state mutations — including `ReturnToLobby` (set joinable, clear context) and `ResetGame` (reinitialize state) — must be wrapped inside `context.State.Execute()` to acquire the semaphore and notify state change listeners. This applies to every public method, not just command-based ones.

**`HandlePlayerLeft(User player, CodewordGameState state)`**: Remove from turn order, adjust `CurrentCluePlayerIndex` if needed, mark as eliminated. If during VotePhase, void any votes cast for the disconnected player (reset `VoteTargetId`/`HasVoted` for voters who targeted this player). Check win conditions. Auto-advance if the leaving player was the current clue giver or if all votes are now in.

---

## Phase 6: Integration (3 files in KnockBox project)

1. **`KnockBox/Services/Navigation/Games/GameTypes.cs`** — Add `Codeword` enum variant with `[Description("Codeword")]` and `[NavigationString("codeword")]`
2. **`KnockBox/Services/Registrations/Logic/LogicRegistrations.cs`** — Add `services.AddSingleton<CodewordGameEngine>()`
3. **`KnockBox/Services/Logic/Games/Shared/LobbyService.cs`** — Add `GameType.Codeword => serviceProvider.GetService<CodewordGameEngine>()` to switch

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
- Static class that loads and parses `WordPairs.csv` via file read at startup
- `WordPairs.csv` must be configured in the `.csproj` as: `<Content Include="Services\Logic\Games\Codeword\Data\WordPairs.csv" CopyToOutputDirectory="PreserveNewest" />`
- Loads from disk using `File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Services/Logic/Games/Codeword/Data/WordPairs.csv"))`
- Returns `IReadOnlyList<WordGroup>` where `WordGroup(string[] Words)` contains 2+ words
- Validates each row has at least 2 words on load; rows with fewer than 2 words are skipped with a warning log
- Empty lines and whitespace-only lines are skipped
- Target: ~1000 word groups eventually. Initial implementation ships with 50-100 groups; the CSV format makes it trivial to add more over time. Groups with more words are especially high-value since they multiply configurations.
- Word groups follow the design philosophy: "thematically adjacent but distinct" — all words in a group should share some associations but diverge on others. Too similar (car/automobile) makes the game impossible for Agents; too different (banana/algebra) makes it trivial.

---

## Phase 8: Blazor UI

Following the CardCounter UI pattern exactly: one main lobby page (`DisposableComponent`) with phase-specific child components (`ComponentBase`).

### Error Display Pattern
Use **toast notifications** (following the CardCounter pattern with `@key`-based re-render and `@onanimationend` auto-dismiss) for all user-facing errors:
- **Clue rejections**: Show an **ambiguous** error message (e.g., "Clue not accepted. Try a different word.") to avoid revealing the secret word to other players. Do not specify *why* the clue was rejected (spaces, matches secret word, previously used) — all rejections use the same generic message.
- **Vote errors**: "You cannot vote for that player." (covers voting for self and voting for eliminated players)
- **VoteToEndGame spam**: "You have already voted to end the game this round."
- **Informant guess timeout**: "Time's up — guess forfeited."
- **General command rejections**: "Action not available right now." (for commands sent in wrong phase)

### CodewordLobby (main page)
- **Route**: `@page "/room/codeword/{ObfuscatedRoomCode}"`
- **Extends**: `DisposableComponent`
- **Injects**: `CodewordGameEngine`, `IGameSessionService`, `INavigationService`, `IUserService`, `ITickService`, `ILogger`
- **OnInitializedAsync**: Initialize user, get session, cast state to `CodewordGameState`, subscribe to `StateChangedEventManager`
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
- **Timer display**: If current FSM state implements `ITimedCodewordGameState`, show countdown
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
- All previously used clues (from any player, any cycle) shown as disabled hints via game-level `UsedClues`
- Calls `GameEngine.SubmitClue()`

### DiscussionPhase
- All submitted clues displayed prominently in a grid/list
- Player list with role-unknown indicators
- "Vote to End Game" button — any alive player can vote, once per elimination cycle; shows progress (X of Y needed). Button disabled after player has voted this cycle.
- Host "Advance to Vote" button to end discussion early
- Timer countdown shown
- Calls `GameEngine.VoteToEndGame()` or `GameEngine.AdvanceToVote()`

### VotePhase
- Grid of alive players (excluding self) as vote targets
- Each player clicks one target to cast their vote
- Vote confirmation: selected player highlighted, confirm button
- Progress indicator: "X of Y votes cast"
- Once voted, show "Waiting for others..." with lock icon
- Calls `GameEngine.CastVote()`

### RevealPhase
- If elimination: show the eliminated player's name. **Do not reveal their role** — roles are hidden during gameplay.
- If tie: "No elimination — tied vote" message
- **If eliminated player is the Informant**: This is the only case where a role is revealed mid-game. Show a text input and "Guess the Agents' Word" button **only to the eliminated Informant**. Other players see "The Informant is making their guess..." with a countdown timer. One attempt only. If correct → game over overlay. If wrong or timed out → result shown, game continues.
- Round summary: vote tally breakdown (who voted for whom)
- Per-cycle score changes are **not** displayed during gameplay (showing vote penalties would reveal roles). Scores are tracked internally and shown at game end.
- Auto-advances via timer to next CluePhase (paused while awaiting Informant guess)

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

### Test Project: `KnockBox.CodewordTests`
- MSTest + Moq (matching CardCounter test patterns)
- Method-level parallelization

### Standard Test Setup Pattern
```
[TestInitialize]: Mock IRandomNumberService, ILogger, create host User,
create CodewordGameState, create CodewordGameContext
```

### Helper Methods
- `MakePlayer(id, name, role, secretWord)` — Create and register player in state
- `SetupGameWithPlayers(count)` — Add N players with proper role distribution
- `SetCurrentCluePlayer(index)` — Set active clue giver

### Test Files & Coverage

**CodewordGameContextTests.cs**
- `AssignRoles` produces correct distribution for each player count (4-8)
- `SelectWordPair` picks unused group, selects 2 words from group, randomly assigns Agent/Insider roles, handles exhaustion
- `SelectWordPair` works correctly with groups of varying sizes (2, 3, 5+ words)
- `TallyVotes` returns correct winner, handles ties
- `CheckWinConditions` — 2 players remain, Informant alive → Informant wins
- `CheckWinConditions` — 2 players remain, Insider alive (no Informant) → Insiders win
- `CheckWinConditions` — 2 players remain, only Agents alive → Agents win
- `CheckWinConditions` — majority voted to end, Informant alive → Informant wins
- `CheckWinConditions` — majority voted to end, Insider alive (no Informant) → Insiders win
- `CheckWinConditions` — majority voted to end, only Agents alive → Agents win
- `CheckWinConditions` — all Insiders+Informant eliminated but >2 players remain → game continues (no auto-resolve)
- `CheckWinConditions` — game continues when neither end trigger met
- `ResetEliminationCycleState` clears per-cycle data
- `GetAlivePlayers` excludes eliminated players
- `ApplyCycleScoring` — applies -1 penalty to players who voted for an Agent, called before ResetEliminationCycleState so vote data is available
- `ApplyEndOfGameScoring` — applies +2 survived, +1 winning team, +3 Informant correct guess, called once at game end

**CodewordGameEngineTests.cs**
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

**CodewordGameEnginePlayerLeftTests.cs**
- Player leaves during CluePhase (was current clue giver) → advances to next
- Player leaves during VotePhase → votes targeting them are voided, rechecks if all voted
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
- Clue previously used by any player in the current game rejected
- Non-active player's submission rejected
- All clues submitted → transitions to DiscussionPhaseState
- Timeout auto-submits "..." and advances

**DiscussionPhaseStateTests.cs**
- VoteToEndGame — single vote recorded, game continues
- VoteToEndGame — duplicate vote from same player in same cycle rejected
- VoteToEndGame — majority reached → game ends, win conditions evaluated → GameOverState
- Host advances to vote → VotePhaseState
- Non-host cannot advance to vote
- Timeout → VotePhaseState

**VotePhaseStateTests.cs**
- Valid vote recorded
- Cannot vote for self
- Cannot vote for eliminated player
- All votes in → transitions to RevealPhaseState
- Tie → EliminationResult.WasTie = true, no player marked IsEliminated
- Majority → correct player marked IsEliminated, LastElimination set
- Timeout → abstaining players skipped, tally proceeds

**RevealPhaseStateTests.cs**
- Elimination of non-Informant → role NOT revealed, no guess prompt, auto-advances
- Elimination of Informant → role revealed via guess mechanic, AwaitingInformantGuess set, auto-advance paused
- Informant guesses correctly → Informant wins → GameOverState
- Informant guesses incorrectly → game continues, result recorded
- Informant guess timeout → treated as forfeited, game continues
- Non-Informant player sends InformantGuessCommand → rejected
- Elimination reduces to 2 players → game ends, win conditions evaluated
- Elimination of last Insider/Informant with >2 remaining → game continues (no auto-resolve)
- Tie (no elimination) → next CluePhaseState
- Per-cycle scoring (-1 penalty) applied correctly before cycle reset
- Tick auto-advances after reveal (or after Informant guess resolves)

**GameOverStateTests.cs**
- `OnEnter` calls `ApplyEndOfGameScoring` and accumulates into `GameScores`
- `StartNextGameCommand` advances to next game when games remain
- `StartNextGameCommand` rejected when all games complete
- `ReturnToLobbyCommand` returns to lobby
- Multi-game: cumulative scores tracked correctly across games

**WordBankTests.cs**
- Parses valid CSV with 2-word rows correctly
- Parses valid CSV with variable-length rows (2, 3, 5+ words)
- Skips empty lines and whitespace-only lines
- Skips rows with fewer than 2 words (logs warning)
- Trims whitespace from words
- Returns empty list for empty file (does not throw)
- All returned `WordGroup` entries have at least 2 words

---

## Key Design Decisions

1. **Players don't know their role** — they see their word (or blank) but not whether it's the Agent or Insider word. Roles are **not** revealed on elimination (except the Informant, whose identity is revealed through the guess mechanic). All roles are revealed at game end.
2. **Informant guess** — the Informant can only attempt a guess when they are voted out during RevealPhase. They get one attempt with a 15-second timeout. The guess UI is only shown to the eliminated Informant; other players see a waiting message. This preserves secrecy during gameplay — no one can reveal themselves as the Informant by attempting a guess.
3. **Tie votes** — no elimination, new clue cycle begins.
4. **Player disconnect** — counts as elimination for win-condition purposes. During VotePhase, votes cast for the disconnected player are voided.
5. **End game vote** — any alive player can vote to end the game during Discussion, once per elimination cycle (to prevent spam). If a majority of alive players vote to end, the game ends and win conditions are evaluated in priority order (Informant → Insider → Agent). This is a democratic mechanism that ties into the Insider goal of gaining the Agents' trust.
6. **Synonym checking** — the GDD prohibits direct synonyms as clues, but programmatic synonym detection is infeasible. This rule is enforced socially by players, consistent with the in-person party game design.
7. **Multi-game sessions** — `TotalGames` controls how many full games are played. Each game has its own role assignments, word pair, and elimination cycles. Scores accumulate across games. `CurrentEliminationCycle` tracks cycles within a single game; `CurrentGameNumber` tracks games within a session.
8. **Clue reuse** — No clue word may be used by any player more than once per game. Tracked via game-level `UsedClues` (HashSet<string>), cleared between games.
9. **No role reveal during gameplay** — Eliminated players' roles are not revealed (except the Informant via the guess mechanic). This prevents information leaks that would trivialize deduction. All roles are revealed at game end. Per-cycle score breakdowns are also hidden during gameplay since vote penalties would reveal roles.
10. **Rotating start player** — `CurrentCluePlayerIndex` is not reset between elimination cycles. Each new CluePhase picks up from where the previous cycle left off (wrapping around `TurnOrder`), so no player always goes first.

---

## Verification

1. `dotnet build` — solution builds successfully (including `IFiniteStateMachine` rename)
2. `dotnet test` — all unit tests pass
3. Manual: create lobby, join 4+ players, play through full game flow
4. Verify integration: `LobbyService` can create Codeword lobby, players can join via lobby code
5. Verify multi-game: play through TotalGames games, confirm cumulative scoring

---

## Critical Files to Modify (existing)
- `KnockBox.Codeword/KnockBox.Codeword.csproj` (after rename)
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
