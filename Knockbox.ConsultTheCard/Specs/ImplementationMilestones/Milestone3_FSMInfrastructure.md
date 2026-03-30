# Milestone 3: FSM Infrastructure

## Objective
Build the finite state machine commands, type aliases, and game context with all helper methods needed by the FSM states.

---

## Action Items

### 3.1 Commands (`ConsultTheCardCommand.cs`)
Define the abstract base and all concrete command types:
```
ConsultTheCardCommand(string PlayerId)               -- abstract base
  SubmitClueCommand(PlayerId, string Clue)            -- CluePhase
  AdvanceToVoteCommand(PlayerId)                      -- Discussion (host only)
  VoteToEndGameCommand(PlayerId)                      -- Discussion (any player, once per cycle)
  CastVoteCommand(PlayerId, string TargetPlayerId)    -- Voting
  InformantGuessCommand(PlayerId, string GuessedWord) -- Reveal (Informant only)
  StartNextGameCommand(PlayerId)                      -- GameOver (host only)
  ReturnToLobbyCommand(PlayerId)                      -- GameOver (host only)
```

### 3.2 Type Aliases (`IConsultTheCardGameState.cs`)
- `IConsultTheCardGameState : IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>`
- `ITimedConsultTheCardGameState : ITimedGameState<...>, IConsultTheCardGameState`

### 3.3 Context (`ConsultTheCardGameContext.cs`)
References: `State`, `Rng`, `Logger`, `Fsm` (`IFiniteStateMachine<...>`), `UsedWordPairIndices` (HashSet)

Key helper methods:
- **`AssignRoles()`** -- Role distribution by player count: 4p=3A/1I, 5p=3A/1I/1Inf, 6p=4A/1I/1Inf, 7p=4A/2I/1Inf, 8p=5A/2I/1Inf. Shuffles players, assigns roles and words.
- **`SelectWordPair()`** -- Picks random unused `WordGroup`, selects 2 words from the group, randomly assigns Agent/Insider roles. Tracks used indices to avoid repeats across games.
- **`GetAlivePlayers()`** / **`GetAlivePlayerCount()`**
- **`TallyVotes()`** -- Returns player ID with most votes, or null on tie.
- **`CheckWinConditions()`** -- Auto-ends when <=2 players remain or majority voted to end. Evaluates: (1) Informant alive -> Informant wins, (2) Insider alive -> Insiders win, (3) Agents win. Returns `WinConditionResult`.
- **`ResetEliminationCycleState()`** -- Clears per-cycle clue/vote/end-game-vote data for all alive players.
- **`ApplyCycleScoring()`** -- Per-cycle: -1 for each player who voted for an Agent. Must be called **before** `ResetEliminationCycleState()`.
- **`ApplyEndOfGameScoring()`** -- End-of-game: +2 survived, +1 winning team, +3 Informant correct guess. Called once in `GameOverState.OnEnter`.

---

## Acceptance Criteria
- [ ] All 7 command types compile and inherit from `ConsultTheCardCommand`
- [ ] Type aliases correctly reference the generic state machine interfaces
- [ ] `ConsultTheCardGameContext` has all required references and helper methods
- [ ] `AssignRoles()` correctly distributes roles for player counts 4-8
- [ ] `SelectWordPair()` selects from unused groups and assigns words randomly
- [ ] `TallyVotes()` handles majority and tie cases
- [ ] `CheckWinConditions()` implements the correct priority logic (Informant > Insider > Agent)
- [ ] `CheckWinConditions()` does NOT auto-end when all Insiders/Informant are eliminated (only <=2 players or majority vote)
- [ ] Scoring helpers apply correct point values
- [ ] `dotnet build` succeeds
