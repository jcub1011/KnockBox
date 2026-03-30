# Milestone 2: Data Models

## Objective
Define all enums, records, configuration, player state, and game state classes needed by the game logic.

---

## Action Items

### 2.1 Enums (in `ConsultTheCardGameState.cs`)
- `ConsultTheCardGamePhase`: `Setup`, `CluePhase`, `Discussion`, `Voting`, `Reveal`, `GameOver`
- `Role`: `Agent`, `Insider`, `Informant`

### 2.2 Records (in `ConsultTheCardGameState.cs`)
- `WordGroup(string[] Words)` -- thematic group of 2+ words; 2 selected at runtime
- `ClueEntry(string PlayerId, string PlayerName, string Clue)`
- `VoteEntry(string VoterId, string VoterName, string TargetId, string TargetName)`
- `EliminationResult(string PlayerId, string PlayerName, Role Role, bool WasTie)`
- `InformantGuessResult(string PlayerId, string PlayerName, string GuessedWord, bool WasCorrect)`
- `WinConditionResult(bool GameOver, Role? WinningTeam, string Reason)`
- `EndGameVoteStatus(HashSet<string> VotedToEnd, int RequiredVotes)` -- tracks Agent end-game votes

### 2.3 ConsultTheCardPlayerState (separate file: `Data/ConsultTheCardPlayerState.cs`)
Properties:
- `PlayerId`, `DisplayName`, `Role`, `SecretWord` (null for Informant)
- `IsEliminated`, `HasSubmittedClue`, `CurrentClue`, `VoteTargetId`, `HasVoted`
- `HasVotedToEndGame` -- one vote per elimination cycle per player
- `Score`, `PreviousClues` (list, prevents reuse across rounds)

### 2.4 ConsultTheCardGameConfig
- `SetupPhaseTimeoutMs` (5000), `CluePhaseTimeoutMs` (30000), `DiscussionPhaseTimeoutMs` (120000)
- `VotePhaseTimeoutMs` (15000), `RevealPhaseTimeoutMs` (10000), `InformantGuessTimeoutMs` (15000)
- `EnableTimers`, `TotalGames` (default 5)

### 2.5 ConsultTheCardGameState (extends `AbstractGameState`)
Properties:
- `Context`, `GamePhase`, `GamePlayers` (ConcurrentDictionary), `TurnOrder`
- `CurrentCluePlayerIndex`, `CurrentEliminationCycle` (int, init 0), `CurrentGameNumber` (int, init 1), `CurrentWordPair`
- `CurrentRoundClues`, `CurrentRoundVotes`
- `LastElimination`, `LastInformantGuess`, `AwaitingInformantGuess`, `WinResult`, `Config`
- `EndGameVoteStatus` -- tracking for "Agents vote to end" mechanic
- `GameScores` (Dictionary<string, int>) -- cumulative scores across games

---

## Acceptance Criteria
- [ ] All enums compile and have the correct values
- [ ] All records are defined with correct properties and types
- [ ] `ConsultTheCardPlayerState` has all required properties
- [ ] `ConsultTheCardGameConfig` has all timeout and configuration fields with correct defaults
- [ ] `ConsultTheCardGameState` extends `AbstractGameState` and has all required properties
- [ ] `dotnet build` succeeds for `KnockBox.ConsultTheCard`
