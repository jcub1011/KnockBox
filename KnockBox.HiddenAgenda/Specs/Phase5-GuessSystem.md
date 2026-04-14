# Phase 5: Guess System (Submission, Countdown, Validation)

## Goal

Implement the guess submission mechanic and the countdown trigger. Players can submit guesses for all opponents' tasks as a free action during their turn. The first guess triggers a 2-turn countdown for all other players. After this phase, guessing works and all three round-end conditions (collection trigger, guess countdown, max turns) integrate correctly.

## Prerequisites

- Phase 3 (turn loop) must be complete -- DrawPhaseState.FinishTurn currently transitions to a GuessPhaseState placeholder.
- Phase 4 (task system) should be complete -- task definitions are needed for guess validation (guessed task IDs must exist in the dossier).
- Phase 1 data models: `SecretTask`, `TaskDefinitions`
- Phase 2 infrastructure: `HiddenAgendaCommand` (SubmitGuessCommand, SkipGuessCommand), `HiddenAgendaGameContext`, `RoundEndTrigger`

---

## Game Design Context

From the GDD:

- **Once per round**, a player may submit guesses for every other player's tasks. This is a **free action** -- it doesn't cost a turn.
- For each opponent, the player guesses all 3 of that opponent's tasks from the public dossier.
- This is a single, all-or-nothing submission. Once submitted, cannot be revised.
- **First guess triggers countdown:** All other players get exactly 2 more turns before the round ends.
- Guesses are revealed at round end, not immediately.
- If a player never submits guesses, they earn 0 guess points for that round.
- Guessing early is a strategic weapon (forces others to guess under pressure) but risky (less data).

---

## Files to Modify / Create

### 1. Replace `FSM/States/GuessPhaseState.cs` (Phase 3 placeholder)

This is an **optional** state entered after the current player's DrawPhase completes (via FinishTurn). It is skipped if the player has already submitted guesses.

```csharp
public sealed class GuessPhaseState : ITimedHiddenAgendaGameState
{
    private DateTimeOffset _expiresAt;

    public ValueResult<...?> OnEnter(HiddenAgendaGameContext context)
    {
        var currentPlayerId = context.State.TurnManager.CurrentPlayer;
        if (currentPlayerId == null)
            return AdvanceToNextPlayer(context);

        var player = context.GamePlayers[currentPlayerId];

        // If player already guessed, skip
        if (player.HasSubmittedGuess)
            return AdvanceToNextPlayer(context);

        context.State.SetPhase(GamePhase.GuessPhase);
        _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
            context.State.Config.GuessPhaseTimeoutMs);
        return null;
    }

    public ValueResult<...?> HandleCommand(
        HiddenAgendaGameContext context, HiddenAgendaCommand command)
    {
        var currentPlayerId = context.State.TurnManager.CurrentPlayer;

        switch (command)
        {
            case SubmitGuessCommand cmd:
            {
                if (cmd.PlayerId != currentPlayerId)
                    return new ResultError("It is not your turn.");

                var player = context.GamePlayers[cmd.PlayerId];
                if (player.HasSubmittedGuess)
                    return new ResultError("You have already submitted guesses this round.");

                // Validate guess format
                var validationError = ValidateGuesses(context, cmd.PlayerId, cmd.Guesses);
                if (validationError != null)
                    return new ResultError(validationError);

                // Store guesses
                player.HasSubmittedGuess = true;
                player.GuessSubmission = cmd.Guesses;

                // Trigger countdown if this is the first guess
                if (!context.State.GuessCountdownActive)
                {
                    context.State.GuessCountdownActive = true;
                    context.State.FirstGuessPlayerId = cmd.PlayerId;

                    // Set 2-turn countdown for all other players
                    foreach (var otherPlayer in context.GamePlayers.Values)
                    {
                        if (otherPlayer.PlayerId != cmd.PlayerId)
                            otherPlayer.GuessCountdownTurnsRemaining = 2;
                    }

                    context.Logger.LogInformation(
                        "Guess countdown triggered by player [{pid}].", cmd.PlayerId);
                }

                return AdvanceToNextPlayer(context);
            }

            case SkipGuessCommand cmd:
            {
                if (cmd.PlayerId != currentPlayerId)
                    return new ResultError("It is not your turn.");
                return AdvanceToNextPlayer(context);
            }

            default:
                return null;
        }
    }

    public ValueResult<...?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
    {
        if (now < _expiresAt) return null;
        // Auto-skip on timeout
        return AdvanceToNextPlayer(context);
    }

    // GetRemainingTime, OnExit...

    /// <summary>
    /// Validates guess submission format:
    /// - Must include an entry for every opponent (not self)
    /// - Each entry must have exactly 3 task IDs
    /// - All task IDs must exist in the current round's task pool (the dossier)
    /// - No duplicate task IDs within a single opponent's guess
    /// </summary>
    private static string? ValidateGuesses(
        HiddenAgendaGameContext context,
        string playerId,
        Dictionary<string, List<string>> guesses)
    {
        var opponents = context.GamePlayers.Keys
            .Where(id => id != playerId).ToHashSet();

        // Must have exactly one entry per opponent
        if (guesses.Count != opponents.Count)
            return $"Must guess for all {opponents.Count} opponents.";

        foreach (var (opponentId, taskIds) in guesses)
        {
            if (!opponents.Contains(opponentId))
                return $"Invalid opponent ID: {opponentId}";
            if (taskIds.Count != 3)
                return $"Must guess exactly 3 tasks for each opponent.";
            if (taskIds.Distinct().Count() != 3)
                return "Duplicate task IDs in guess for an opponent.";

            var poolIds = context.State.CurrentTaskPool.Select(t => t.Id).ToHashSet();
            foreach (var taskId in taskIds)
            {
                if (!poolIds.Contains(taskId))
                    return $"Task ID '{taskId}' is not in the current dossier.";
            }
        }

        return null; // Valid
    }

    private static IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>
        AdvanceToNextPlayer(HiddenAgendaGameContext context)
    {
        // Check round-end conditions after potential countdown decrement
        var trigger = context.CheckRoundEndConditions();
        if (trigger != RoundEndTrigger.None)
            return new FinalGuessState();

        context.State.TurnManager.NextTurn();
        return new EventCardPhaseState();
    }
}
```

### 2. Update `DrawPhaseState.FinishTurn` (from Phase 3)

The FinishTurn method in DrawPhaseState should transition to GuessPhaseState regardless of whether the player has already guessed (GuessPhaseState.OnEnter handles the skip). This simplifies the flow:

```csharp
private IGameState<...>? FinishTurn(HiddenAgendaGameContext context)
{
    var currentPlayerId = context.State.TurnManager.CurrentPlayer!;
    var player = context.GamePlayers[currentPlayerId];

    player.TurnsTakenThisRound++;
    context.State.TotalTurnsTaken++;

    // Decrement guess countdown if active
    if (context.State.GuessCountdownActive && !player.HasSubmittedGuess)
        player.GuessCountdownTurnsRemaining--;

    // Check round-end conditions
    var trigger = context.CheckRoundEndConditions();
    if (trigger != RoundEndTrigger.None)
        return new FinalGuessState();

    // Always go to GuessPhaseState (it will skip if player already guessed)
    return new GuessPhaseState();
}
```

### 3. Update `HiddenAgendaGameContext.CheckRoundEndConditions()`

Ensure the guess countdown check is correct:

```csharp
public RoundEndTrigger CheckRoundEndConditions()
{
    // 1. Collection trigger: 3 of 5 collections completed
    if (GetCompletedCollectionCount() >= 3)
        return RoundEndTrigger.CollectionTrigger;

    // 2. Guess countdown: all non-first-guesser players have exhausted their 2 turns
    if (State.GuessCountdownActive)
    {
        bool allExpired = GamePlayers.Values
            .Where(p => p.PlayerId != State.FirstGuessPlayerId)
            .All(p => p.HasSubmittedGuess || p.GuessCountdownTurnsRemaining <= 0);
        if (allExpired)
            return RoundEndTrigger.GuessCountdown;
    }

    // 3. Max turns: all players at their maximum
    int maxTurns = GetMaxTurnsPerPlayer();
    if (GamePlayers.Values.All(p => p.TurnsTakenThisRound >= maxTurns))
        return RoundEndTrigger.MaxTurns;

    return RoundEndTrigger.None;
}
```

---

## Tests

### `Unit/Logic/Games/HiddenAgenda/States/GuessPhaseStateTests.cs` (new)

```
OnEnter:
- Player already guessed -> chain-transitions to next player's EventCardPhaseState
- Player hasn't guessed -> sets phase to GuessPhase, stays in state

SubmitGuessCommand:
- Valid guess stores guesses on player state and marks HasSubmittedGuess
- Valid guess transitions to next player
- First guess triggers countdown (GuessCountdownActive = true, other players get 2 remaining turns)
- Second guess does NOT re-trigger countdown
- Wrong player returns error
- Already-guessed player returns error
- Missing opponent in guesses returns error
- Wrong number of tasks (not 3) returns error
- Duplicate task IDs in single opponent guess returns error
- Task ID not in dossier returns error
- Extra opponent ID returns error

SkipGuessCommand:
- Valid skip advances to next player
- Wrong player returns error

Tick:
- Auto-skips on timeout, advances to next player

Countdown integration:
- After first guess, each other player's GuessCountdownTurnsRemaining starts at 2
- After 2 more turns per player, CheckRoundEndConditions returns GuessCountdown
- Player who submitted guess is excluded from countdown check
```

### `Unit/Logic/Games/HiddenAgenda/HiddenAgendaGameContextTests.cs` (update)

Add tests for updated CheckRoundEndConditions:
```
- Countdown active + all non-first players exhausted -> GuessCountdown
- Countdown active + some players still have turns remaining -> None
- First guesser excluded from countdown exhaustion check
- All three triggers can coexist (collection trigger takes priority)
```

---

## Verification

1. `dotnet build` compiles
2. All tests pass
3. Full turn loop with guessing: player takes turn -> GuessPhase -> submit or skip -> next player
4. First guess triggers countdown correctly
5. Countdown decrements properly on subsequent turns
6. Round ends when: (a) 3 collections complete, (b) countdown expires, (c) max turns
7. Guess validation catches all invalid formats
8. Player can only guess once per round
