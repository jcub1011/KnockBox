# Milestone 05 — Player Tracing Input (drag + tap)

*Implements GDD §4 (rules of tracing, player-facing). Depends on: 02, 04. Unblocks: 07 (consumes banks). Parallel with 06.*

---

## Goal

Give each player a phone controller showing their own copy of the shared grid, on which they trace words by **both** smooth drag (pointer/touch) **and** tapping adjacent cells. Completed traces are submitted, validated server-side, and banked with accept/reject feedback. Cells are reusable across words; a word banks once per player per round.

## Scope

**In:** a JS interop module for pointer/touch path capture; a `TraceryGrid` component supporting drag and tap over one shared path model; the `SubmitTrace` engine method; banking + feedback + banked-words list.

**Out:** host display of the shared grid and live standings (07); scoring values (06 — banking records the word; points are computed at round close).

## Files to create or modify

- **Create** `wwwroot/tracery-trace.js` — ES module exporting init/dispose; converts pointer/touch movement over cells into ordered cell-id path events back to .NET via `DotNetObjectReference` (mirror how `SpardleRoom.razor.cs` loads `spardle-keyboard.js`).
- **Create** `Components/TraceryGrid.razor` (+ `.razor.cs`) — renders cells, highlights the in-progress path and the connecting line, raises a completed-path callback. Supports tap-adjacent selection and drag selection feeding **one** client path model.
- **Modify** `TraceryGameEngine` — add `Result SubmitTrace(TraceryGameState state, User player, IReadOnlyList<int> path)`.
- **Modify** `TraceryPlayerState` — banked-words set (word → score-bearing entry, finalized in 06) + helpers; ensure `ResetRound` clears it.
- **Modify** `Pages/TraceryRoom.razor(.cs)` — player `Playing` view hosts `TraceryGrid`, shows banked words + feedback; load/dispose the JS module; reuse a toast/shake feedback component (`Components/TraceryToast.razor`, modeled on `SpardleToast`).
- **Create** `Unit/Logic/Games/TracerySubmitTraceTests.cs`.

## Key types & methods

**Client path model (shared by drag + tap)**
- A single ordered list of selected cell ids with live validity hints (next cell must be 8-way adjacent and unvisited). Tap appends; drag appends as the pointer enters a new eligible cell; backtracking (pointer/tap returns to the previous cell) pops the last cell — standard Word-Hunt feel.
- Submit on pointer-up (drag) or an explicit submit/tap-last action (tap). Clearing resets the path. The client only *previews* legality; the server is authoritative.

**`SubmitTrace`** (wrap mutation in `state.Execute`, Spardle `SubmitGuess` shape):
1. Reject if host is a non-participant observer.
2. Reject if `Phase != Playing || !IsRoundActive` (input lock from 04).
3. Resolve the player's state (reject strangers without materializing an entry).
4. `TracerySolver.ValidateTrace(CurrentGrid, path, MinWordLength)` — the single source of adjacency/length/dictionary truth (built in 02). On failure, return its error for UI feedback.
5. If the word is already banked this round → no-op success (GDD §4: scores once per player per round, regardless of path).
6. Otherwise add to the player's bank. (Point value computed at round close in 06; banking here may store length/letters for later or defer entirely.)

## Reuse references

- `SpardleRoom.razor.cs` — `IJSRuntime`/`InvokeAsync` module load + dispose; toast + shake feedback timing.
- `SpardleEngine.SubmitGuess` (lines ~502–548) — `Execute` wrapping, host-observer rejection, stranger rejection, `Result` unwrap.
- `TracerySolver.ValidateTrace` (02) — do not duplicate rule logic in the component.

## Acceptance criteria

- Both drag and tap produce identical, server-validated submissions through one code path.
- Accepted words appear in the player's banked list; rejected traces show a reason (too short / not adjacent / not a word) and don't bank.
- Re-banking an already-found word is a silent no-op.
- A cell used by one word remains available for others.
- Submissions after the timer expires are rejected.

## Tests

- `SubmitTrace`: parametrized rejections (too short, non-adjacent, self-intersecting, not-a-word, wrong-phase, host-observer) + happy path; duplicate-bank no-op; bank survives across multiple distinct words reusing cells.
