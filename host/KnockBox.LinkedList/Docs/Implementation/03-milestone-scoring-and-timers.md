# Milestone 3 — Scoring Modes & Timers

## Goal

Make both scoring modes real and add a round-results screen. **Fewest Guesses** (puzzle): score = accepted pairs; rejections are free but capped. **Fastest Time** (pressure): a per-turn clock runs while the active player thinks/submits, **pauses during auditing**, and **rejected attempts still consume clock time**; the score is total elapsed time. Add the host-set **par** comparison for Collective, and a `RoundOver` screen showing the result.

## Prerequisites

- Milestones 1–2 complete: core loop works in Fewest Guesses; `RejectionsThisTurn`, `Chain`, `AcceptedPairs`, `RoundOver` phase exist.

## Architecture context (restated)

- Timers use `state.ScheduleCallback(TimeSpan, Func<Task>)` (auto-cancelled on dispose) to fire timeout transitions, plus a `PhaseExpiresAtUtc` timestamp the UI reads to render a live countdown.
- Live countdown rendering uses the shared `CountdownClock` component driven by the host tick. Copy the pattern from `host/KnockBox.Codeword/Pages/CodewordLobby.razor` (lines 34–57) and the host-tick hook in `CodewordLobby.razor.cs` (`TryGetHostTick`, lines ~15–39): `tickInterval = TickService.TicksPerSecond`.
- The "clock pauses during auditing" requirement means **accumulate elapsed time on segment boundaries**, not by counting down a single deadline. Track an accruing total plus the timestamp the current *thinking* segment started; pause = stop the segment and bank its elapsed; resume = start a new segment.

## Files to create / modify

**Modify**
- `LinkedListSettings.cs` — timer fields already present from M1 (`PerTurnClock`, `EnableTimers`); add any per-mode tuning needed.
- `Services/State/Games/LinkedListGameState.cs` — time-accrual fields + guess accessor.
- `Services/Logic/Games/LinkedListGameEngine.cs` — start/pause/resume clock on submit/audit transitions; compute round result.
- `Pages/PlayingPhase.razor` — timer bar (Fastest Time only).
- `Pages/LinkedListLobby.razor(.cs)` — `TryGetHostTick` host tick + `RoundOver` dispatch.

**Create**
- `Pages/RoundOverPhase.razor` (+ `.css`) — round result screen.

## Implementation detail

### 1. Time accrual model (state)

```csharp
// Fastest Time accrual. All mutated inside Execute.
public TimeSpan ElapsedThinkingTime { get; set; } = TimeSpan.Zero; // banked total for the round
public DateTimeOffset? ThinkingSegmentStartedUtc { get; set; }      // non-null while the clock is "running"
public bool ClockRunning => ThinkingSegmentStartedUtc is not null;

public int GuessCount => Chain.Count;  // accepted pairs = Fewest-Guesses score
```

Helpers (called inside `Execute`):
- `StartClock()` → if Fastest Time + `EnableTimers` and not running, set `ThinkingSegmentStartedUtc = now`.
- `BankClock()` → if running, `ElapsedThinkingTime += now - ThinkingSegmentStartedUtc; ThinkingSegmentStartedUtc = null`.

> `now` must be passed in (e.g. `DateTimeOffset.UtcNow` captured by the caller) so engine logic stays testable. Do not call `DateTimeOffset.UtcNow` from inside scripts/tests indirectly — engine code may use it, but tests should inject or assert tolerances.

### 2. Engine clock transitions (Fastest Time)

- **Turn begins / player is thinking**: `StartClock()` (when a new submitter becomes active, including after a forfeit advance).
- **`SubmitPair`**: `BankClock()` — submission goes to the Auditor; the clock pauses during auditing (deliberation never counts, §5.2).
- **`Approve`**: clock stays banked; next submitter's turn calls `StartClock()`. On destination, finalize.
- **`Reject`**: rejected attempts **consume clock** (§5.2) — so the time spent up to `SubmitPair` was already banked; on reject, `StartClock()` again for the retry (the player is thinking again). Net effect: bad guesses cost the seconds spent thinking about them. If the cap forfeits the turn, `BankClock()` and move on.
- Per-turn timeout (optional, `PerTurnClock`): `ScheduleCallback(PerTurnClock, …)` to auto-forfeit a turn that runs over; set `PhaseExpiresAtUtc` for the UI. Cancel the handle when the turn ends early.

### 3. Round result + par

On reaching `RoundOver`, compute and store a `RoundResult`:
```csharp
public sealed record RoundResult(
    ScoringMode Mode, int Guesses, TimeSpan Elapsed,
    int? Par, bool BeatPar, bool DestinationReached);
public RoundResult? LastRoundResult { get; set; }
```
- Fewest Guesses: `Guesses = GuessCount`; `BeatPar = Par is int p && Guesses <= p`.
- Fastest Time: bank any running segment first; `Elapsed = ElapsedThinkingTime`. (Par for time is a future nicety — compare to the team's previous run if you track it; otherwise just report elapsed.)

### 4. UI

- **Timer bar** in `PlayingPhase` (only `ScoringMode == FastestTime && EnableTimers`): render running elapsed (and the per-turn countdown if `PerTurnClock` is used) with `CountdownClock`. Show a clear "PAUSED — auditing" state when `!ClockRunning` and a submission is pending.
- **Host tick**: implement `TryGetHostTick` in `LinkedListLobby.razor.cs` so the countdown advances smoothly (interval `TickService.TicksPerSecond`). Only the host needs to drive timeout transitions; all clients render from `PhaseExpiresAtUtc`/elapsed.
- **`RoundOverPhase`**: show whether the destination was reached, the score for the active mode (guesses or elapsed), par comparison ("Par 4 · You: 3 — under par!"), the full final chain, and a Continue control (host-only) that leads into the match-flow handling added in M4 (for now it can return to lobby or start a new round).

## Tests

- Fewest Guesses: `GuessCount` equals accepted pairs and ignores rejections (reject several times under the cap, confirm count unchanged).
- Fastest Time accrual: simulate segments by passing controlled timestamps — thinking accrues, `SubmitPair` banks and pauses (no accrual during the audit gap), a reject resumes accrual, destination finalizes `ElapsedThinkingTime`. Assert the banked total equals the sum of thinking segments only (audit gaps excluded).
- Rejections cost time: time spent before a rejected `SubmitPair` is banked and not refunded.
- Par: `BeatPar` true/false around the boundary; null par → `BeatPar == false` and no par shown.

## Verification

- `dotnet test …` green.
- Manual: play a **Fewest Guesses** round — score = accepted pairs, rejections free. Play a **Fastest Time** round — confirm the clock visibly pauses while the Auditor deliberates and that a rejected attempt eats the seconds the player spent. `RoundOver` shows the right metric and par comparison.

## Done-when checklist

- [ ] `GuessCount` = accepted pairs; rejections never counted in Fewest Guesses.
- [ ] Fastest-Time clock banks thinking segments, pauses during auditing, and charges rejected attempts.
- [ ] Per-turn timeout (if enabled) forfeits via `ScheduleCallback`; handle cancelled on early turn end.
- [ ] `RoundResult` computed and shown on `RoundOverPhase` with par comparison.
- [ ] Host tick drives a smooth live countdown; all clients render consistent time.
- [ ] Tests pass; both modes verified manually.
