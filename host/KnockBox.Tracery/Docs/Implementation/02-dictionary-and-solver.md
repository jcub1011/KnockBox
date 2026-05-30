# Milestone 02 — Dictionary & Solver (the keystone)

*Implements GDD §4 (validation) and §9 (solver). Depends on: 01. Unblocks: 03, 04, 05, 06, 07.*

---

## Goal

Build the single grid solver that underpins four jobs (GDD §9): generation testing, runtime validation, scoring input, and reveal data. It is pure logic with no Blazor/state dependencies, so it is exhaustively unit-testable in isolation. This is the most important milestone to get right.

## Scope

**In:** reference the word-service contract; a Tracery-owned trie built once from the full dictionary; a `Grid` model with 8-way adjacency; a DFS solver producing the complete findable-word set; a `ValidateTrace` entry point for runtime submissions.

**Out:** board generation (03), scoring math (06 — solver only returns words + paths), UI (05).

## Files to create or modify

- **Modify** `KnockBox.Tracery.csproj` — add `ProjectReference` to `..\KnockBox.WordService.Contracts\KnockBox.WordService.Contracts.csproj`; add `InternalsVisibleTo` for tests (if not already from 01).
- **Modify** `KnockBox.TraceryTests.csproj` — add `ProjectReference`s to `KnockBox.WordService` and `KnockBox.WordService.Contracts` (so tests build the real trie from real data).
- **Modify** `TraceryGameEngine` — inject `IWordListService` (Spardle ctor pattern); own and lazily build the trie singleton.
- **Create** `Services/Logic/Dictionary/TraceryTrie.cs`.
- **Create** `Models/Grid.cs`.
- **Create** `Models/TracedWord.cs` — a found word + its cell path (record).
- **Create** `Services/Logic/TracerySolver.cs`.
- **Create** `Unit/Logic/Dictionary/TraceryTrieTests.cs`, `Unit/Logic/TracerySolverTests.cs`.

## Key types & methods

**`TraceryTrie`**
- `bool IsWord(ReadOnlySpan<char> word)` and `bool IsPrefix(ReadOnlySpan<char> word)` — both allocation-free on the hot path (span-based, lowercase ASCII).
- `static TraceryTrie BuildFrom(IWordListService svc, int minWordLength)` — enumerate `WordPoolMode.FullDictionary`: for each `len` in `GetAvailableLengths`, for each `i` in `[0, GetWordCount(mode,len))`, insert `GetWord(mode,len,i)` (raw ASCII bytes) when `len >= minWordLength`. Skip words shorter than the floor at build time so the search space is already pruned.
- Built **once** and cached on the singleton engine (the ~386k-word load cost is paid at first game creation, not per round). Use a lazy/`Interlocked` guard so concurrent first lobbies don't double-build.

**`Grid`**
- Backing store of letters (`char[]` row-major) + `Width`/`Height`.
- `char this[int r, int c]`, `int CellId(r,c)`, `(int r,int c) FromCellId(int)`.
- `IReadOnlyList<int> Neighbors(int cellId)` — 8-way orthogonal+diagonal, edge/corner aware. Precompute an adjacency table at construction for speed (the solver hits it constantly).

**`TracerySolver`** (constructed with the trie + settings)
- `IReadOnlyDictionary<string, TracedWord> Solve(Grid grid, int minWordLength)` — DFS from every cell, 8-way adjacency, a per-path `visited` set (or bitmask) enforcing **no self-intersection within one word**, **prefix pruning** (abandon the moment `IsPrefix` is false), collecting words of length ≥ min that pass `IsWord`. Dedupe to one entry per distinct word (keep the first/shortest path found — paths only matter for reveal animation, not scoring). Cells are reusable across different words (a fresh visited set per DFS root).
- `Result ValidateTrace(Grid grid, IReadOnlyList<int> path, int minWordLength)` — runtime check for a player submission: path length ≥ min; each consecutive pair adjacent (8-way); no repeated cell; the spelled word is `IsWord`. Returns the spelled word on success. This is the single source of truth for accepting a banked word in Milestone 05 — do **not** re-implement adjacency rules in the UI.

## Reuse references

- `SpardleEngine.cs` lines 13–18 (ctor injection of `IWordListService` + `ILoggerFactory`).
- `IWordListService.cs` — `GetAvailableLengths`, `GetWordCount`, `GetWord(mode,len,index)` (returns `ReadOnlySpan<byte>` aliasing internal buffer — decode/insert immediately, never store across `await`).
- `KnockBox.SpardleTests` references `KnockBox.WordService` directly to construct `WordListService` in tests — copy that approach.

## Acceptance criteria

- `Solve` on a hand-built grid returns exactly the expected word set (including the long/diagonal/bending paths, excluding self-intersecting ones).
- Prefix pruning measurably bounds the search (no exploration past dead prefixes — assert via an instrumented trie or a timing sanity check on a 5×5).
- `ValidateTrace` rejects: too-short, non-adjacent jump, revisited cell, non-dictionary word; accepts a legal trace and returns the word.
- Words below `MinWordLength` never appear in `Solve` output.

## Tests

- Trie: `IsWord`/`IsPrefix` truth table on known in/out words; case-insensitive; non-ASCII → false.
- Solver: small fixed grid → exact expected set; diagonal-only word; bending path; self-intersection excluded; reused-cell-across-words confirmed; min-length filter.
- `ValidateTrace`: one parametrized test per rejection reason + the happy path.
