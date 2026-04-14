# Phase 6: Round Lifecycle (Final Guesses, Reveal, Scoring, Multi-Round)

## Goal

Implement the complete round-end flow: final guess opportunity for players who haven't guessed, task reveal, scoring, and reset for the next round. After this phase, the full game loop works from start to finish across multiple rounds.

## Prerequisites

- Phase 3 (turn mechanics) -- full turn loop
- Phase 4 (task evaluation) -- `EvaluateTaskCompletion` for all 31 tasks
- Phase 5 (guess system) -- guess submission, countdown, round-end triggers
- `FinalGuessState` placeholder from Phase 3 to be replaced

---

## Game Design Context (from GDD)

### Round End Flow
1. A round-end trigger fires (collection trigger, guess countdown, or max turns)
2. **Final Guesses** -- any player who has not yet submitted guesses gets one final opportunity (timed)
3. **Reveal** -- all secret tasks are revealed, guess accuracy shown
4. **Scoring** -- task completion points (+1 to +3 by difficulty) + correct guess points (+1 each), added to cumulative scores
5. **Round Over** -- show cumulative scoreboard, host starts next round
6. **Reset** -- collections to 0, task pool rotates, new round begins
7. After final round -> **Match Over** -- winner announced

### Scoring Rules
- Task completion: +1 (Devotion/Easy), +2 (Style,Movement/Medium), +3 (Neglect,Rivalry/Hard)
- Each correct guess of an opponent's task: +1 (max 3 per opponent)
- Wrong guesses: 0 (no penalty)
- Players who didn't guess: 0 guess points

### Multi-Round
- 3-5 rounds configurable (default 4)
- Scores accumulate across rounds
- Between rounds: task pool rotates per config (Full/Partial/Fixed)
- Player with highest cumulative score after final round wins

---

## Files to Create / Modify

### 1. Replace `FSM/States/FinalGuessState.cs` (Phase 3 placeholder)

Timed state giving non-guessing players a final chance.

```csharp
public sealed class FinalGuessState : ITimedHiddenAgendaGameState
{
    private DateTimeOffset _expiresAt;

    public ValueResult<...?> OnEnter(HiddenAgendaGameContext context)
    {
        // Check if anyone still needs to guess
        bool anyoneNeedsToGuess = context.GamePlayers.Values
            .Any(p => !p.HasSubmittedGuess);

        if (!anyoneNeedsToGuess)
        {
            // Everyone already guessed, skip straight to Reveal
            return new RevealState();
        }

        context.State.SetPhase(GamePhase.FinalGuess);
        _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
            context.State.Config.FinalGuessTimeoutMs);
        return null;
    }

    public ValueResult<...?> HandleCommand(
        HiddenAgendaGameContext context, HiddenAgendaCommand command)
    {
        switch (command)
        {
            case SubmitFinalGuessCommand cmd:
            {
                var player = context.GamePlayers.GetValueOrDefault(cmd.PlayerId);
                if (player == null)
                    return new ResultError("Player not found.");
                if (player.HasSubmittedGuess)
                    return new ResultError("You have already submitted guesses.");

                // Validate guess format (same validation as GuessPhaseState)
                var error = ValidateGuesses(context, cmd.PlayerId, cmd.Guesses);
                if (error != null)
                    return new ResultError(error);

                player.HasSubmittedGuess = true;
                player.GuessSubmission = cmd.Guesses;

                // Check if all players have now submitted
                if (context.GamePlayers.Values.All(p => p.HasSubmittedGuess))
                    return new RevealState();

                return null; // Stay in FinalGuess, waiting for others
            }

            case SkipFinalGuessCommand cmd:
            {
                var player = context.GamePlayers.GetValueOrDefault(cmd.PlayerId);
                if (player == null)
                    return new ResultError("Player not found.");

                // Mark as "skipped" so we don't wait for them
                player.HasSubmittedGuess = true;
                // GuessSubmission stays null -> 0 guess points

                if (context.GamePlayers.Values.All(p => p.HasSubmittedGuess))
                    return new RevealState();

                return null;
            }

            default:
                return null;
        }
    }

    public ValueResult<...?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
    {
        if (now < _expiresAt) return null;

        // Timeout: mark all non-guessing players as skipped
        foreach (var player in context.GamePlayers.Values)
        {
            if (!player.HasSubmittedGuess)
                player.HasSubmittedGuess = true; // No guess submission = 0 points
        }

        return new RevealState();
    }

    // GetRemainingTime, OnExit...

    // Reuse validation logic from GuessPhaseState (consider extracting to Context or static helper)
    private static string? ValidateGuesses(...) { /* same as GuessPhaseState */ }
}
```

### 2. Create `FSM/States/RevealState.cs`

Timed state that evaluates all tasks, scores the round, and displays results.

```csharp
public sealed class RevealState : ITimedHiddenAgendaGameState
{
    private DateTimeOffset _expiresAt;

    public ValueResult<...?> OnEnter(HiddenAgendaGameContext context)
    {
        // Evaluate all tasks for all players
        var roundResult = context.ScoreRound();
        context.State.RoundResults.Add(roundResult);

        context.State.SetPhase(GamePhase.Reveal);
        _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
            context.State.Config.RevealTimeoutMs);
        return null;
    }

    public Result OnExit(HiddenAgendaGameContext context) => Result.Success;
    public ValueResult<...?> HandleCommand(...) => null; // No commands during reveal

    public ValueResult<...?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
    {
        if (now < _expiresAt) return null;
        return new RoundOverState();
    }

    public ValueResult<TimeSpan> GetRemainingTime(...) => _expiresAt - now;
}
```

### 3. Create `FSM/States/RoundOverState.cs`

Untimed state (host-driven). Shows cumulative scoreboard and waits for host to start next round.

```csharp
public sealed class RoundOverState : IHiddenAgendaGameState
{
    public ValueResult<...?> OnEnter(HiddenAgendaGameContext context)
    {
        // Accumulate round scores into cumulative scores
        foreach (var player in context.GamePlayers.Values)
        {
            player.CumulativeScore += player.RoundScore;
        }

        context.State.SetPhase(GamePhase.RoundOver);
        return null;
    }

    public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

    public ValueResult<...?> HandleCommand(
        HiddenAgendaGameContext context, HiddenAgendaCommand command)
    {
        if (command is not StartNextRoundCommand cmd) return null;

        // Host-only
        if (cmd.PlayerId != context.State.Host.Id)
            return new ResultError("Only the host can start the next round.");

        // Check if match is over
        if (context.State.CurrentRound >= context.State.Config.TotalRounds)
            return new MatchOverState();

        // Reset for new round and start
        context.ResetForNewRound();
        return new RoundSetupState();
    }
}
```

### 4. Create `FSM/States/MatchOverState.cs`

Terminal state showing final results and winner.

```csharp
public sealed class MatchOverState : IHiddenAgendaGameState
{
    public ValueResult<...?> OnEnter(HiddenAgendaGameContext context)
    {
        // Determine winner with tiebreakers:
        // 1. Highest cumulative score
        // 2. Most total correct guesses across all rounds
        // 3. Most total tasks completed across all rounds
        var ranked = context.GamePlayers.Values
            .OrderByDescending(p => p.CumulativeScore)
            .ThenByDescending(p => context.State.RoundResults
                .Sum(r => r.PlayerResults.GetValueOrDefault(p.PlayerId)?.GuessPoints ?? 0))
            .ThenByDescending(p => context.State.RoundResults
                .Sum(r => r.PlayerResults.GetValueOrDefault(p.PlayerId)?.TaskResults
                    .Count(t => t.Completed) ?? 0))
            .ToList();

        context.State.MatchWinner = ranked.First().PlayerId;
        context.State.SetPhase(GamePhase.MatchOver);
        return null;
    }

    public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

    public ValueResult<...?> HandleCommand(
        HiddenAgendaGameContext context, HiddenAgendaCommand command)
    {
        switch (command)
        {
            case ReturnToLobbyCommand cmd:
            {
                if (cmd.PlayerId != context.State.Host.Id)
                    return new ResultError("Only the host can return to lobby.");
                // Signal to the engine that the match is done by setting phase to Lobby.
                // Do NOT call Dispose() here -- we are inside the Execute lock and
                // disposing the state (which disposes the semaphore) would cause issues.
                // The engine's ReturnToLobby method checks for this phase after Execute
                // completes and disposes outside the lock.
                context.State.SetPhase(GamePhase.Lobby);
                context.State.UpdateJoinableStatus(false);
                return null;
            }

            case PlayAgainCommand cmd:
            {
                if (cmd.PlayerId != context.State.Host.Id)
                    return new ResultError("Only the host can start a new match.");

                // Full reset: clear all scores, round results, round counter
                context.State.CurrentRound = 0;
                context.State.RoundResults.Clear();
                context.State.MatchWinner = null;
                foreach (var player in context.GamePlayers.Values)
                {
                    player.CumulativeScore = 0;
                }
                context.ResetForNewRound();
                return new RoundSetupState();
            }

            default:
                return null;
        }
    }
}
```

### 5. Add scoring to `HiddenAgendaGameContext.cs`

```csharp
/// <summary>
/// Evaluates all tasks and guesses, computes round scores, returns a RoundResult.
/// </summary>
public RoundResult ScoreRound()
{
    var playerResults = new Dictionary<string, PlayerRoundResult>();

    foreach (var player in GamePlayers.Values)
    {
        // Evaluate task completion
        var taskResults = new List<TaskResult>();
        int taskPoints = 0;
        foreach (var task in player.SecretTasks)
        {
            bool completed = EvaluateTaskCompletion(player.PlayerId, task);
            taskResults.Add(new TaskResult(task, completed));
            if (completed)
                taskPoints += task.PointValue;
        }

        // Evaluate guess accuracy
        int guessPoints = 0;
        if (player.GuessSubmission is not null)
        {
            foreach (var (opponentId, guessedTaskIds) in player.GuessSubmission)
            {
                if (GamePlayers.TryGetValue(opponentId, out var opponent))
                {
                    var actualTaskIds = opponent.SecretTasks
                        .Select(t => t.Id).ToHashSet();
                    foreach (var guessedId in guessedTaskIds)
                    {
                        if (actualTaskIds.Contains(guessedId))
                            guessPoints++;
                    }
                }
            }
        }

        int totalRoundPoints = taskPoints + guessPoints;
        player.RoundScore = totalRoundPoints;

        playerResults[player.PlayerId] = new PlayerRoundResult(
            player.PlayerId,
            player.DisplayName,
            taskResults,
            taskPoints,
            guessPoints,
            totalRoundPoints);
    }

    return new RoundResult(State.CurrentRound, playerResults);
}

/// <summary>
/// Extracts guess validation into a shared helper usable by both GuessPhaseState and FinalGuessState.
/// </summary>
public string? ValidateGuessSubmission(string playerId, Dictionary<string, List<string>> guesses)
{
    var opponents = GamePlayers.Keys.Where(id => id != playerId).ToHashSet();

    if (guesses.Count != opponents.Count)
        return $"Must guess for all {opponents.Count} opponents.";

    var poolIds = State.CurrentTaskPool.Select(t => t.Id).ToHashSet();

    foreach (var (opponentId, taskIds) in guesses)
    {
        if (!opponents.Contains(opponentId))
            return $"Invalid opponent ID: {opponentId}";
        if (taskIds.Count != 3)
            return "Must guess exactly 3 tasks for each opponent.";
        if (taskIds.Distinct().Count() != 3)
            return "Duplicate task IDs in guess.";
        foreach (var taskId in taskIds)
        {
            if (!poolIds.Contains(taskId))
                return $"Task '{taskId}' is not in the current dossier.";
        }
    }
    return null;
}
```

### 6. Add `RoundResult` and `PlayerRoundResult` records

These may already exist from Phase 1's state expansion. If not, add to `HiddenAgendaGameState.cs`:

```csharp
public record RoundResult(int RoundNumber, Dictionary<string, PlayerRoundResult> PlayerResults);

public record PlayerRoundResult(
    string PlayerId,
    string DisplayName,
    List<TaskResult> TaskResults,
    int TaskPoints,
    int GuessPoints,
    int TotalRoundPoints);

public record TaskResult(SecretTask Task, bool Completed);
```

### 7. Update `HiddenAgendaGameState.cs`

Add properties if not already present:
```csharp
public List<RoundResult> RoundResults { get; } = [];
public string? MatchWinner { get; set; }
```

### 8. Update `HiddenAgendaGameEngine.cs`

Add handlers for ReturnToLobby and PlayAgain:
```csharp
public Result ReturnToLobby(User player, HiddenAgendaGameState state)
{
    if (!TryGetContext(state, out var ctx, out var err)) return err;
    var result = ProcessCommand(ctx, new ReturnToLobbyCommand(player.Id));
    // Dispose outside the Execute lock -- the command sets phase to Lobby as a signal
    if (result.IsSuccess && state.Phase == GamePhase.Lobby)
        state.Dispose();
    return result;
}

public Result PlayAgain(User player, HiddenAgendaGameState state)
{
    if (!TryGetContext(state, out var ctx, out var err)) return err;
    return ProcessCommand(ctx, new PlayAgainCommand(player.Id));
}

public Result StartNextRound(User player, HiddenAgendaGameState state)
{
    if (!TryGetContext(state, out var ctx, out var err)) return err;
    return ProcessCommand(ctx, new StartNextRoundCommand(player.Id));
}
```

---

## Tests

### `Unit/Logic/Games/HiddenAgenda/States/FinalGuessStateTests.cs`

```
- OnEnter with no non-guessers -> chain-transition to RevealState
- OnEnter with non-guessers -> sets phase to FinalGuess, stays
- SubmitFinalGuessCommand stores guesses and marks submitted
- After all players submit -> transitions to RevealState
- SkipFinalGuessCommand marks player as submitted (no guesses)
- Invalid guess format returns error
- Already-guessed player returns error
- Timeout marks all remaining as skipped, transitions to RevealState
```

### `Unit/Logic/Games/HiddenAgenda/States/RevealStateTests.cs`

```
- OnEnter calls ScoreRound and stores result
- OnEnter sets phase to Reveal
- Tick transitions to RoundOverState after timeout
- No commands accepted
```

### `Unit/Logic/Games/HiddenAgenda/States/RoundOverStateTests.cs`

```
- OnEnter accumulates round scores into cumulative scores
- OnEnter sets phase to RoundOver
- StartNextRoundCommand from host resets and transitions to RoundSetupState
- StartNextRoundCommand from non-host returns error
- After final round, StartNextRoundCommand transitions to MatchOverState
```

### `Unit/Logic/Games/HiddenAgenda/States/MatchOverStateTests.cs`

```
- OnEnter determines winner (highest cumulative score)
- OnEnter sets phase to MatchOver
- ReturnToLobbyCommand from host disposes state
- ReturnToLobbyCommand from non-host returns error
- PlayAgainCommand from host resets everything and starts new match
- PlayAgainCommand resets cumulative scores to 0
```

### `Unit/Logic/Games/HiddenAgenda/HiddenAgendaGameContextTests.cs` (update)

```
ScoreRound:
- All tasks completed: correct task points by difficulty
- No tasks completed: 0 task points
- Perfect guesses: +1 per correct guess (max 3 per opponent)
- No guesses submitted: 0 guess points
- Mixed scenario: partial task completion + partial guess accuracy
- Round score = task points + guess points

ValidateGuessSubmission:
- Valid submission returns null
- Missing opponent returns error
- Extra opponent returns error
- Wrong task count returns error
- Task not in dossier returns error
```

---

## Verification

1. `dotnet build` compiles
2. All tests pass
3. Complete game loop works:
   - Lobby -> RoundSetup -> turns with guessing -> round end trigger -> FinalGuess -> Reveal -> RoundOver -> next round
   - After final round -> MatchOver -> PlayAgain or ReturnToLobby
4. Scoring is correct:
   - Task completion points match difficulty (1/2/3)
   - Correct guesses earn +1 each
   - Wrong guesses earn 0
   - Cumulative scores accumulate correctly
5. Multi-round works: 3-5 rounds, scores carry over, task pool rotates
6. PlayAgain resets everything for a new match
