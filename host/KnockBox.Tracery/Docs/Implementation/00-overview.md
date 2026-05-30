# Tracery Implementation — Overview & Conventions

*Companion to the [Game Design Document](../tracery-gdd.md). Read the GDD first; this folder turns it into buildable milestones.*

---

## Purpose

`KnockBox.Tracery` currently ships as scaffolding only: placeholder `TraceryGameState`/`TraceryGameEngine`, a single lobby page, a header component, and `plugin.json` flagged `workInProgress: true`. These milestone documents describe, in build order, how to grow that scaffolding into the full game from the GDD — a real-time, shared-grid, competitive word-tracing party game with a generate-and-test board pipeline, a DFS grid solver, a layered scoring system, timed rounds, and a host-screen reveal.

Each milestone is independently reviewable and leaves the build green.

## Architectural decision — follow the Spardle template

Tracery is structurally the twin of **Spardle**: a timed, multi-round word game with cross-round scoring and an end screen. The scaffolding already subclasses `AbstractGameState`/`AbstractGameEngine` directly, so Tracery follows Spardle's proven, lightweight pattern rather than the heavier FSM (`IGameState<TContext,TCommand>`) used by Operator/HiddenAgenda.

Core mechanics borrowed from Spardle:

- A **`GamePhase` enum** on the state; the engine drives transitions.
- **Timed transitions** via `state.ScheduleCallback(duration, callback)` (wraps the callback in `ExecuteAsync`). Countdown UI reads `state.PhaseExpiresAtUtc` and renders with the shared `CountdownClock` component (`KnockBox.Core.Components.Shared`) — see `SpardleRoundTimer.razor`.
- A **`TracerySettings` record** on the state, replaced atomically via `UpdateSettings(Func<TracerySettings,TracerySettings>)` routed through `Execute`. Persisted to host-browser localStorage from the lobby page (HiddenAgenda/Operator `LocalStorage.GetAsync/SetAsync` + `_userHasEdited` guard pattern).
- **Per-player data** in a dictionary keyed by user id, written only inside `Execute`/`ExecuteAsync`. Round outcomes captured as immutable `RoundResult` records; cumulative scores summed across rounds.
- **Real-time flow:** player actions call engine methods that wrap mutation in `state.Execute(...)`; pages (inheriting `LobbyPageBase<TraceryGameState>` / `DisposableComponent`) subscribe to `state.StateChangedEventManager` and re-render, disposing the subscription in `Dispose()`.
- A **single `@page "/room/tracery/{ObfuscatedRoomCode}"` root** (`TraceryRoom.razor`) that switches the rendered view on `Phase` and on host-vs-player role (Spardle's `SpardleRoom.razor` switch + `SpardleHostObserverPlayingView` precedent).

`RouteIdentifier` stays `"tracery"` (must match the `@page` route). Player range stays `(2, 8)`.

## Reference files to mirror

| Concern | Reference |
|---|---|
| Engine: phases, `ScheduleCallback`, scoring, guess handling | `KnockBox.Spardle/SpardleEngine.cs` |
| State: settings, phase, player dictionary, frozen roster | `KnockBox.Spardle/SpardleState.cs` |
| Settings record + immutable update | `KnockBox.Spardle/SpardleSettings.cs` |
| Phase-switching page + code-behind | `KnockBox.Spardle/Pages/SpardleRoom.razor(.cs)` |
| Countdown timer component | `KnockBox.Spardle/Components/SpardleRoundTimer.razor` |
| Phase/PlayerState/RoundResult models | `KnockBox.Spardle/Models/*` |
| Settings localStorage persistence | `KnockBox.HiddenAgenda/Pages/LobbyPhase.razor.cs` |
| Final standings screen | `KnockBox.HiddenAgenda/Pages/MatchOverPhase.razor.cs` |
| Word-list contract | `KnockBox.WordService.Contracts/IWordListService.cs` |
| JS interop module loading | `KnockBox.Spardle/Pages/SpardleRoom.razor.cs` (`spardle-keyboard.js`) |
| Deterministic RNG test double | `KnockBox.SpardleTests/Unit/SpardleEngineTests.cs` (`SequentialRng`) |

## Dependency additions

- `host/KnockBox.Tracery/KnockBox.Tracery.csproj` — add `<ProjectReference Include="..\KnockBox.WordService.Contracts\KnockBox.WordService.Contracts.csproj" />` (compile-time contract only; the impl is resolved at runtime from the sibling library plugin, which the host loads before any game). Add `<InternalsVisibleTo Include="KnockBox.TraceryTests" />`.
- `host/KnockBox.TraceryTests/KnockBox.TraceryTests.csproj` — add `ProjectReference`s to **both** `KnockBox.WordService` (impl, constructed directly in tests) and `KnockBox.WordService.Contracts` — mirroring `KnockBox.SpardleTests`.

## Confirmed decisions

- **Dictionary source:** reuse `KnockBox.WordService`. Build a Tracery-owned **trie** once from the `FullDictionary` pool (enumerate via `GetAvailableLengths`/`GetWordCount`/`GetWord`). No new word data shipped. `IWordListService` only does exact and indexed lookups — there is **no** prefix structure anywhere in the repo, so the trie is Tracery's to build (Milestone 02).
- **Tracing input:** support **both** smooth drag-to-trace (pointer/touch via JS interop) **and** tap-adjacent-cells, sharing one client-side path model (Milestone 05).
- **Scoring constants** (length-bonus curve, rare-letter table, unique-find multiplier) and **generation quality bar** all live in `TracerySettings` so they are playtest-tunable (GDD §5, §6, §10).

## Milestone dependency graph

```
01 Foundation ──► 02 Solver ──► 03 Generation ──► 04 Round lifecycle ──┬─► 05 Tracing input ─┐
                                                                       └─► 06 Scoring ───────┴─► 07 Reveal ─► 08 Testing & tuning
```

01 must land first (skeleton flow). 02→03→04 are the logic spine. 05 and 06 both depend on 04 and can proceed in parallel. 07 consumes 05+06. 08 hardens everything.

## Folder conventions (already used by the scaffolding)

- Engine: `Services/Logic/Games/TraceryGameEngine.cs`
- State: `Services/State/Games/TraceryGameState.cs`
- New solver/generation/scoring logic: `Services/Logic/{Dictionary,...}/`
- POCO models/records: `Models/`
- Razor: `Pages/` (routable) and `Components/` (reusable)
- Browser assets: `wwwroot/` (mounted at `_content/KnockBox.Tracery/...`)
- Tests mirror the source tree under `KnockBox.TraceryTests/Unit/...`

## Glossary

- **Trace / path** — the ordered sequence of grid cells a player connects to spell a word.
- **Bank** — a player's accepted set of words for the current round; a word scores once per player per round regardless of path.
- **Findable word set** — every valid word the solver can trace on a given board; drives validation, the "nobody found" reveal, and the theoretical maximum.
- **Unique-find** — a word banked by exactly one player in the round; earns the unique-find multiplier (GDD §5.4).
- **Theoretical max** — the total score obtainable if a single player banked the entire findable set (benchmark on the reveal).

## GDD coverage map

| GDD section | Milestone(s) |
|---|---|
| §3 Core loop, §8 Settings | 01, 04 |
| §4 Rules of tracing | 02 (validation), 05 (input) |
| §5 Scoring | 06 |
| §6 Grid generation | 03 |
| §7 The reveal | 07 |
| §9 Solver / architecture | 02, 03, 04 |
| §10 Open questions / tuning | 08 |
