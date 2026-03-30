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
- `OnEnter`: Advance `CurrentCluePlayerIndex` past eliminated players to the next alive player (wraps around `TurnOrder`). This produces a **rotating start player** — each elimination cycle picks up from where the previous one left off. If none alive, transition to `GameOverState`.
- Tracks whose turn it is (skips eliminated players on advancement)
- `HandleCommand(SubmitClueCommand)`: Validate single word (no spaces), not the player's secret word, not previously used by **any player** in the current game (checked against game-level `UsedClues`). Store clue, add to `UsedClues`, advance to next player. All alive players submitted -> transition to `DiscussionPhaseState`.
- `Tick`: If `EnableTimers`, auto-submit "..." for timed-out player and advance. If disabled, no auto-action.
- Known limitation: Synonym detection is enforced socially, not programmatically.

### 4.3 DiscussionPhaseState (timed)
- `HandleCommand(VoteToEndGameCommand)`: Any alive player, once per elimination cycle. Track in `EndGameVoteStatus`. Majority reached -> evaluate win conditions -> `GameOverState`.
- `HandleCommand(AdvanceToVoteCommand)`: Host only (validate `command.PlayerId == context.State.Host.Id`). Transitions to `VotePhaseState`.
- `Tick`: If `EnableTimers`, auto-advance on timeout. Otherwise, host must explicitly advance.

### 4.4 VotePhaseState (timed)
- `HandleCommand(CastVoteCommand)`: Validate not voting for self, target is alive. Mark as voted. When all alive players have voted, call `TallyVotes()`. If a player has the most votes (no tie), mark them as `IsEliminated = true` and set `LastElimination`. If tie, set `LastElimination` with `WasTie = true`. Transition to `RevealPhaseState`.
- `Tick`: If `EnableTimers`, abstain non-voters on timeout, tally, transition. Otherwise, waits for all votes.

### 4.5 RevealPhaseState (timed)
- `OnEnter`: Call `ApplyCycleScoring()` (per-cycle penalties — applies regardless of whether elimination occurred or was a tie, since players still voted). Then:
  - **If elimination occurred** (not a tie): If the eliminated player is the **Informant**, set `AwaitingInformantGuess = true` and pause auto-advance (this is the only case where a role is revealed during gameplay — via the guess mechanic). If **not** the Informant, call `CheckWinConditions()` — if game should end (≤2 players remain), transition to `GameOverState`; otherwise call `ResetEliminationCycleState()`.
  - **If tie** (no elimination): Call `ResetEliminationCycleState()`. Will auto-advance to next `CluePhaseState` via Tick.
- Roles are **not** revealed during gameplay (except the Informant via the guess mechanic). All roles are revealed at game end.
- `HandleCommand(InformantGuessCommand)`: Only if `AwaitingInformantGuess == true` and sender is the eliminated Informant. Correct -> Informant wins -> `GameOverState`. Wrong -> set `AwaitingInformantGuess = false`, record result in `LastInformantGuess`, call `CheckWinConditions()` — if game should end, transition to `GameOverState`; otherwise call `ResetEliminationCycleState()` and continue to next cycle. One attempt only.
- `Tick`: If `AwaitingInformantGuess`, use `InformantGuessTimeoutMs` (15s) -- timeout = forfeited (same flow as wrong guess: check win conditions, reset cycle state). Otherwise, auto-advance to `CluePhaseState` on timeout (reveal is always timed).

### 4.6 GameOverState
- `OnEnter`: Call `ApplyEndOfGameScoring()`. Accumulate scores into `GameScores`. Set phase to `GameOver`.
- `HandleCommand(StartNextGameCommand)`: Host only (validate `command.PlayerId == context.State.Host.Id`). If `CurrentGameNumber < TotalGames`, increment game number and reset per-game state:
    - Clear on all players: `Role`, `SecretWord`, `IsEliminated`, `HasSubmittedClue`, `CurrentClue`, `VoteTargetId`, `HasVoted`, `HasVotedToEndGame`, `Score`
    - Reset game-level: `CurrentEliminationCycle` to 0, `CurrentCluePlayerIndex` to 0, `CurrentWordPair` to null, `CurrentRoundClues` (clear), `CurrentRoundVotes` (clear), `UsedClues` (clear)
    - Clear: `LastElimination`, `LastInformantGuess`, `AwaitingInformantGuess`, `WinResult`, `EndGameVoteStatus`
    - Re-randomize `TurnOrder`
    - **Preserve**: `UsedWordPairIndices` (avoid repeat word groups across games), `GameScores` (cumulative)
    - Transition to `SetupState`.
- `HandleCommand(ReturnToLobbyCommand)`: Host only (validate `command.PlayerId == context.State.Host.Id`). Returns to lobby.

---

## Acceptance Criteria
- [ ] `SetupState` assigns roles, selects word pair, increments cycle, and auto-advances after 5s
- [ ] `CluePhaseState` validates clues (no spaces, not secret word, not used by any player in current game via `UsedClues`), advances turn order, and transitions after all clues submitted
- [ ] `CluePhaseState` skips eliminated players in turn order with rotating start player (picks up from previous cycle's position)
- [ ] `DiscussionPhaseState` handles end-game voting (once per cycle per player) and host advance
- [ ] `VotePhaseState` prevents self-voting and voting for eliminated players, tallies correctly, marks eliminated player (`IsEliminated`, `LastElimination`)
- [ ] `RevealPhaseState` does NOT reveal roles (except Informant via guess mechanic)
- [ ] `RevealPhaseState` calls `CheckWinConditions()` after elimination (and after Informant guess resolves)
- [ ] `RevealPhaseState` handles Informant guess flow (one attempt, timeout = forfeit)
- [ ] `RevealPhaseState` handles tie flow correctly (scoring applied, cycle reset, no elimination)
- [ ] `RevealPhaseState` calls `ApplyCycleScoring()` before `ResetEliminationCycleState()`
- [ ] `GameOverState` handles multi-game progression and preserves cross-game state
- [ ] All timed states respect the `EnableTimers` flag (except reveal, which always auto-advances)
- [ ] Win condition evaluation follows priority: Informant > Insider > Agent
- [ ] Host-only commands validate sender via `command.PlayerId == context.State.Host.Id`
- [ ] `dotnet build` succeeds
