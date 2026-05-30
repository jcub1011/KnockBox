# Milestone 03 — Grid Generation (every board is a good board)

*Implements GDD §6. Depends on: 02 (solver is the quality oracle). Unblocks: 04.*

---

## Goal

Produce boards that are reliably word-rich with at least one big find to chase, using weighted letter sampling plus generate-and-test against the solver. No dead rounds.

## Scope

**In:** a frequency-weighted letter distribution; a generator that samples, solves, and accepts only boards clearing a tunable quality bar; an attempt cap with a small-grid seed fallback.

**Out:** wiring generation into the round loop (04); scoring (06).

## Files to create or modify

- **Create** `Services/Logic/LetterDistribution.cs` — weighted sampling table + `char Next(IRandomNumberService rng)`.
- **Create** `Services/Logic/GridGenerator.cs` — `ValueResult<Grid> Generate(TracerySettings settings)` using injected `IRandomNumberService` + the solver.
- **Modify** `TraceryGameEngine` — inject `IRandomNumberService` (`KnockBox.Core.Services.Logic.RandomGeneration`, as Spardle does) and hold/construct the generator.
- **Create** `Unit/Logic/LetterDistributionTests.cs`, `Unit/Logic/GridGeneratorTests.cs`.

## Key types & methods

**`LetterDistribution`**
- A curated weight table (Boggle-style or English-frequency — start from a documented table so it is auditable). Vowel/consonant balance matters more than raw Scrabble frequency for traceability; bias slightly toward common letters so most first-try boards pass.
- `char Next(IRandomNumberService rng)` — weighted draw via cumulative weights + `rng.GetRandomInt(total)` (same RNG abstraction Spardle samples with). Keep the table in one place so it is tunable per GDD §10.

**`GridGenerator`**
- `Generate(settings)`:
  1. Sample `Width×Height` letters from `LetterDistribution`.
  2. Run `TracerySolver.Solve` on the candidate.
  3. Accept iff it clears the **quality bar** (all from `TracerySettings`): findable-word count ≥ `MinFindableWords` (scale the default with grid area); ≥1 findable word of length ≥ `MinLongWordLength` (default 7 — the "big find"); if `RequireRareLetterWord`, ≥1 findable word using a rare-letter tile.
  4. On failure, regenerate up to `MaxGenerationAttempts`.
  5. **Fallback** (GDD §6 optional): if attempts exhaust (realistically only on very small grids), place a single known-good word from the dictionary along a valid path, then fill remaining cells from the distribution, and re-solve to recompute the findable set. Log when the fallback fires so tuning can see how often.
- Returns the accepted `Grid` (and, for the caller's convenience, the solved findable set can be recomputed once by the engine in 04 — or return both to avoid solving twice).

## Reuse references

- `SpardleEngine.cs` — `IRandomNumberService` injection and `rng.GetRandomInt`/Fisher–Yates usage.
- `TracerySolver` (Milestone 02) as the acceptance oracle.

## Acceptance criteria

- Over many seeded runs at the default 4×4, accepted boards always satisfy the full quality bar.
- Weighted draws produce the table's expected letter frequencies (within tolerance) over a large sample.
- The fallback path triggers and yields a passing board when the quality bar is set artificially high for a tiny grid.
- Generation stays fast enough to run once per round on the host without a perceptible stall (revisit perf formally in 08).

## Tests

- `GridGenerator`: with a deterministic RNG double (Spardle's `SequentialRng` style), every accepted board clears the bar; assert the long-word and rare-letter guarantees hold.
- Fallback: force exhaustion (impossible bar on a 3×3) → fallback fires, board still passes, log emitted.
- `LetterDistribution`: empirical frequency test over N draws matches the weight table within tolerance; never emits an off-table char.
