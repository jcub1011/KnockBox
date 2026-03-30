# Consult The Card - Implementation Plan

## Context

The `Knockbox.ConsultTheCard` project exists as a stub (empty `Class1.cs`). The game design document at `Specs/GameDesignDocument.md` fully specifies a social deduction party game for 4-8 players. This plan implements the full game: backend logic, Blazor UI, unit tests, and integration wiring — all following the CardCounter architecture pattern. Power Cards are excluded per the design doc.

---

## File Structure

```
Knockbox.ConsultTheCard/
  Services/
    Logic/Games/ConsultTheCard/
      ConsultTheCardGameEngine.cs
      Data/
        WordBank.cs
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
  DiscussionPhase.razor                  # Chat/discussion, guess button, advance button
  DiscussionPhase.razor.cs
  VotePhase.razor                        # Player selection grid for simultaneous voting
  VotePhase.razor.cs
  RevealPhase.razor                      # Eliminated player's role reveal + round summary
  RevealPhase.razor.cs
  GameOverPhase.razor                    # Final scores, winner, replay options
  GameOverPhase.razor.cs

Knockbox.ConsultTheCardTests/
  Knockbox.ConsultTheCardTests.csproj
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

## Phase 1: Project Scaffolding

1. Delete `Knockbox.ConsultTheCard/Class1.cs`
2. Update `Knockbox.ConsultTheCard.csproj`:
   - Add `<ProjectReference>` to `KnockBox.Core`
   - Add `<Using Include="Microsoft.Extensions.Logging" />`
   - Add `InternalsVisibleTo` for `Knockbox.ConsultTheCardTests`
3. Create `Knockbox.ConsultTheCardTests/` project:
   - .NET 10.0, MSTest (`Microsoft.VisualStudio.TestTools.UnitTesting`), Moq
   - References: `Knockbox.ConsultTheCard`, `KnockBox.Core`
   - Method-level parallelization via `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`
   - Add to solution file

---

## Phase 2: Data Models

### Enums (in `ConsultTheCardGameState.cs`)
- `ConsultTheCardGamePhase`: `Setup`, `CluePhase`, `Discussion`, `Voting`, `Reveal`, `GameOver`
- `Role`: `Agent`, `Insider`, `Informant`

### Records (in `ConsultTheCardGameState.cs`)
- `WordPair(string AgentWord, string InsiderWord)`
- `ClueEntry(string PlayerId, string PlayerName, string Clue)`
- `VoteEntry(string VoterId, string VoterName, string TargetId, string TargetName)`
- `EliminationResult(string PlayerId, string PlayerName, Role Role, bool WasTie)`
- `InformantGuessResult(string PlayerId, string PlayerName, string GuessedWord, bool WasCorrect)`
- `WinConditionResult(bool GameOver, Role? WinningTeam, string Reason)`

### ConsultTheCardPlayerState (separate file)
- `PlayerId`, `DisplayName`, `Role`, `SecretWord` (null for Informant)
- `IsEliminated`, `HasSubmittedClue`, `CurrentClue`, `VoteTargetId`, `HasVoted`
- `Score`, `PreviousClues` (list, prevents reuse across rounds)

### ConsultTheCardGameConfig
- `CluePhaseTimeoutMs` (30s), `DiscussionPhaseTimeoutMs` (120s), `VotePhaseTimeoutMs` (15s), `RevealPhaseTimeoutMs` (10s)
- `EnableTimers`, `TotalRounds` (for multi-round scoring, default 5)

### ConsultTheCardGameState (extends AbstractGameState)
- `Context`, `GamePhase`, `GamePlayers` (ConcurrentDictionary), `TurnOrder`
- `CurrentCluePlayerIndex`, `CurrentRound`, `CurrentWordPair`
- `CurrentRoundClues`, `CurrentRoundVotes`
- `LastElimination`, `LastInformantGuess`, `WinResult`, `Config`

---

## Phase 3: FSM Infrastructure

### Commands (`ConsultTheCardCommand.cs`)
```
ConsultTheCardCommand(string PlayerId)               # abstract base
├── SubmitClueCommand(PlayerId, string Clue)          # CluePhase
├── InformantGuessCommand(PlayerId, string GuessedWord) # Discussion
├── AdvanceToVoteCommand(PlayerId)                    # Discussion (host only)
├── CastVoteCommand(PlayerId, string TargetPlayerId)  # Voting
└── StartNewRoundCommand(PlayerId)                    # GameOver (host only)
```

### Type Aliases (`IConsultTheCardGameState.cs`)
- `IConsultTheCardGameState : IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>`
- `ITimedConsultTheCardGameState : ITimedGameState<...>, IConsultTheCardGameState`

### Context (`ConsultTheCardGameContext.cs`)
References: `State`, `Rng`, `Logger`, `Fsm`, `UsedWordPairIndices` (HashSet)

Key helpers:
- **`AssignRoles()`** — Uses scaling table: 4p=3A/1I, 5p=3A/1I/1Inf, 6p=4A/1I/1Inf, 7p=4A/2I/1Inf, 8p=5A/2I/1Inf. Shuffles players randomly, assigns roles and words.
- **`SelectWordPair()`** — Picks random unused `WordPair` from `WordBank`
- **`GetAlivePlayers()`** / **`GetAlivePlayerCount()`**
- **`TallyVotes()`** — Returns player ID with most votes, or null on tie
- **`CheckWinConditions()`** — Agents win if no Insiders/Informant alive; Insiders win if <=2 alive; otherwise continue
- **`ResetRoundState()`** — Clears per-round clue/vote data for all alive players
- **`ApplyScoring()`** — Per design doc: +2 survived, +1 winning team, +3 Informant correct guess, -1 voted for Agent

---

## Phase 4: FSM States

### SetupState (timed)
- `OnEnter`: Increment round, call `AssignRoles()` + `SelectWordPair()`, randomize clue order, set phase
- `Tick`: Auto-advance to `CluePhaseState` after brief delay

### CluePhaseState (timed)
- Tracks whose turn it is to give a clue (skips eliminated players)
- `HandleCommand(SubmitClueCommand)`: Validate single word, not secret word, not previously used. Store clue, advance to next player. When all alive players have submitted, transition to `DiscussionPhaseState`.
- `Tick`: Auto-submit "..." for timed-out player, advance

### DiscussionPhaseState (timed)
- `HandleCommand(InformantGuessCommand)`: Any player can attempt. If guesser is Informant and correct: Informant wins, → `GameOverState`. If Informant and wrong: eliminate them, check win conditions, → `RevealPhaseState` or `GameOverState`. If not Informant: reject silently.
- `HandleCommand(AdvanceToVoteCommand)`: Host only. Transitions to `VotePhaseState`.
- `Tick`: Auto-advance to `VotePhaseState` on timeout

### VotePhaseState (timed)
- `HandleCommand(CastVoteCommand)`: Validate not voting for self, target is alive. Mark as voted. When all alive players have voted, tally and transition to `RevealPhaseState`.
- `Tick`: Abstain for non-voters on timeout, tally, transition

### RevealPhaseState (timed)
- `OnEnter`: If elimination occurred, reveal role. Apply scoring. Check win conditions. If game over → chain to `GameOverState`. Otherwise, reset round state.
- `Tick`: Transition to `CluePhaseState` (next round) or `GameOverState` on timeout

### GameOverState
- `OnEnter`: Calculate final scores, set phase to `GameOver`
- `HandleCommand(StartNewRoundCommand)`: Host resets game for another session

---

## Phase 5: Engine

`ConsultTheCardGameEngine` — singleton, extends `AbstractGameEngine`:
- Min 4 / Max 8 players
- `CreateStateAsync()`, `StartAsync()` — follow CardCounter pattern exactly
- `ProcessCommand()`, `Tick()` — delegate to FSM inside execute lock
- Public UI methods: `SubmitClue()`, `CastVote()`, `InformantGuess()`, `AdvanceToVote()`, `StartNewRound()`, `ReturnToLobby()`, `ResetGame()`
- `HandlePlayerLeft()`: Remove from turn order, adjust indices, check win conditions, auto-advance if needed

---

## Phase 6: Integration (3 files in KnockBox project)

1. **`KnockBox/Services/Navigation/Games/GameTypes.cs`** — Add `ConsultTheCard` enum variant with `[Description("Consult The Card")]` and `[NavigationString("consult-the-card")]`
2. **`KnockBox/Services/Registrations/Logic/LogicRegistrations.cs`** — Add `services.AddSingleton<ConsultTheCardGameEngine>()`
3. **`KnockBox/Services/Logic/Games/Shared/LobbyService.cs`** — Add `GameType.ConsultTheCard => serviceProvider.GetService<ConsultTheCardGameEngine>()` to switch

---

## Phase 7: Word Bank

Populate `WordBank.cs` with 50-100 word pairs following the design philosophy: "thematically adjacent but distinct." Examples from design doc: Ocean/Lake, Guitar/Violin, Castle/Fortress, Sunrise/Sunset, Astronaut/Pilot.

---

## Phase 8: Blazor UI

Following the CardCounter UI pattern exactly: one main lobby page with phase-specific child components.

### ConsultTheCardLobby (main page)
- **Route**: `@page "/room/consult-the-card/{ObfuscatedRoomCode}"`
- **Extends**: `DisposableComponent`
- **Injects**: `ConsultTheCardGameEngine`, `IGameSessionService`, `INavigationService`, `IUserService`, `ITickService`
- **OnInitializedAsync**: Initialize user, get session, cast state to `ConsultTheCardGameState`, subscribe to `StateChangedEventManager`
- **Host tick**: Register tick callback via `TickService.RegisterTickCallback()` calling `GameEngine.Tick()` once per second
- **Phase switching**: Render child component based on `GameState.GamePhase` and `GameState.IsJoinable`
- **Timer display**: If current FSM state implements `ITimedConsultTheCardGameState`, show countdown
- **Kicked player detection**: Check `GameState.KickedPlayers` in `OnAfterRender`
- **Dispose**: Unsubscribe state change listener, dispose tick registration

### LobbyPhase
- Player list with host badge, kick buttons (host only)
- Host settings drawer: timer toggles, timeout durations, total rounds
- Start button (host only, enabled when 4-8 players)
- Calls `GameEngine.StartAsync()`

### SetupPhase
- Shows the player their secret word in a large card-like display
- Informant sees a blank/special "???" card
- Brief phase — auto-advances via timer
- No player actions needed (purely informational)

### CluePhase
- Shows whose turn it is to give a clue (highlighted player)
- Active player sees a text input + submit button
- Non-active players see a waiting indicator
- Clue history: list of submitted clues for this round (player name + clue word)
- Previously used clues shown as disabled hints
- Calls `GameEngine.SubmitClue()`

### DiscussionPhase
- All submitted clues displayed prominently in a grid/list
- Player list with role-unknown indicators
- "Guess the Agents' Word" button — opens a text input for any player to attempt
- Host "Advance to Vote" button to end discussion early
- Timer countdown shown
- Calls `GameEngine.InformantGuess()` or `GameEngine.AdvanceToVote()`
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
- Score changes for this round
- Auto-advances via timer to next CluePhase or GameOver

### GameOverPhase
- Winner announcement (team or Informant)
- Final scoreboard: all players ranked by score with role revealed
- Scoring breakdown per player
- Host buttons: "Return to Lobby" or "Play Again"
- Calls `GameEngine.ReturnToLobby()` or `GameEngine.ResetGame()`

---

## Phase 9: Unit Tests

### Test Project: `Knockbox.ConsultTheCardTests`
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
- `SelectWordPair` returns unused pairs, handles exhaustion
- `TallyVotes` returns correct winner, handles ties
- `CheckWinConditions` — all Insiders+Informant eliminated → Agents win
- `CheckWinConditions` — 2 players remain → Insiders win
- `CheckWinConditions` — game continues when neither met
- `ResetRoundState` clears per-round data
- `GetAlivePlayers` excludes eliminated players

**ConsultTheCardGameEngineTests.cs**
- `CreateStateAsync` returns valid state
- `StartAsync` fails if not host, fails if <4 players
- `StartAsync` succeeds and transitions to SetupState
- `SubmitClue` before game start returns error
- `ReturnToLobby` only works for host after GameOver
- `ResetGame` reinitializes state

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
- `Tick` auto-advances to CluePhaseState

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
- Informant guesses incorrectly → eliminated, check win conditions
- Non-Informant guess → rejected (no-op)
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
- Scoring applied correctly after elimination
- Tick auto-advances

**GameOverStateTests.cs**
- `OnEnter` calculates final scores
- `StartNewRoundCommand` resets and restarts

---

## Key Design Decisions

1. **Players don't know their role** — they see their word (or blank) but not whether it's the Agent or Insider word. Role is only revealed on elimination.
2. **Informant guess** — any player can attempt it during Discussion. Only the actual Informant gets the win; non-Informants are silently rejected (the command is a no-op). This preserves the secret that you don't know who the Informant is.
3. **Tie votes** — no elimination, new clue round begins.
4. **Player disconnect** — counts as elimination for win-condition purposes.

---

## Verification

1. `dotnet build` — solution builds successfully
2. `dotnet test` — all unit tests pass
3. Manual: create lobby, join 4+ players, play through full game flow
4. Verify integration: `LobbyService` can create ConsultTheCard lobby, players can join via lobby code

---

## Critical Files to Modify (existing)
- `Knockbox.ConsultTheCard/Knockbox.ConsultTheCard.csproj`
- `KnockBox/Services/Navigation/Games/GameTypes.cs`
- `KnockBox/Services/Registrations/Logic/LogicRegistrations.cs`
- `KnockBox/Services/Logic/Games/Shared/LobbyService.cs`
- `KnockBox.slnx` (add test project)

## Critical Files to Reference (patterns)
- `KnockBox.CardCounter/Services/Logic/Games/CardCounter/CardCounterGameEngine.cs`
- `KnockBox.CardCounter/Services/State/Games/CardCounter/CardCounterGameState.cs`
- `KnockBox.CardCounter/Services/Logic/Games/CardCounter/FSM/CardCounterGameContext.cs`
- `KnockBox.Core/Services/Logic/Games/Engines/Shared/AbstractGameEngine.cs`
- `KnockBox.Core/Services/State/Games/Shared/IFiniteStateMachine.cs`
- `KnockBox/Components/Pages/Games/CardCounter/CardCounterLobby.razor.cs` (UI pattern)
- `KnockBox/Components/Shared/DisposableComponent.cs` (base class)
- `KnockBox.CardCounterTests/KnockBox.CardCounterTests.csproj` (test project setup)
