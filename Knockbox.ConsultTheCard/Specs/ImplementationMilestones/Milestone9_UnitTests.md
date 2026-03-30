# Milestone 9: Unit Tests

## Objective
Write comprehensive unit tests covering all game logic: context helpers, engine, player-left handling, all FSM states, and word bank parsing.

---

## Action Items

### 9.1 Test Infrastructure
- MSTest + Moq (matching CardCounter test patterns)
- Method-level parallelization
- Standard `[TestInitialize]`: Mock `IRandomNumberService`, `ILogger`, create host `User`, create `ConsultTheCardGameState`, create `ConsultTheCardGameContext`
- Helper methods:
  - `MakePlayer(id, name, role, secretWord)` -- create and register player in state
  - `SetupGameWithPlayers(count)` -- add N players with proper role distribution
  - `SetCurrentCluePlayer(index)` -- set active clue giver

### 9.2 ConsultTheCardGameContextTests.cs
- `AssignRoles` produces correct distribution for each player count (4-8)
- `SelectWordPair` picks unused group, selects 2 words, assigns roles, handles exhaustion
- `SelectWordPair` works with variable-size groups (2, 3, 5+ words)
- `TallyVotes` returns correct winner, handles ties
- `CheckWinConditions` -- 2 players remain: Informant alive -> Informant wins
- `CheckWinConditions` -- 2 players remain: Insider alive (no Informant) -> Insiders win
- `CheckWinConditions` -- 2 players remain: only Agents -> Agents win
- `CheckWinConditions` -- majority voted to end: Informant/Insider/Agent priority
- `CheckWinConditions` -- all Insiders+Informant eliminated but >2 remain -> game continues
- `CheckWinConditions` -- neither end trigger met -> game continues
- `ResetEliminationCycleState` clears per-cycle data
- `GetAlivePlayers` excludes eliminated players
- `ApplyCycleScoring` -- -1 penalty for voting for an Agent (called before reset)
- `ApplyEndOfGameScoring` -- +2 survived, +1 winning team, +3 Informant correct guess

### 9.3 ConsultTheCardGameEngineTests.cs
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

### 9.4 ConsultTheCardGameEnginePlayerLeftTests.cs
- Player leaves during CluePhase (was current clue giver) -> advances to next
- Player leaves during VotePhase -> rechecks if all voted
- Insider leaves -> check if Agents now win
- All players leave -> GameOver
- Player leaves during Discussion -> adjusts alive count

### 9.5 SetupStateTests.cs
- `OnEnter` assigns roles matching scaling table
- `OnEnter` sets word pair on all players
- Informant gets null `SecretWord`
- `Tick` auto-advances to CluePhaseState after 5s timeout

### 9.6 CluePhaseStateTests.cs
- Valid clue submission stores clue and advances turn
- Clue with spaces rejected
- Clue matching secret word rejected
- Previously used clue rejected
- Non-active player's submission rejected
- All clues submitted -> transitions to DiscussionPhaseState
- Timeout auto-submits "..." and advances

### 9.7 DiscussionPhaseStateTests.cs
- VoteToEndGame -- single vote recorded, game continues
- VoteToEndGame -- duplicate vote same player same cycle rejected
- VoteToEndGame -- majority reached -> game ends, win conditions evaluated -> GameOverState
- Host advances to vote -> VotePhaseState
- Non-host cannot advance to vote
- Timeout -> VotePhaseState

### 9.8 VotePhaseStateTests.cs
- Valid vote recorded
- Cannot vote for self
- Cannot vote for eliminated player
- All votes in -> transitions to RevealPhaseState
- Tie -> `EliminationResult.WasTie = true`
- Majority -> correct player eliminated
- Timeout -> abstaining players skipped, tally proceeds

### 9.9 RevealPhaseStateTests.cs
- Elimination of non-Informant -> no guess prompt, auto-advances
- Elimination of Informant -> `AwaitingInformantGuess` set, auto-advance paused
- Informant guesses correctly -> Informant wins -> GameOverState
- Informant guesses incorrectly -> game continues, result recorded
- Informant guess timeout -> forfeited, game continues
- Non-Informant sends InformantGuessCommand -> rejected
- Elimination reduces to 2 players -> game ends, win conditions evaluated
- Elimination of last Insider/Informant with >2 remaining -> game continues
- Tie (no elimination) -> next CluePhaseState
- Per-cycle scoring (-1 penalty) applied correctly before cycle reset
- Tick auto-advances after reveal (or after Informant guess resolves)

### 9.10 GameOverStateTests.cs
- `OnEnter` calls `ApplyEndOfGameScoring` and accumulates into `GameScores`
- `StartNextGameCommand` advances to next game when games remain
- `StartNextGameCommand` rejected when all games complete
- `ReturnToLobbyCommand` returns to lobby
- Multi-game: cumulative scores tracked correctly across games

### 9.11 WordBankTests.cs
- Parses valid CSV with 2-word rows correctly
- Parses valid CSV with variable-length rows (2, 3, 5+ words)
- Skips empty lines and whitespace-only lines
- Skips rows with fewer than 2 words (logs warning)
- Trims whitespace from words
- Returns empty list for empty file (does not throw)
- All returned `WordGroup` entries have at least 2 words

---

## Acceptance Criteria
- [ ] All test files listed above are created with the specified test cases
- [ ] All tests use the standard test setup pattern and helper methods
- [ ] `dotnet test` passes all tests
- [ ] Test coverage includes happy paths, error cases, edge cases, and boundary conditions
- [ ] Tests verify correct state transitions for all FSM states
- [ ] Tests verify scoring at both cycle and end-of-game levels
- [ ] Tests verify multi-game session flow (game progression, state preservation/reset)
- [ ] Tests verify player-left handling across all phases
