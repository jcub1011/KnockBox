# Target Architecture

This is the destination design. It preserves the server-side core intact and concentrates change in
the UI tier, a new realtime transport, and a mandatory per-player projection layer.

```
ONE ASP.NET server process
├─ Server (unchanged core)
│   ├─ PluginLoader (filesystem + per-plugin ALC + AssemblyDependencyResolver)   ← UNCHANGED
│   ├─ AbstractGameEngine (singletons) + AbstractGameState (per-room, locked)     ← UNCHANGED
│   ├─ LobbyService, SessionServiceProvider, WordService (+ 4.3MB dictionary)     ← UNCHANGED
│   ├─ Per-player state projection layer                                          ← NEW
│   └─ SignalR GameHub (typed commands in / per-player projections out)           ← NEW
└─ KnockBox.Client (Blazor WebAssembly)
    ├─ Host shell, router, KnockBox.Core.Client SDK                               ← NEW
    └─ Game UI assemblies (downloaded at runtime, per game, on room entry)        ← MOVED here
         ↕ SignalR (typed DTOs from *.Contracts)
Browser renders UI only; the server is the single source of truth.
```

---

## 1. The plugin tri-split

Today each game is **one** Razor Class Library fusing three concerns: server logic, UI, and data.
Split each game into **three** assemblies. This mirrors the existing two-project library pattern
(`host/KnockBox.WordService` + `host/KnockBox.WordService.Contracts`), which already proves cross-ALC
type identity via contract promotion.

### (i) `KnockBox.{Game}` — server logic (stays the loaded plugin)
- Keeps `AbstractGameEngine` subclass, `AbstractGameState` subclass, server-only data (decks, word
  lists), `IGameModule`, and `plugin.json`.
- **Loaded exactly as today** by `PluginLoader` (`sdk/KnockBox.Core/Plugins/PluginLoader.cs`) into its
  per-plugin `PluginLoadContext` from `games/`. No change to the ALC model, `AssemblyDependencyResolver`,
  the forbidden-dependency gate, the Core-version gate, or contract promotion.
- **Loses** Razor pages/components, scoped CSS, client `wwwroot`.
- **Gains** a projection method (§2) and command handlers (§3). What was a Razor page calling
  `engine.RollDice(...)` becomes a hub command handler calling the same engine method.

### (ii) `KnockBox.{Game}.Contracts` — shared DTOs (loads in BOTH runtimes)
- Pure `record` DTOs: **commands** (client→server), **events** (one-shot server→client), and
  **view projections** (`record StateProjection<TView>(long Version, TView View)`).
- **Zero `KnockBox.*` references**, no plugin-targets import — identical constraints to
  `KnockBox.WordService.Contracts` (today only interfaces/POCOs/enums).
- Must be trimmer- and WASM-safe: `System.Text.Json` **source-generated** contexts so DTOs survive IL
  trimming without reflection roots (note the third-party exception in [`02-risks.md`](./02-risks.md)).
- **Server:** promoted into the default ALC by the existing
  `PluginLoader.PromoteExportedContracts` path so engine, hub, and projector share one CLR type.
  **Browser:** shipped to the client and loaded into `AssemblyLoadContext.Default` so the downloaded UI
  binds the same types the server serializes.

### (iii) `KnockBox.{Game}.Client` — browser UI (downloaded at runtime)
- Razor pages/components, scoped CSS, JS interop, `wwwroot`.
- References **only** `KnockBox.{Game}.Contracts` + a new WASM-safe `KnockBox.Core.Client` SDK.
- **Must not** reference server `KnockBox.Core` types (`AbstractGameState`, the engine) — those drag
  server-only surface (filesystem, `AsyncReaderWriterLock`, `ThreadSafeEventManager`) into the browser.
  Enforced by analyzer rule KB1005 ([`02-risks.md`](./02-risks.md)).
- This is the unit `LazyAssemblyLoader` pulls when a player enters the room.

### Manifest + loader evolution
`plugin.json` / `PluginManifest` (`sdk/KnockBox.Core/Plugins/PluginManifest.cs`) gain optional fields.
**Note:** `schemaVersion` is today a *validation constant* (`SupportedSchemaVersion = 1`) the parser
rejects anything else against — not a stored, forward-compatible field. Bumping to `2` therefore
requires a deliberate parser change to accept v2 (while still accepting v1); the new fields below are
additive, but the version gate is **not** automatically forward-compatible:
- `"clientAssembly": "KnockBox.{Game}.Client"`
- `"clientContracts": ["KnockBox.{Game}.Contracts"]`
- `"clientAssets": [{ "name": "...", "sha256": "..." }]` — integrity hashes for runtime-streamed DLLs.

Server side: after a game plugin is accepted, a new `IPluginClientAssetService` (in
`KnockBox.Platform`) maps `routeIdentifier → { client dll bytes, contract dll bytes, wwwroot files }`
from the plugin folder. Extend the existing `Program.MapPluginStaticAssets` (which already mounts
`wwwroot` at `/_content/{PluginName}`) to also serve client DLLs at
`GET /_plugins/{routeIdentifier}/client/{assembly}.dll`, gated by the same first-party/third-party
toggle `Program.cs` already reads before `AddKnockBoxPlatform`.

### Lazy-load: pull a game's UI only on room navigation
The `.Client` router can't statically know the 12 games. On navigating to `room/{routeIdentifier}/{code}`:
1. Fetch the client manifest for `{routeIdentifier}`.
2. Load `[contracts.dll, client.dll]`. First-party assemblies may be declared at build time via
   `<BlazorWebAssemblyLazyLoad>` + `LazyAssemblyLoader`. **Runtime-unknown third-party** assemblies
   can't use `LazyAssemblyLoader` (it only knows build-time-declared assemblies) — fetch bytes from the
   HTTP endpoint and `AssemblyLoadContext.Default.LoadFromStream(...)` directly.
3. Resolve the game root component by convention (e.g. `{rootNamespace}.GameRoot` implementing a new
   `IGameClientModule` from `KnockBox.Core.Client`) and render via `DynamicComponent`.

A new `IClientPluginLoader.LoadGameAsync(routeIdentifier)` in `KnockBox.Core.Client` hides both paths.

### Host shell — render mode, prerendering, and the game catalogue
- **Prerendering must be handled explicitly.** A Blazor Web App with `InteractiveWebAssembly`
  prerenders on the server by default, but game components now live in **runtime-downloaded assemblies
  the server does not reference** — they cannot be prerendered server-side. Game pages must opt out of
  prerender (`prerender: false`) or use a render-mode wrapper that defers entirely to the client;
  leaving the default on will fail at runtime.
- **The home-page game catalogue must cross the wire.** Today the home page enumerates the server-side
  `IGameModule` singletons to build the list. In the WASM client that list isn't available locally —
  add a small server endpoint (or initial-payload projection) returning the catalogue (name, route,
  tile, WIP flag) so the client router/home page can render it and drive lazy-load on navigation.

---

## 2. Per-player state projection (mandatory, security-critical, net-new)

Today, hidden info never reaches the browser because the **server renders** and only the HTML diff
crosses SignalR. In WASM the **client renders**, so anything the server sends is in browser memory and
inspectable. Naively broadcasting the full `AbstractGameState` leaks every secret.

Add a projection contract (the engine owns the rules, so it owns projection — or a paired
`IStateProjector<TState, TView>` per game):

```csharp
TView ProjectFor(User recipient, AbstractGameState state);   // server-only; runs under the read lock
```

- Runs **server-side only**, inside `WithExclusiveRead` for a consistent snapshot, returning a filtered
  view DTO from `KnockBox.{Game}.Contracts`: recipient's own secret role included; others redacted;
  deck reduced to counts + the recipient's own hand; DndMapper fog reduced to cells the recipient can
  see.
- The repo's existing server-side visibility logic becomes the projection core — e.g. DndMapper's
  `TokenVisibilityFilter`, `FogPolygonBuilder`, `ImageVisibilityFilter`; HiddenAgenda secret-task
  filtering; Codeword word pairs; card hands. These move from render-time filters to projection-time
  filters.

**Consequences**
- **Security improvement:** "the server never sends secrets to the wrong client" becomes an enforced,
  testable boundary.
- **Net-new work for all 12 games** and the most likely source of leak bugs. Required guardrails:
  (1) a **default-deny base projector** (unknown fields are not projected); (2) per-game **leak tests**
  ("player A's view never contains player B's secret"); (3) analyzer gates KB1006/KB1007.
- Cost scales with **players-per-room** (4–8 projections per state change), not global connections.
- The per-lobby subscriber computes projections under a **read lock** for a consistent snapshot. The
  RW lock allows concurrent readers but is **writer-preferred**, so a long fan-out (DndMapper, 4–8
  recipients) blocks mutations for its duration. Project into cheap snapshot DTOs *under* the lock,
  then **serialize/send outside it** to keep the write-stall short.

---

## 3. Realtime transport — SignalR `GameHub`

A single strongly-typed `GameHub : Hub<IGameClient>` in `KnockBox.Platform`.

- **Groups per lobby** for non-secret events. Because projections are per-player, secret-bearing state
  is sent per-connection (`Clients.Client(connId)`), so the hub tracks `userId → {connectionId}` per
  lobby in a hub-scoped singleton.
- **Commands in:** `Send(lobbyCode, GameCommandEnvelope)` — authenticate the caller's `User` from the
  connection's session, resolve the engine via the existing keyed
  `GetKeyedService<AbstractGameEngine>(routeIdentifier)`, and invoke the engine method, which mutates
  via `state.Execute`. Reuses the **sealed host-identity checks** already in `AbstractGameEngine`
  (`caller.Id == state.Host.Id`).
- **Projections out:** `IGameClient.ReceiveProjection(routeIdentifier, version, payload)` and
  `ReceiveEvent(...)` for one-shots. A monotonic `Version` per state lets the client drop
  out-of-order/duplicate projections after reconnect.

### Keystone: replace the `StateChangedEventManager` fan-out
Today each circuit's component subscribes to `state.StateChangedEventManager` (see
`sdk/KnockBox.Core/Components/Shared/LobbyPageBase.cs`). In the new model **the server subscribes once
per lobby**:
- On `LobbyService.CreateLobbyAsync`, register one server-side subscriber on
  `state.StateChangedEventManager.Subscribe(...)`.
- On notify — which **already fires outside the lock**, the load-bearing invariant in
  `AbstractGameState` — the subscriber takes a read lock, computes `ProjectFor(recipient)` per
  connected member, and pushes each projection. Projection + serialization happen exactly where
  `InvokeAsync(StateHasChanged)` used to: outside `Execute`.
- `PlayerUnregistered` (also fires outside the lock) maps to hub group-removal + a re-projection to the
  remaining players.

### Client subscription / disposal
A new `HubLobbyPageBase<TView>` in `KnockBox.Core.Client` replaces `LobbyPageBase<TGameState>`: it
opens/joins the hub connection, registers `ReceiveProjection` → store `TView` + `StateHasChanged`, and
on `Dispose` leaves the group / disposes the subscription — the same disposal discipline as today's
`_stateSubscription?.Dispose()`, but against the hub. `DisposableComponent` moves to
`KnockBox.Core.Client` (pure `ComponentBase`, no server dependency).

### Reconnection / grace-period rework
`SessionServiceProvider` (`sdk/KnockBox.Platform/Services/State/Shared/SessionServiceProvider.cs`)
caches session-scoped services with a **1-minute eviction grace** (`EvictionDelay`, verified),
reference-counted via a lifecycle token. The cache key is `RegistrationKey(SessionToken, ServiceType)`
— it keys on the **`SessionToken`, not the circuit** (and not directly on `userId`), so it is
transport-agnostic and reusable near-verbatim. What changes is the caller:
- `OnConnectedAsync` acquires a session-service reference (cancels pending eviction — the
  `ReferenceCount == 1` path).
- `OnDisconnectedAsync` releases it (starts the 1-minute timer). SignalR auto-reconnect within the
  window re-acquires before eviction fires — **provided the reconnecting connection presents the same
  `SessionToken`** (see "Hub identity" below). This is the load-bearing detail that makes hub reconnect
  equivalent to today's circuit reconnect.
- **Multi-tab:** tracking the set of connections per session means eviction starts only when the
  *last* connection for a `SessionToken` drops — strictly better than today's per-circuit model
  (removes "second tab kills first tab's session"). This relies on the `SessionToken → {connId}`
  mapping the hub maintains (§3 above).

### Hub identity — establishing the user without a circuit
Today `IUserService` and `IGameSessionService` are **scoped per Blazor circuit**; the circuit *is* the
identity boundary. WASM has no circuit, so the hub must establish identity itself:
- The SignalR connection must authenticate on its handshake (e.g. the existing auth cookie, or a
  short-lived token issued by the server shell) and resolve the caller's `User` + `SessionToken` from
  that — **not** trust a client-supplied identity in the command envelope.
- That resolved `SessionToken` is what keys `SessionServiceProvider` (above) and what reconnect must
  re-present. The exact mechanism (cookie vs. token, and how the `.Client` shell obtains it) is an
  **explicit Phase 1 design item** and should be exercised in the Phase 0 spike — it underpins both
  command authorization (`caller.Id == state.Host.Id`) and the reconnect/grace semantics.

---

## 4. Special cases

### WordService — stays SERVER-side (confirmed)
`IWordListService` (`host/KnockBox.WordService.Contracts/IWordListService.cs`) is a validation/lookup
oracle (`IsValidWord`, `IsInPool`, `GetWordCount`, …) over a ~4.3 MB dictionary. Word validity is an
**authoritative rule** and the dictionary is large — **do not ship it to browsers** (bloat + lets
clients enumerate answers / cheat). Keep `KnockBox.WordService` exactly as today.
- **Client→server validation flow (Spardle / Tracery / LinkedList):** a guess becomes
  `SubmitGuessCommand(word)`; the server engine calls `IWordListService.IsValidWord` inside `Execute`,
  mutates state (accept/reject + scoring), and the per-player projection carries back validity + score.
  The client may do a cheap optimistic local check (length/charset) but **never** authoritative
  dictionary membership. Strictly better than today.
- `IWordListService.GetWord` returns a `ReadOnlySpan<byte>` (`IsValidWord`/`IsInPool`/`GetWordCount`
  return `bool`/`int`). `ReadOnlySpan<T>` is a ref struct — it can't cross `await` or serialize — so it
  stays server-internal and never enters a view DTO. KB1007 enforces this.

### DndMapper — the long pole (~18.3k C# LOC across ~115 files + ~20 JS files)
Its rendering tier is **already** in the browser via JS interop — canvas/WebGL/worker JS
(`wwwroot/js/dndMapper*.js` incl. `dndMapperImageDownscaleWorker.js`, the `dice-box-threejs` lib).
Those JS files move into `KnockBox.DndMapper.Client/wwwroot` largely unchanged; interop semantics are
the same under `InteractiveWebAssembly`. Client IndexedDB persistence is **not** DndMapper-local: it
uses the shared SDK `ScopedIndexedDbService`
(`sdk/KnockBox.Core/Services/Storage/IndexedDb/ScopedIndexedDbService.cs`) + `indexedDbService.js` +
`BlobShare`, which already run via JS interop today and carry over (see Phase 1). The hard parts:
- **Collaborative + fog-of-war projection:** authoritative map/fog/token state stays in server
  `AbstractGameState`; visibility filters (`TokenVisibilityFilter`, `FogPolygonBuilder`,
  `ImageVisibilityFilter`) **move into the server-side projection** so each player receives only what
  they can see. This is the largest projection to write and the highest-value security boundary in the
  app (a player must not receive hidden GM map regions).
- **VTF (zip) import:** read in the browser, upload the large binary to the server for authoritative
  ingest over a dedicated **HTTP POST** (not the hub — SignalR is poor at large binary), then project
  the resulting map. The packager JS stays client-side.

### DiceSimulator CSV export — client-side
Pure client work over the client's own projected roll log; generate + download entirely in the browser
via JS (the existing file-download interop is the template). No server round-trip, no server
filesystem. Analyzer rule KB1008 must allow browser File/Blob download while still flagging server
`System.IO`.

### High-frequency interaction (DrawnToDress strokes, DndMapper token drag / fog paint)
Some games emit updates far faster than turn-based games — live drawing strokes (DrawnToDress) and
continuous token drag / fog painting (DndMapper). Blazor Server's render-diff batching, which today
implicitly coalesces these, is exactly what the explicit hub removes. The hub model therefore needs a
deliberate **throttle / batch / delta** design for these streams (coalesce strokes to a tick, send
deltas rather than full projections, debounce drag), or it will flood SignalR. Treat this as dedicated
design work for DrawnToDress and DndMapper — not the default per-state-change projection path.
