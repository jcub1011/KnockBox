# Milestone 06 — Scoring

*Implements GDD §5. Depends on: 04 (round close), 05 (banks). Unblocks: 07. Parallel with 05.*

---

## Goal

Implement Tracery's layered scoring identity and resolve it at round close: base length + superlinear length bonus + rare-letter bonus + the signature unique-find multiplier. All constants are settings-driven so playtests can retune (GDD §10). Per-round and cumulative scores feed the reveal and standings.

## Scope

**In:** a pure `TraceryScorer`; the round-close pass that computes per-word/per-player points, resolves unique-find across all banks, writes a `RoundResult`, and updates cumulative scores.

**Out:** reveal presentation (07) — scoring only produces the numbers and per-word breakdowns it needs.

## Files to create or modify

- **Create** `Services/Logic/TraceryScorer.cs` — pure scoring functions (constants passed in from `TracerySettings`).
- **Modify** `TracerySettings` — expose the length-bonus table, rare-letter table, unique-find multiplier + toggles (the placeholders reserved in 01).
- **Modify** `TraceryGameEngine.CompleteRound` — insert the unique-find resolution + scoring + `RoundResult` write + cumulative update.
- **Modify** `Models/RoundResult.cs` / `TraceryPlayerState` — carry per-word point breakdowns and per-player round/cumulative totals.
- **Create** `Unit/Logic/TraceryScorerTests.cs`.

## Key types & methods

**`TraceryScorer`** (pure, deterministic):
- `int BaseScore(word)` = length.
- `int LengthBonus(len)` — table from GDD §5.2: 4→0, 5→+1, 6→+3, 7→+6, 8→+10, 9→+15, 10+→+21 (and up). Sourced from settings.
- `int RareLetterBonus(word)` — per qualifying letter occurrence (GDD §5.3): K,F,H,V,W,Y → +1; J,X → +3; Q,Z → +5. Gated by `RareLetterBonusEnabled`.
- `int WordScore(word, bool isUnique, settings)` = `round((Base + LengthBonus + RareLetter) * (isUnique && UniqueFindBonusEnabled ? UniqueFindMultiplier : 1.0))`. Multiplier applies to the **whole** word score so harder unique finds pay more (GDD §5.4). Define the rounding rule explicitly (e.g. `Math.Round(..., MidpointRounding.AwayFromZero)`) and test it.

**Round-close pass** (inside `CompleteRound`, GDD §9 — unique-find resolved only after the round locks):
1. Build a global frequency map: for each banked word, how many players banked it this round.
2. For each player, for each banked word: `isUnique = freq[word] == 1`; compute `WordScore`; sum into the player's `RoundScore`.
3. `player.CumulativeScore += RoundScore`.
4. Write a `RoundResult` with per-player word breakdowns (word, components, unique flag, points) — the reveal (07) reads these directly rather than recomputing.

**Worked example to encode as a test** (GDD §5.5): `QUARTZ`, unique → Base 6 + LengthBonus 3 + RareLetter (Q+5, Z+5) = 19, ×1.5 ≈ **29**.

## Reuse references

- `SpardleEngine.cs` `BuildOutcomes`/`CompleteRound` (lines ~336–464) — round-close outcome assembly + cumulative update shape; `PointsForSolver` (internal static, unit-tested directly) — mirror that testability for `TraceryScorer`.

## Acceptance criteria

- Each layer matches the GDD tables exactly; toggles disable their layer.
- Unique-find is resolved against the full set of all players' banks, post-lock; a word banked by ≥2 players gets no multiplier for anyone.
- The `QUARTZ` example yields ≈29.
- Cumulative scores accumulate correctly across rounds and match per-round sums.

## Tests

- Each function in isolation (base, length-bonus across the whole table incl. 10+, rare-letter counts incl. repeats, multiplier + rounding).
- `QUARTZ` worked example.
- Unique vs shared: same word banked by 1 vs 2 players → multiplier applied/withheld.
- Cumulative across a scripted 2-round match equals the sum of round results.
