# Milestone 04 — Round Lifecycle & Timers

*Implements GDD §3 (full loop) and §9 (runtime flow). Depends on: 02, 03. Unblocks: 05, 06.*

---

## Goal

Replace Milestone 01's placeholder transitions with the real multi-round loop: each round generates a board (03), solves its authoritative word set (02), stores both on the state, runs a countdown-timed `Playing` phase that locks input on expiry, then advances through reveal/round-over to the next round or final standings.

## Scope

**In:** per-round grid generation + solve stored on state; the real phase machine driven by `ScheduleCallback` + `PhaseExpiresAtUtc`; input-lock semantics; player-disconnect handling; the countdown timer component.

**Out:** the tracing UI and `SubmitTrace` itself (05), scoring math at round close (06), reveal content (07). `CompleteRound` here just transitions; 06 fills in scoring.

## Files to create or modify

- **Modify** `TraceryGameState` — add per-round fields: `Grid? CurrentGrid`, the authoritative findable set (`IReadOnlyDictionary<string,TracedWord>`), `RoundStartTime`, `IsRoundActive`.
- **Modify** `TraceryGameEngine` — real `EnterRoundIntro / EnterPlaying / CompleteRound / AdvanceAfterResults / EnterFinalStandings`; generate+solve at round start; `SubscribePlayerUnregistered` handling.
- **Create** `Components/TraceryRoundTimer.razor` — mirror `SpardleRoundTimer` (`CountdownClock` + unlimited fallback).
- **Modify** `Pages/TraceryRoom.razor` — render the timer during `Playing`; wire phase views.
- **Modify** `Unit/Logic/Games/TraceryGameEngineTests.cs`.

## Key types & methods

Phase helpers (all assume they run inside the execute lock — directly or via `ScheduleCallback`, which wraps in `ExecuteAsync`), modeled on `SpardleEngine` lines ~254–386:

- **`EnterRoundIntro`** — if `CurrentRound >= TotalRounds`, go to `EnterFinalStandings`; else set `Phase = RoundIntro`, `PhaseExpiresAtUtc = now + TransitionDuration`, schedule `EnterPlaying`.
- **`EnterPlaying`** — `GridGenerator.Generate(settings)`; `TracerySolver.Solve` (or reuse the set generation produced); store `CurrentGrid` + findable set; reset each player's round bank; `IsRoundActive = true`; `Phase = Playing`. If `RoundTimer > 0`, set `PhaseExpiresAtUtc` and `ScheduleCallback(RoundTimer, EndRoundIfStillActive(capturedRound))`; else unlimited (`PhaseExpiresAtUtc = null`) — host-advanced.
- **`EndRoundIfStillActive(roundNum)`** — guard `Phase == Playing && CurrentRound == roundNum`; then `CompleteRound`.
- **`CompleteRound`** — `IsRoundActive = false`; (06 inserts unique-find resolution + scoring + `RoundResult` here); `Phase = Reveal`; schedule `AdvanceAfterResults` after a reveal duration.
- **`AdvanceAfterResults`** — next round via `EnterRoundIntro`, or `EnterFinalStandings` when done.
- **`EnterFinalStandings`** — `Phase = FinalStandings`, `IsRoundActive = false`, `PhaseExpiresAtUtc = null`.

**Input lock:** the round is the gate. `SubmitTrace` (added in 05) early-returns a failure unless `Phase == Playing && IsRoundActive`. When the timer fires, `EndRoundIfStillActive` flips `IsRoundActive`/`Phase`, so subsequent submissions are rejected — no separate lock flag needed.

**Disconnect handling:** `SubscribePlayerUnregistered` (Spardle precedent) so a mid-round leaver doesn't hold an "everyone finished" check open. For Tracery players bank independently against a clock, so a leaver simply stops banking; the timer still ends the round. Re-check any "all done early" optimization here if one is added.

## Reuse references

- `SpardleEngine.cs` lines 254–386 (`EnterRoundIntro`/`EnterPlaying`/`CompleteRound`/`AdvanceAfterResults`/`EnterGameOver`) and lines 24–40 (`SubscribePlayerUnregistered`).
- `SpardleRoundTimer.razor` + `CountdownClock` (`KnockBox.Core.Components.Shared`).

## Acceptance criteria

- Each round shows a freshly generated, quality-passing board; the same board is shown to all players.
- The countdown renders and, on expiry, the round ends and further submissions are rejected.
- The match runs `TotalRounds` rounds then lands on `FinalStandings`.
- Unlimited-timer mode (`RoundTimer = 0`) shows the ∞ timer and waits for host advance.
- A player leaving mid-round never hangs the round.

## Tests

- Deterministic phase-sequence test: assert `Phase` and `PhaseExpiresAtUtc` at each transition; advancing `TotalRounds` times reaches `FinalStandings`.
- A `Grid` and findable set are populated on entering `Playing`.
- Submissions (stub) are rejected when `Phase != Playing` or `!IsRoundActive`.
- `EndRoundIfStillActive` is a no-op when the round already advanced (stale captured round number).
