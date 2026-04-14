# Milestone 2: Data Models

## Objective
Define all enums, records, configuration, player state, and game state classes needed by the game logic.

---

## Action Items

### 2.1 Enums (in `CodewordGameState.cs`)
- `CodewordGamePhase`: `Setup`, `CluePhase`, `Discussion`, `Voting`, `Reveal`, `GameOver`
- `Role`: `Agent`, `Insider`, `Informant`

### 2.2 Records (in `CodewordGameState.cs`)
- `WordGroup(string[] Words)` -- thematic group of 2+ words; 2 selected at runtime
- `ClueEntry(string PlayerId, string PlayerName, string Clue)`
- `VoteEntry(string VoterId, string VoterName, string TargetId, string TargetName)`
- `EliminationResult(string PlayerId, string PlayerName, Role Role, bool WasTie)`
- `InformantGuessResult(string PlayerId, string PlayerName, string GuessedWord, bool WasCorrect)`
- `WinConditionResult(bool GameOver, Role? WinningTeam, string Reason)`
- `EndGameVoteStatus(HashSet<string> VotedToEnd, int RequiredVotes)` -- tracks player votes to end the game

### 2.3 CodewordPlayerState (separate file: `Data/CodewordPlayerState.cs`)
Properties:
- `PlayerId`, `DisplayName`, `Role`, `SecretWord` (null for Informant)
- `IsEliminated`, `HasSubmittedClue`, `CurrentClue`, `VoteTargetId`, `HasVoted`
- `HasVotedToEndGame` -- one vote per elimination cycle per player
- `Score`

### 2.4 CodewordGameConfig
- `SetupPhaseTimeoutMs` (5000), `CluePhaseTimeoutMs` (30000), `DiscussionPhaseTimeoutMs` (120000)
- `VotePhaseTimeoutMs` (15000), `RevealPhaseTimeoutMs` (10000), `InformantGuessTimeoutMs` (15000)
- `EnableTimers`, `TotalGames` (default 5)

### 2.5 CodewordGameState (extends `AbstractGameState`)
Properties:
- `Context`, `GamePhase`, `GamePlayers` (ConcurrentDictionary), `TurnOrder`
- `CurrentCluePlayerIndex`, `CurrentEliminationCycle` (int, init 0), `CurrentGameNumber` (int, init 1), `CurrentWordPair`
- `CurrentRoundClues`, `CurrentRoundVotes`
- `LastElimination`, `LastInformantGuess`, `AwaitingInformantGuess`, `WinResult`, `Config`
- `EndGameVoteStatus` -- tracking for "vote to end game" mechanic
- `UsedClues` (HashSet<string>) -- all clue words used by any player in the current game (prevents reuse across players and cycles)
- `GameScores` (Dictionary<string, int>) -- cumulative scores across games

---

## Acceptance Criteria
- [ ] All enums compile and have the correct values
- [ ] All records are defined with correct properties and types
- [ ] `CodewordPlayerState` has all required properties
- [ ] `CodewordGameConfig` has all timeout and configuration fields with correct defaults
- [ ] `CodewordGameState` extends `AbstractGameState` and has all required properties
- [ ] `dotnet build` succeeds for `KnockBox.Codeword`
