# Milestone 4: FSM States

## Objective
Implement all six game states that drive the game flow through the finite state machine.

---

## Action Items

### 4.1 SetupState (timed, 5s timeout)
- `OnEnter`: Increment `CurrentEliminationCycle` (0 -> 1 on first cycle), call `AssignRoles()` + `SelectWordPair()`, randomize clue order, set phase to `Setup`
- `Tick`: Auto-advance to `CluePhaseState` after `SetupPhaseTimeoutMs` (5s)
- `GetRemainingTime`: Returns countdown based on expiration set in `OnEnter`

### 4.2 CluePhaseState (timed)
- `OnEnter`: Advance `CurrentCluePlayerIndex` past eliminated players to first alive player. If none alive, transition to `GameOverState`.
- Tracks whose turn it is (skips eliminated players on advancement)
- `HandleCommand(SubmitClueCommand)`: Validate single word (no spaces), not the player's secret word, not previously used. Store clue, advance to next player. All alive players submitted -> transition to `DiscussionPhaseState`.
- `Tick`: If `EnableTimers`, auto-submit "..." for timed-out player and advance. If disabled, no auto-action.
- Known limitation: Synonym detection is enforced socially, not programmatically.

### 4.3 DiscussionPhaseState (timed)
- `HandleCommand(VoteToEndGameCommand)`: Any alive player, once per elimination cycle. Track in `EndGameVoteStatus`. Majority reached -> evaluate win conditions -> `GameOverState`.
- `HandleCommand(AdvanceToVoteCommand)`: Host only. Transitions to `VotePhaseState`.
- `Tick`: If `EnableTimers`, auto-advance on timeout. Otherwise, host must explicitly advance.

### 4.4 VotePhaseState (timed)
- `HandleCommand(CastVoteCommand)`: Validate not voting for self, target is alive. Mark as voted. All voted -> tally and transition to `RevealPhaseState`.
- `Tick`: If `EnableTimers`, abstain non-voters on timeout, tally, transition. Otherwise, waits for all votes.

### 4.5 RevealPhaseState (timed)
- `OnEnter`: If elimination occurred, reveal role. Call `ApplyCycleScoring()` (before clearing vote data). If eliminated player is Informant, set `AwaitingInformantGuess = true` and pause auto-advance. Otherwise, call `ResetEliminationCycleState()`.
- `HandleCommand(InformantGuessCommand)`: Only if `AwaitingInformantGuess == true` and sender is the eliminated Informant. Correct -> Informant wins -> `GameOverState`. Wrong -> set `AwaitingInformantGuess = false`, record result, continue. One attempt only.
- `Tick`: If `AwaitingInformantGuess`, use `InformantGuessTimeoutMs` (15s) -- timeout = forfeited. Otherwise, auto-advance to `CluePhaseState` on timeout (reveal is always timed). On transition out, call `ResetEliminationCycleState()`.

### 4.6 GameOverState
- `OnEnter`: Call `ApplyEndOfGameScoring()`. Accumulate scores into `GameScores`. Set phase to `GameOver`.
- `HandleCommand(StartNextGameCommand)`: Host only. If `CurrentGameNumber < TotalGames`, increment game number, reset per-game state (roles, words, cycles, clues, turn order, end-game votes, elimination/guess data). **Preserve** `UsedWordPairIndices` and `GameScores`. Transition to `SetupState`.
- `HandleCommand(ReturnToLobbyCommand)`: Host only. Returns to lobby.

---

## Acceptance Criteria
- [ ] `SetupState` assigns roles, selects word pair, increments cycle, and auto-advances after 5s
- [ ] `CluePhaseState` validates clues (no spaces, not secret word, not reused), advances turn order, and transitions after all clues submitted
- [ ] `CluePhaseState` skips eliminated players in turn order
- [ ] `DiscussionPhaseState` handles end-game voting (once per cycle per player) and host advance
- [ ] `VotePhaseState` prevents self-voting and voting for eliminated players, tallies correctly
- [ ] `RevealPhaseState` handles Informant guess flow (one attempt, timeout = forfeit)
- [ ] `RevealPhaseState` calls `ApplyCycleScoring()` before `ResetEliminationCycleState()`
- [ ] `GameOverState` handles multi-game progression and preserves cross-game state
- [ ] All timed states respect the `EnableTimers` flag (except reveal, which always auto-advances)
- [ ] Win condition evaluation follows priority: Informant > Insider > Agent
- [ ] `dotnet build` succeeds
