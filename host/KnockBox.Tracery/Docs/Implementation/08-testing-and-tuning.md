# Milestone 08 — Testing, Performance & Tuning

*Implements GDD §10 (open questions / tuning). Depends on: all prior. Unblocks: ship.*

---

## Goal

Harden the game and make it playtest-ready: a complete test matrix, a performance sanity check on the solver/generator at the largest supported grid, confirmation that every balance lever is settings-driven, and a manual end-to-end verification. Flip `workInProgress` off when shippable.

## Scope

**In:** coverage review + gap-filling tests; perf sanity; tuning-surface documentation; manual E2E script; ship flag.

**Out:** new gameplay features.

## Files to create or modify

- **Add/extend** tests across `KnockBox.TraceryTests/Unit/...` to fill matrix gaps.
- **Add** `Unit/Logic/SolverPerformanceTests.cs` — bounded-time generate-and-test on the largest supported grid.
- **Modify** `plugin.json` — set `workInProgress: false` once the game is verified shippable.
- **Optionally add** a short `TUNING.md` (or a section here) documenting every tunable in `TracerySettings`.

## Test matrix (confirm each is covered)

| Area | Key cases |
|---|---|
| Trie | in/out words, prefix truth, case-insensitivity, non-ASCII |
| Solver | exact word set on fixed grids, diagonal/bending paths, self-intersection excluded, cell reuse, min-length, `ValidateTrace` rejections |
| Generator | quality bar always met (seeded), fallback fires, letter-frequency distribution |
| Scorer | every layer, `QUARTZ` example, unique vs shared, rounding, cumulative |
| Engine | phase sequence + expiry, input lock, multi-round → final standings, disconnect mid-round, `SubmitTrace` rejections + duplicate no-op |
| Reveal | longest/highest/nobody-found/rarest/theoretical-max from fixed inputs, tie-breaks |
| Module | manifest (already present), custom header |

Use a deterministic RNG double (Spardle's `SequentialRng`) wherever randomness would otherwise make assertions flaky. Tests run at `MethodLevel` parallelism (existing `MSTestSettings.cs`).

## Performance

- The solver runs once per round and many times per generated board, so generate-and-test cost is the dominant risk (GDD §9). Add a test asserting that generating a passing board at the **largest supported grid** completes within a generous bound (e.g. well under a second on CI), catching prefix-pruning regressions.
- Confirm the trie is built **once** (cached on the singleton engine), not per round or per lobby.

## Tuning surface (GDD §10 — all must be in `TracerySettings`)

- Length-bonus curve, rare-letter table, unique-find multiplier + toggles.
- Grid dimensions, round timer, rounds per match, min word length.
- Generation quality bar: min findable words, min long-word length, require-rare-letter, max attempts.

Document each with its default and intended effect so playtesters can adjust without code changes.

## Manual end-to-end verification

1. `dotnet build host/KnockBox/KnockBox.csproj` — confirm the plugin stages into `bin/.../games/Tracery/`.
2. `dotnet run --project host/KnockBox/KnockBox.csproj`.
3. Host: create a Tracery lobby; change a few settings; refresh — settings persist.
4. Join from a second browser/profile as a player (phone controller).
5. Start: confirm both see the same generated grid and the countdown.
6. Trace words by **dragging** and by **tapping** — confirm accept/reject feedback, banked list, cell reuse, duplicate no-op.
7. Let the timer expire — confirm input locks and the reveal plays (longest, highest, nobody-found, standings).
8. Confirm the match runs the configured rounds and ends on final standings with a correct winner.
9. Confirm a player leaving mid-round doesn't hang the round.

## Acceptance criteria

- `dotnet test host/KnockBox.Host.slnx` green; the matrix above is fully covered.
- Perf sanity test passes; trie build is once-only.
- All balance levers confirmed settings-driven and documented.
- Manual E2E passes end to end; `workInProgress` set to `false`.
