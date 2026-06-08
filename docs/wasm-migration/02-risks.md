# Risks & Trade-offs

No hard blockers exist, but **two risks could sink the migration**. Both must be cleared by the Phase 0
spike before committing to the 12-game big-bang.

---

## Existential risk 1 — Runtime third-party UI inside a *trimmed* WASM client

**The tension:** a shippable WASM app should be IL-trimmed (and ideally AOT'd) for download size, but
trimming is a **closed-world** optimization — it removes everything not statically reachable. A
runtime-loaded third-party plugin DLL is by definition not in the static graph. The stakeholder
requires third-party UI to load at runtime with no host rebuild, so this is unavoidable.

- The plugin's **own** code is fine (we ship the DLL whole, untrimmed). The danger is the **framework
  surface** the plugin needs but the trimmer removed because no first-party code referenced it.
- **Mitigation — preserved framework root set:** a trimming root descriptor
  (`ILLink.Descriptors.xml` / `TrimmerRootAssembly`) preserving the full public surface the plugin SDK
  promises: all of `KnockBox.Core.Client`, the relevant `Microsoft.AspNetCore.Components.*` rendering
  surface, and `System.Text.Json`. Because third-party JSON source-gen contexts aren't known at build
  time, the client must tolerate **reflection-based** JSON for plugin DTOs
  (`JsonSerializerIsReflectionEnabledByDefault = true`), which fights trimming.
- **Net cost:** to keep third-party UI working, the baseline download is larger and trimming is weaker
  than an ideal closed app. Accepted as the price of the runtime-third-party requirement.
- **AOT is off the table for plugin code:** runtime-loaded IL runs on the **interpreter**, not AOT.
  **Recommendation for v1: no AOT** — avoid a two-tier perf model and extra trimming complexity; accept
  a larger runtime download, mitigated by per-game lazy load + immutable caching. Interpreted execution
  is an acceptable perf tax for UI-bound party games.

**Phase 0 kill-criterion 1:** build a throwaway trimmed `.Client`, `LoadFromStream` a DLL **not** in
its build graph, render it, and round-trip a hub command. If the preserved-surface + reflection-JSON
cost is unacceptable, or it doesn't work trimmed → **stop / rethink** (abandon trimming, or abandon
runtime-loadable third-party UI in favor of build-time-bundled UI).

---

## Existential risk 2 — Per-player projection leaks

Today secrets stay server-side because rendering is server-side. In WASM, **anything the server sends
is in browser memory.** Every game needs a correct server-side projection (see
[`01-target-architecture.md`](./01-target-architecture.md) §2) or it leaks hidden roles/cards/fog. This
is net-new work for all 12 games and the most likely place for security bugs.

**Mitigations:**
- A **default-deny base projector** — fields are excluded unless explicitly projected.
- Per-game **leak tests**: "player A's projected view never contains player B's secret."
- Analyzer gates: **KB1006** (client interaction must go through `*.Contracts`), **KB1007** (server
  `ProjectFor` must return a Contracts type, never raw `AbstractGameState`).

**Phase 0 kill-criterion 2:** prove projection + hub fan-out end-to-end on one secret-bearing game
(HiddenAgenda is the cleanest secret model), demonstrating no secret reaches the wrong client.

---

## Loss of browser-side ALC version isolation

The server has one ALC per plugin (`PluginLoadContext`) and supports side-by-side library versions
(different Major/Minor load together). The **browser has effectively one ALC**
(`AssemblyLoadContext.Default`) and is **single-threaded** in .NET 10.

- **No side-by-side versions in the browser.** Two loaded clients referencing different versions of the
  same contracts assembly → the default ALC binds whichever loads first; a second `LoadFromStream` of
  the same simple-name/different-version conflicts or silently binds the already-loaded one → type
  identity bugs.
- **Mitigation:** enforce **one resolved version per contract simple-name per session**, client-side.
  The client loader keeps a `loadedAssemblies` map and refuses/deduplicates a second version, logging
  loudly. Document this as an explicit **reduction** of the plugin model in the browser: side-by-side
  versioning is a server-only feature; client contracts must be backward-compatible within a session.
- **Single-threaded:** projection-apply + render run on the WASM UI thread. Heavy client work
  (DndMapper image downscale) must stay in **JS web workers**, not C# threads (already the case).

---

## Asset serving & integrity

- Serve client DLLs at `/_plugins/{routeIdentifier}/client/*` with `ETag` / immutable caching keyed on
  version.
- The WASM framework files get SRI hashes automatically; **runtime-streamed plugin DLLs do not.** Carry
  a SHA-256 per client asset in `plugin.json` (`clientAssets`); the server computes/verifies on serve,
  and the client verifies downloaded bytes **before** `LoadFromStream`. This restores the integrity
  guarantee framework SRI would otherwise provide.

---

## Trust-model change (must be documented)

The architecture doc already states that `games/` is trust-equivalent to the host binary and that
third-party plugins run with full host privilege. That does not change. The **new exposure** is that a
third-party plugin now also runs **arbitrary code in every player's browser**. The third-party admin
toggle now gates browser-side code execution too. Update
`host/KnockBox/Specs/knockbox-platform-architecture.md` accordingly.

### New analyzer rules (extend KB1001–KB1004; ship in `KnockBox.Templates`)
| Rule | Flags |
|---|---|
| **KB1005** | `.Client` references a server-only type (`AbstractGameState`, `AbstractGameEngine`, `IWordListService`, server `KnockBox.Core` namespaces). Client may reference only `KnockBox.Core.Client` + `*.Contracts`. |
| **KB1006** | `.Client` sends a command not in its `*.Contracts`, or reads a state field absent from a `*.Contracts` view DTO (forces all server interaction through the typed contract; prevents reaching into projected secrets). |
| **KB1007** | A server engine returns a non-Contracts type from `ProjectFor` (prevents serializing `AbstractGameState` itself). |
| **KB1008** | `.Client` uses server `System.IO`/filesystem — must distinguish browser File/Blob download (allowed, e.g. DiceSimulator CSV) from server `System.IO` (flagged). |

---

## Goal assessment (honest)

| Dimension | Effect |
|---|---|
| **Per-connection server memory** | **Real reduction** — eliminates the Blazor Server per-circuit render tree + diff state + DI scope. Dominant per-user cost for a many-players-per-room host. The strongest reason to migrate. |
| **Per-room server memory** | **Unchanged** — `AbstractGameState`, lobby registry, singleton engines, `GameSessionState` all stay server-side at identical footprint. |
| **New server costs** | Per-player projection allocations + serialization, hub/group bookkeeping, SignalR buffers — partially offset the render-tree savings. The ceiling shifts from render-tree RAM toward projection CPU + SignalR throughput. |
| **New client costs** | First-load download of WASM runtime + framework + per-game UI (mitigated by per-game lazy load, immutable caching, and not shipping the dictionary). Interpreted plugin code adds a modest perf tax. |

**Bottom line:** the per-connection scalability win is genuine and is the right target for a
Jackbox-style host. The per-room win is not there. Both "keep it dynamic" goals survive, but the
browser-side plugin model is a **reduced** version of the server model. Set expectations accordingly.
