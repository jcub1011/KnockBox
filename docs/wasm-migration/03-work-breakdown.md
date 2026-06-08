# Work Breakdown & Verification

A phased plan that de-risks a big-bang migration. The cutover is big-bang, but the SDK/platform branch
must stay green while games migrate one-by-one behind it.

---

## Phase 0 — Spike / kill-criteria (FIRST, time-boxed)

Prove the two things that can sink the migration **before** committing all 12 games. See
[`02-risks.md`](./02-risks.md).

1. **Runtime third-party UI in a trimmed WASM app.** Build a throwaway trimmed `.Client`;
   `LoadFromStream` a DLL not in its build graph; confirm it renders and round-trips a hub command.
   *Kill if:* preserved-surface/reflection-JSON cost is unacceptable or it doesn't work trimmed.
2. **Per-player projection + hub fan-out end-to-end** on HiddenAgenda (cleanest secret model); prove no
   secret reaches the wrong client.

**Exit gate:** both spikes pass with acceptable download size and a clean projection boundary.

---

## Phase 1 — SDK / platform plumbing (no game touches)

Land all shared infrastructure on a branch that compiles green. Games are not yet split.

- **`KnockBox.Core.Client`** (new WASM-safe SDK): `DisposableComponent`, `HubLobbyPageBase<TView>`,
  `IClientPluginLoader` / `IGameClientModule`, a JSON source-gen base. **Client storage largely already
  exists** — the shared `ScopedIndexedDbService` / `IIndexedDbService` / `indexedDbService.js` /
  `BlobShare` stack runs via JS interop today; carry it over / adapt rather than building new wrappers.
- **`GameHub` + `IGameClient`** in `KnockBox.Platform`, with per-lobby groups and `userId → {connId}`
  bookkeeping.
- **Projection base:** `IStateProjector` / `ProjectFor` hook + default-deny base projector + the single
  per-lobby server subscriber wired in `LobbyService.CreateLobbyAsync`.
- **Loader / manifest evolution:** `plugin.json` schemaVersion 2 — note `schemaVersion` is today a
  validation *constant* (`SupportedSchemaVersion = 1`), so accepting v2 is a **parser change**, not a
  free additive bump. Add `clientAssembly`, `clientContracts`, `clientAssets` + SHA-256;
  `IPluginClientAssetService`; the `/_plugins/{route}/client/*` endpoint (server `PluginLoader` itself
  unchanged).
- **Build / staging pipeline:** extend `Directory.Plugin.targets` (today stages one DLL set into
  `games/`) to also build + stage the `.Client` + `.Contracts` assemblies and **compute the
  `clientAssets` SHA-256 at build time**, so the serve endpoint and the client integrity check have
  hashes to verify against.
- **Session rework:** move acquire/release of `SessionServiceProvider` lifecycle tokens from
  `GameSessionService` (circuit) to hub `OnConnectedAsync` / `OnDisconnectedAsync`, keyed on the
  connection's `SessionToken`; confirm the existing 1-minute eviction tests still pass with the new
  caller.
- **Hub identity / auth (foundational):** define how the SignalR connection authenticates on handshake
  and resolves `User` + `SessionToken` without a circuit (cookie vs. token, and how the `.Client` shell
  obtains it). Underpins command authorization and the reconnect/grace semantics — design here, prove in
  Phase 0.
- **Analyzer KB1005–KB1008**; update `KnockBox.Templates` (`dotnet new knockbox-game`) to scaffold the
  tri-split.
- **Host shell:** convert `KnockBox` to a Blazor Web App + new `KnockBox.Client` project
  (`InteractiveWebAssembly`); migrate the home page + lobby-creation flow. Decide the **render-mode /
  prerender** strategy (game pages can't prerender server-side — their assemblies are runtime-loaded);
  add a **server endpoint for the game catalogue** the client home page enumerates.

---

## Phase 2 — Per-game migration (×12, repeated pattern, parallelizable)

For each game, apply the same recipe:
1. Extract **Contracts** DTOs (commands + view).
2. Write **`ProjectFor`** (the risky part — default-deny; project only what the recipient may see).
3. Move Razor / scoped CSS / `wwwroot` / JS into **`{Game}.Client`**, talking to the hub via contracts.
4. Delete the server-side Razor pages + direct engine calls from UI.
5. Add **projector leak tests** + **hub integration tests**.

Order easy → hard so the pattern is proven on simple games before the expensive ones:

| Order | Game | Notes |
|---|---|---|
| 1 | DiceSimulator | Simplest; no hidden state; client-side CSV export validates File/Blob path |
| 2 | AlphaChain | Pure in-memory symmetric logic |
| 3 | Tracery | Word validation → server (WordService stays server-side) |
| 4 | LinkedList | Embedded word list → server |
| 5 | Spardle | Word validation → server |
| 6 | CardCounter | Deck/hand projection |
| 7 | Operator | Per-player hands |
| 8 | TaskMaster | Uniform visibility |
| 9 | Codeword | Hidden word pair + role assignment projection |
| 10 | DrawnToDress | Canvas/drawing state over the hub — needs stroke throttle/batch/delta design (no more render-diff coalescing) |
| 11 | HiddenAgenda | Secret tasks + rivalry target projection (already spiked in Phase 0) |
| 12 | **DndMapper** | **Long pole (~18.3k C# LOC, ~115 files + ~20 JS files)** — fog/visibility projection, VTF binary upload, WebGL/IndexedDB/worker JS port, large collaborative state, high-frequency token-drag/fog-paint stream |

---

## Phase 3 — Hardening

- Reconnect storms and multi-tab behavior.
- Projection-leak fuzzing across all games.
- Download-size budget enforcement; caching + integrity verification.
- Update `host/KnockBox/Specs/knockbox-platform-architecture.md` for the new trust model and the
  reduced browser-side plugin guarantees.

---

## Highest-risk items (what could sink it)

1. **Trimming vs. runtime third-party UI** — existential; Phase 0 gate. Worst case forces "no trimming"
   (bigger download) or "first-party-only client UI" (breaks a stated goal).
2. **Projection leaks** — security; every game is a fresh chance to leak. Default-deny base + mandatory
   leak tests + KB1006/KB1007.
3. **DndMapper collaborative + fog projection + VTF binary** — schedule; biggest single chunk, deepest
   JS-interop + binary-upload surface.
4. **Loss of browser ALC version isolation** — correctness for third-party contracts; mitigated by
   single-version-per-session enforcement, but a real reduction from the server guarantee.
5. **Big-bang coordination** — keep the Phase 1 SDK branch green while games migrate one-by-one behind
   it, even though the final cutover is big-bang.

---

## Verification

**Build**
- `dotnet build sdk/KnockBox.Sdk.slnx`
- `dotnet build host/KnockBox.Host.slnx` (also stages plugins into `games/`)
- WASM client publish must trim green: `dotnet publish host/KnockBox/KnockBox.csproj -c Release`

**Automated tests**
- `dotnet test sdk/KnockBox.Sdk.slnx` and `dotnet test host/KnockBox.Host.slnx`
- New **per-game projection leak tests** — a player's projected view never contains another player's
  secret.
- New **hub integration tests** — command → engine mutation → projection broadcast to the right
  recipients.
- Analyzer tests for KB1005–KB1008 in `KnockBox.Plugins.AnalyzerTests`.

**Phase 0 spike acceptance**
- A trimmed `.Client` loads an out-of-build-graph DLL via `LoadFromStream`, renders it, and round-trips
  a hub command.
- HiddenAgenda projection proves no secret leaks to the wrong connection.

**Manual end-to-end**
- `dotnet run --project host/KnockBox/KnockBox.csproj`; open two browsers; create + join a lobby.
- Confirm: realtime updates flow over the hub; the game UI downloads only on room entry (check the
  network tab); reconnect within 1 minute restores the session; and via browser devtools (network +
  memory) **no hidden state** is present in client payloads.
