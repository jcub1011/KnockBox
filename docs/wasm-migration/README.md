# KnockBox WebAssembly Migration

This folder holds the migration plan for moving KnockBox from **Blazor Server** to a **Blazor Web App**
with game UI rendered client-side via **`InteractiveWebAssembly`**, backed by an explicit **SignalR**
transport, while keeping all authoritative game logic and the runtime plugin loader on the server.

> **Status:** Feasibility analysis + recommended plan. Gated on a **Phase 0 spike** (see
> [`02-risks.md`](./02-risks.md)) before committing to the full big-bang migration.

## Documents
1. [`01-target-architecture.md`](./01-target-architecture.md) — the target design: plugin tri-split,
   per-player state projection, the SignalR `GameHub`, session/reconnect rework, special cases.
2. [`02-risks.md`](./02-risks.md) — the two existential risks, the Phase 0 kill-criteria, the
   runtime-third-party-UI-under-trimming problem, loss of browser ALC version isolation.
3. [`03-work-breakdown.md`](./03-work-breakdown.md) — phased plan, per-game migration order,
   verification strategy.
4. [`04-per-game-migration-guide.md`](./04-per-game-migration-guide.md) — **the practical
   Phase 2 recipe**: step-by-step per-game migration (DiceSimulator is the reference
   implementation) plus the footguns hit during game #1 and how to avoid them. Start here when
   migrating a remaining plugin.

## Why migrate

The goal is **lower server memory** and **higher client scalability**. Blazor Server holds a
server-side render tree + diff state + DI scope **per connected circuit**; for a Jackbox-style host
with many players/spectators per room, that per-connection footprint is the dominant scaling cost.
Moving rendering to the browser eliminates it.

The pre-1.0 status makes this the right time for the breaking changes the migration requires.

## The central reframe

KnockBox is **multiplayer with server-authoritative state** — hidden roles (HiddenAgenda), word
answers (Codeword), card decks, fog-of-war (DndMapper), RNG, turn order, and rule enforcement all
live on the server and must not be trusted to the client. **Therefore the server does not go away.**
"Migrate to WASM" means:

- **Move to the browser:** game UI (Razor components, scoped CSS, JS interop, `wwwroot`).
- **Stays on the server (largely unchanged):** `AbstractGameEngine` + `AbstractGameState`, the
  filesystem/ALC-based `PluginLoader`, the lobby registry, sessions, WordService + its dictionary.
- **New:** an explicit SignalR `GameHub` replacing Blazor Server's implicit per-circuit render
  diffing, and a **per-player state projection** layer (the server sends each client only the view it
  is allowed to see).

Because almost every server-runtime dependency the codebase relies on (filesystem plugin discovery,
`AssemblyDependencyResolver`, isolated/collectible ALCs, `AsyncReaderWriterLock`, `AsyncLocal`) lives
in the **server-side logic layer that does not move**, most apparent "WASM blockers" are non-issues.
The real work is concentrated in the UI split, the projection layer, and supporting **runtime-loadable
third-party plugin UI** inside a trimmed WASM client.

## Decisions locked
| Decision | Choice |
|---|---|
| Target architecture | One server; Blazor Web App + `InteractiveWebAssembly` `.Client`; server keeps logic/state/loader; add SignalR `GameHub` |
| Third-party plugins | **Must stay runtime-loadable** — no host rebuild; UI downloads into the browser at runtime |
| Scope | **Big-bang** — all 12 games + WordService, including DndMapper |

## Verdict (honest)
- **Per-connection memory/scalability win: real.** The right reason to migrate.
- **Per-room state: unchanged.** `AbstractGameState`, lobby registry, engines, sessions stay
  server-side at the same footprint. The "less server state" intuition is *overstated* for per-room
  cost; the migration also adds new server CPU (projection + serialization).
- **Both "keep it dynamic" goals survive but with weakened browser-side guarantees:** no ALC version
  isolation, interpreted (non-AOT) plugin execution, a broad preserved framework surface, and manual
  asset integrity checks.
- **No hard blockers.** Two risks could still sink it — clear them in Phase 0 first.

## Two runtime facts that shape everything
- **Browser WASM is effectively single-threaded in .NET 10.** Blazor WASM multithreading is not
  expected until ~.NET 12 (2027). No synchronous blocking on the UI thread; heavy CPU work goes to JS
  web workers (DndMapper already does this).
- **Runtime assembly loading in the browser is weaker than on the server.** `LazyAssemblyLoader` and
  `AssemblyLoadContext.Default.LoadFromStream` exist, but there is **no filesystem, no
  `AssemblyDependencyResolver`, and effectively one ALC** (no side-by-side version isolation).
