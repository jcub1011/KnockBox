# Milestone 07 — Reveal & Host Display

*Implements GDD §7. Depends on: 05 (banks), 06 (scores). Unblocks: 08.*

---

## Goal

Make the host screen the social payoff. During `Playing` the host shows the shared grid and live standings; at round close the `Reveal` plays paced beats (longest word + finder, highest-scoring word, words nobody found, rarest letters, optional theoretical max, standings); the match ends on a `FinalStandings` screen sorted by cumulative score.

## Scope

**In:** the host-observer `Playing` view; reveal-data assembly from the solver's findable set + players' banks; the reveal and final-standings views.

**Out:** new scoring math (07 only reads `RoundResult` + the findable set from 06/04); player-side input (05).

## Files to create or modify

- **Create** `Services/Logic/RevealBuilder.cs` — pure assembly of reveal data from `CurrentGrid`, the findable set, and the round's banks/`RoundResult`.
- **Modify** `Models/RoundResult.cs` (or add `Models/RevealData.cs`) — carry the assembled beats.
- **Create** host views: `Components/TraceryHostGridView.razor` (shared grid + live standings during `Playing`), `Components/TraceryRevealView.razor`, `Components/TraceryFinalStandingsView.razor`.
- **Modify** `Pages/TraceryRoom.razor(.cs)` — render host vs player views per phase (host-observer split like `SpardleHostObserverPlayingView`); pace reveal beats.
- **Create** `Unit/Logic/RevealBuilderTests.cs`.

## Key types & methods

**`RevealBuilder.Build(grid, findableSet, roundResult, settings)`** → reveal data:
- **Longest word found** + which player(s) found it (tie-break defined).
- **Highest-scoring word** of the round (from `RoundResult` per-word points).
- **Words nobody found** — `findableSet` keys minus the union of all banks; surface the long/rare ones first (sort by would-be score). Sourced directly from the solver's complete set (GDD §7/§9).
- **Rarest letters put to use** — highest-value rare letters appearing in banked words.
- **Theoretical maximum** (optional, GDD §7) — total score if one player banked the entire `findableSet` (each word scored as unique). Benchmark only.
- **Standings** — per-player round points + running cumulative.

Keep `RevealBuilder` pure so it is unit-testable from a fixed grid + scripted banks; the view just renders it.

**Pacing:** the `Reveal` phase (entered by `CompleteRound` in 04) reveals beats over time. Either reveal all at once for v1 and add staggered `ScheduleCallback` beats as polish, or stagger from the start — keep the beat list data-driven so ordering/timing is tunable.

**Host vs player routing:** in `TraceryRoom`, branch on `IsHost()` (Operator/Spardle `IsHost` precedent). Host gets grid + standings; players keep their controller/feedback view.

## Reuse references

- `SpardleRoom.razor` host-observer split + `SpardleHostObserverPlayingView`.
- `HiddenAgenda/Pages/MatchOverPhase.razor.cs` — winner/standings sort by `CumulativeScore` descending.
- `RoundResult` per-word breakdown (06) and the findable set (04) as the only data sources — no recompute.

## Acceptance criteria

- Host `Playing` view shows the shared grid and live standings, updating as players bank words (via `StateChangedEventManager`).
- Reveal correctly identifies the longest word + finder, the highest-scoring word, and lists notable words nobody found (sourced from the solver set).
- Theoretical max (when enabled) is computed from the full findable set.
- `FinalStandings` ranks players by cumulative score with a clear winner.

## Tests

- `RevealBuilder` from a fixed grid + scripted banks: assert longest, highest-scoring, nobody-found set (set difference), rarest-letters, and theoretical-max values.
- Tie cases (two players share the longest word) resolve per the defined rule.
