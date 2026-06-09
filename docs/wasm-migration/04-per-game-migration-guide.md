# Per-Game Migration Guide (Phase 2)

A step-by-step, footgun-aware recipe for migrating a game plugin from the Blazor Server
model to the WASM tri-split. **DiceSimulator is the reference implementation** — when in
doubt, copy what `host/KnockBox.DiceSimulator{,.Contracts,.Client}` does.

> **Status:** DiceSimulator (game #1) and CardCounter (game #2) are migrated and the shared
> infrastructure is proven. Game #2 added the **server-owned tick loop** (timed games) and a
> **WASM `CountdownClock`** — both now reusable. Most of the footguns below are solved once in
> the SDK/platform; this guide tells you which steps are per-game and which are handled for you.

Read [`01-target-architecture.md`](./01-target-architecture.md) and
[`03-work-breakdown.md`](./03-work-breakdown.md) first. The migration order (easy → hard)
and per-game special cases live in the work-breakdown.

---

## 0. What's already shared (do NOT rebuild per game)

These were built during game #1 and work for every game automatically:

- **Runtime client loader** (`RuntimePluginLoader`) downloads the entry assembly **and its
  declared contract dependencies**, verifies SHA-256, and loads them into the browser ALC.
- **Manifest dependency serving** — `PluginClientAssetService` emits each game's
  `clientContracts` as manifest `dependencies` with hashes; the build stages them.
- **Hub lifecycle** — `GameHub` (commands in / projections out), per-lobby projection
  fan-out (`GameViewCoordinator`), session acquire/release with a 1-minute grace, and
  **lobby close-on-host-leave** (immediate on explicit leave, grace-based on disconnect),
  including kicking remaining players (`lobby-closed` event).
- **`HubLobbyPageBase<TView>`** — opens the hub, joins, applies projections into `View`,
  exposes `LobbyCode`, handles `lobby-closed` (navigates home), and provides
  `SubmitCommandAsync` / `LeaveAsync`.
- **`RoomCodeButton`** (in `KnockBox.Core.Client`) — full reveal/modal/copy/long-press
  parity, parameterized by `Code` + `JoinUrl`.
- **Host shell** — `MainLayout` suppresses its header on WASM routes; `Home` branches
  `IsClientGame` games to the hub-create flow; `IsHomePage` tolerates query strings.
- **Generic runtime-game page** — `host/KnockBox.Client/Pages/RuntimeGameLobby.razor` is
  `@page "/room/{Route}"` + `"/room/{Route}/{Code}"`, serving every migrated game; no per-game
  host-page work remains (done at game #2).
- **Server-owned tick loop** — `LobbyTickService` (in `KnockBox.Platform`) drives time-based
  FSM transitions for any engine implementing `IServerTickHandler` (`KnockBox.Core`), replacing
  the old per-host browser-circuit tick. Engines opt in; the host has no compile-time knowledge.
- **`CountdownClock`** (in `KnockBox.Core.Client`) — renders a phase countdown from a
  server-projected deadline timestamp, reusing the existing `_content/KnockBox.Core/js/countdownClock.js`.
- **Analyzers** KB1005–KB1008 enforce the boundaries at build time.

What you still do **per game**: extract Contracts, write `ProjectFor` + command handlers,
port the UI, declare the staging/manifest bits, recreate the game's custom header, ship its
stylesheet, and add tests.

**The generic host page is done (game #2).** Each new game now needs only **one
`WasmRouteTable` prefix entry** (`"room/{route}"`) — do NOT collapse the list to a blanket
`"room/"`: un-migrated games also live under `room/` and must keep rendering under
`InteractiveServer` (with the default header). The generic `@page "/room/{Route}/{Code}"` is
safe alongside un-migrated server games because Blazor's literal-segment routes
(`/room/alpha-chain/{code}`) outrank the parameterized one; only routes listed in
`WasmRouteTable` flip to the static→WASM transition.

---

## 1. Create `KnockBox.{Game}.Contracts`

Pure DTOs, **zero `KnockBox.*` references, no targets import** (mirror
`host/KnockBox.DiceSimulator.Contracts`). Pin `Version`/`AssemblyVersion`/`FileVersion`.

Put in it:
- **Pure data types the UI needs** — move enums/records that currently live in the server
  project (e.g. DiceSimulator moved `DiceType`, `RollMode`, `DiceRollEntry`, `DiceRollAction`).
  The server then references the Contracts project for them.
- **The view DTO** projected to clients (e.g. `DiceSimulatorView`). Include a `RecipientId`
  field so the UI can compute "am I the host / is this my row" (replaces the server page's
  `UserService.CurrentUser` checks).
- **Command name constants** (`public static class {Game}Commands { public const string ... }`).
- **A source-gen JSON context** so DTOs survive trimming:
  ```csharp
  [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
  [JsonSerializable(typeof({Game}View))]
  [JsonSerializable(typeof({CommandPayload}))]
  public partial class {Game}ContractsJsonContext : JsonSerializerContext;
  ```

> **Footgun — enum-keyed dictionaries don't round-trip.** `System.Text.Json` serializes a
> `Dictionary<SomeEnum, T>`'s keys as the enum's **numeric** string even with the string-enum
> converter, and the client may fail to read them. **Key wire dictionaries by `string`** in the
> view DTO (DiceSimulator's `PlayerStatsView.RollCountByDie` is `IReadOnlyDictionary<string,int>`,
> mapped from the server's `Dictionary<DiceType,int>` via `key.ToString()`). Cover it in the
> serialization test.

---

## 2. Server: projector + command handler

**Projector** — `host/KnockBox.{Game}/Services/Projection/{Game}StateProjector.cs`:
```csharp
public sealed class {Game}StateProjector
    : AbstractStateProjector<{Game}GameState, {Game}View>
{
    public override {Game}View ProjectFor({Game}GameState state, Guid recipientId) { /* build view */ }
}
```
- **Default-deny is the security boundary.** Build the view field-by-field. Copy a secret
  field **only** when the entry being projected belongs to `recipientId` (see
  `HiddenAgendaProjector` for the secret-bearing pattern). KB1007 enforces the view is a
  Contracts type, not server state.
- Runs under the read lock the coordinator holds — read snapshot-returning members only.

**Engine** implements both interfaces (mirror `HiddenAgendaGameEngine` /
`DiceSimulatorGameEngine`):
```csharp
public class {Game}GameEngine(...)
    : AbstractGameEngine<{Game}GameState>, IGameStateProjector, IGameCommandHandler
{
    private readonly {Game}StateProjector _projector = new();
    public object? ProjectFor(AbstractGameState state, Guid recipientId)
        => ((IGameStateProjector)_projector).ProjectFor(state, recipientId);

    public async ValueTask<Result> HandleCommandAsync(
        User caller, AbstractGameState state, string command, string? payloadJson, CancellationToken ct = default)
    {
        if (state is not {Game}GameState s) return Result.FromError("Invalid game state.");
        return command switch
        {
            {Game}Commands.Start => await StartAsync(caller, s, ct),
            {Game}Commands.SomeAction => SomeActionFromPayload(caller, s, payloadJson),
            _ => Result.FromError($"Unknown command [{command}].")
        };
    }
}
```
- Deserialize command payloads with **string-enum + case-insensitive** options so client
  JSON parses (see `DiceSimulatorGameEngine.CommandJsonOptions`).
- Lobby creation is **not** a command — it flows through `GameHub.CreateRoom` →
  `LobbyService.CreateLobbyAsync` → the engine's existing `CreateStateAsync`. No change there.
- No DI changes: `AddGameEngine<TEngine>()` already registers the engine keyed by route, and
  the hub/coordinator resolve the projector + command handler off that keyed instance.
- **Timed games (auto-advance on a deadline):** also implement `IServerTickHandler` on the
  engine (`void Tick(AbstractGameState, DateTimeOffset now)`, delegating to the existing
  per-frame tick). The platform's `LobbyTickService` calls it ~4 Hz for every open lobby — the
  old host-circuit tick (`LobbyPageBase.TryGetHostTick`) is gone in WASM. Project the current
  deadline as an absolute UTC timestamp (e.g. `PhaseEndsAtUtc` + a duration) so the client can
  render a `CountdownClock`; the server stays authoritative on expiry. (CardCounter is the
  reference for all three interfaces + the countdown.)

> **Footgun — never compare `User` by reference in a command handler.** `User` is a plain
> class (reference equality). The hub resolves a **fresh `User` per command** from the
> connection token, so `if (user != state.Host)` (reference) **always rejects the real host**.
> Compare by id: `if (user.Id != state.Host.Id)`. (This is why DiceSimulator's "Clear History"
> silently failed for the host.) Add a test that calls the handler with a *different* `User`
> instance carrying the host's id.

---

## 3. Create `KnockBox.{Game}.Client`

`Sdk="Microsoft.NET.Sdk.Razor"`, `<StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>`,
references **only** `KnockBox.Core.Client` + `{Game}.Contracts`, imports
`..\Directory.Client.targets`, and declares the contracts DLL to stage:
```xml
<ItemGroup>
  <ClientContractAssembly Include="$(TargetDir)KnockBox.{Game}.Contracts.dll" />
</ItemGroup>
<Import Project="..\Directory.Client.targets" />
```
Files (copy DiceSimulator.Client):
- `_Imports.razor` — include `@using KnockBox.Core.Client.Components`,
  `@using KnockBox.Core.Client.Json`, `@using KnockBox.{Game}.Contracts`, `@using System.Text.Json`.
- `GameClientModule.cs` — `IGameClientModule` (`RouteIdentifier`, `GameRootComponentType => typeof(GameRoot)`).
- `GameRoot.razor` — `@inherits HubLobbyPageBase<{Game}View>`; `[Parameter] Code`,
  `[Parameter] PlayerName`; override `RouteIdentifier`, `LobbyUri => $"room/{route}/{Code}"`,
  `DisplayName`. Pass a `SourceGenProjectionDeserializer<{Game}View>(...)` to the base ctor.
  Send commands via `SubmitCommandAsync(...)`. Render the custom header (step 5).
- Any client-side service (e.g. a CSV generator) — pure stream/text work is fine
  (KB1008 only flags path-based `System.IO`).

> **Footgun — the streamed `.Client` DLL has no scoped-CSS bundle and can't use HeadContent.**
> See step 4. Also: client JSON must be trim-safe — serialize command payloads with the
> source-gen context, and for trivial payloads (a bare `Guid`) build the JSON string by hand
> (`$"\"{id}\""`) rather than calling reflection-based `JsonSerializer.Serialize`.

---

## 4. Styling (the most error-prone UI step)

The old scoped `.razor.css` does **not** ship: `StaticWebAssetsEnabled=false` produces no
`{Assembly}.styles.css`, and a runtime-loaded WASM component can't use `<HeadContent>` (the
`HeadOutlet` runs in a different render mode).

**Recipe:**
1. Move the page's CSS into a **plain stylesheet in the SERVER plugin's**
   `wwwroot/css/{game}.css` (it's mounted at `/_content/KnockBox.{Game}/`).
2. **Scope every rule under a root wrapper class** (DiceSimulator uses `.ds-root`). The old
   scoped CSS relied on Blazor's per-element attribute; de-scoped globally, bare selectors
   (`h3`, `label`, `input`, `select`, `table`) **leak into the whole app**. Prefix them:
   `.ds-root h3 { … }`.
3. In `GameRoot.razor`, wrap the UI in `<div class="ds-root">` and **link the stylesheet from
   the component markup** (browsers honor a body-placed `<link>`):
   ```razor
   <link rel="stylesheet" href="_content/KnockBox.{Game}/css/{game}.css" />
   ```

> **Footgun — a top header inside the scrolling article shrinks to nothing.** WASM game UI
> renders inside `MainLayout`'s `<article>`, a `flex-direction:column` scroll container. A
> fixed-height header is a flex item with default `flex-shrink:1`, so it collapses as content
> grows. Give the header `flex-shrink: 0`.

> **Footgun — JS interop: a `<script>` rendered into the body does NOT execute.** Convert the
> game's JS to an **ES module** (`export function ...`) in the server `wwwroot/js/`, and import
> it from the component:
> ```csharp
> _mod ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/KnockBox.{Game}/js/file.js");
> await _mod.InvokeVoidAsync("fn", args);
> ```

---

## 5. Custom header

The server header (`{Game}Header.razor` + `GetCustomHeader()`) used server-only services
(`INavigationService`, `IGameSessionService`, `RoomCodeButton` from `KnockBox.Core`) — all
forbidden in a `.Client` assembly by KB1005. Recreate a **WASM-safe** header in the client
project (copy `DiceSimulatorHeader.razor`):
- Inject `NavigationManager` for navigation.
- Use the shared **`KnockBox.Core.Client.Components.RoomCodeButton`** with `Code="@Code"`,
  `JoinUrl`, and the game's cover/value CSS classes. Build `JoinUrl` as
  `$"{Nav.BaseUri}?join={Uri.EscapeDataString(Code)}"`.
- Expose `[Parameter] EventCallback OnLeave`; wire brand + leave button to it. `GameRoot`
  passes `OnLeave="LeaveAsync"` (the base method) so a leaving host closes the lobby
  immediately. `GameRoot` also passes `Code="@LobbyCode"` (resolved by the base).
- For the room-code reveal, define cover/value styles with a **grid-overlap** in the game CSS,
  prefixed with the actions container to out-specify `RoomCodeButton`'s scoped `a` reset
  (see DiceSimulator's `.ds-header-actions .ds-header-code{,-cover,-value}` rules).

> **Footgun — the default header double-renders.** Handled by shared infra (`MainLayout`
> skips its header on `WasmRouteTable` routes), but **only if you add your route** in step 7.
> If you forget, you get two header bars.

---

## 6. Manifest, staging, and host wiring

**`host/KnockBox.{Game}/plugin.json`** — add (placeholder zero hash is fine; the real
per-serve hash comes from the build-time `assets.sha256.json` sidecar):
```json
"clientAssembly": "KnockBox.{Game}.Client",
"clientContracts": ["KnockBox.{Game}.Contracts"],
"clientAssets": [
  { "name": "KnockBox.{Game}.Client", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" }
]
```

**`host/KnockBox.{Game}/KnockBox.{Game}.csproj`** — reference the Contracts project AND
declare it as a private ALC dependency to stage:
```xml
<ProjectReference Include="..\KnockBox.{Game}.Contracts\KnockBox.{Game}.Contracts.csproj" />
<ItemGroup>
  <PluginPrivateAssembly Include="$(TargetDir)KnockBox.{Game}.Contracts.dll" />
</ItemGroup>
```

> **Footgun (the worst one) — the server plugin silently fails to load and the tile vanishes.**
> The server engine/state/projector now reference `{Game}.Contracts` at runtime, but it is a
> *private* dependency not promoted to the default ALC. If it isn't staged into
> `games/KnockBox.{Game}/`, the plugin's `PluginLoadContext` throws
> `FileNotFoundException: '...Contracts...'` and the loader **skips the entire plugin** — no
> error in the UI, the game just disappears from the home page and `/api/games`. The
> `<PluginPrivateAssembly>` item above makes `Directory.Plugin.targets` stage it. (Note this is
> distinct from `<ClientContractAssembly>` in the *client* csproj, which stages the contracts
> DLL into `games/KnockBox.{Game}/client/` for the browser. You need **both**.)

**`host/KnockBox/KnockBox.csproj`** — build + stage the client (Contracts comes transitively):
```xml
<ProjectReference Include="..\KnockBox.{Game}.Client\KnockBox.{Game}.Client.csproj"
                  ReferenceOutputAssembly="false" Private="false" />
```

**`host/KnockBox.Host.slnx`** — add the `.Contracts`, `.Client`, and any new test project.

---

## 7. Routing + launch

- Add `"room/{route}"` to `WasmRouteTable.Prefixes`
  (`sdk/KnockBox.Core.Client/Routing/WasmRouteTable.cs`). This makes `App.razor` keep the
  route static (static→WASM transition) **and** makes `MainLayout` suppress its header.
- `RuntimeGameLobby.razor` already serves every route generically — no per-game edit.
- **Home page launch needs no per-game change** — `Home.IsClientGame` already detects any
  game with a `clientAssembly` and routes create/join through the hub.

> **Footgun — `ArgumentException: URI is not contained by the base URI`.** Navigation to a WASM
> route must use an **absolute** URI. `MainLayout.OnLocationChanging` calls
> `ToBaseRelativePath(TargetLocation)`, which throws on a root-relative path (`/room/...`). The
> shared `Home.NavigateToWasm` builds absolute URIs (`{BaseUri}{path}`) and `IsHomePage` strips
> the query string (so `/?join=CODE` is still "home" and the handler early-returns). Don't
> reintroduce root-relative `NavigateTo(..., forceLoad:true)` for these routes.

---

## 8. Delete the server UI

Remove from `host/KnockBox.{Game}/`: `Pages/*Lobby.razor{,.cs,.css}`,
`Components/{Game}Header.razor{,.css}`, the `GetCustomHeader()` override in the module, the
server-side CSV/interop service (moved to client), and the now-orphaned `_Imports.razor` if no
Razor components remain. Keep `wwwroot/` (now holding the de-scoped CSS + ES-module JS + tile),
`plugin.json`, the engine/state/play-log, and the projector.

> **Footgun — orphaned `_Imports.razor`.** If you delete all `.razor` files but leave
> `_Imports.razor`, it still compiles and references the deleted `.Pages` namespace →
> `CS0234`. Delete it.

> **Known regression to track — play-log on leave.** `BuildOnLeavePlayLog` was a server
> `LobbyPageBase` hook with no hub-side equivalent yet. DiceSimulator's on-leave play log is
> currently dropped. Keep `{Game}PlayLogMetadata` server-side; a shared hub-side play-log hook
> (fire from `GameHub.OnDisconnectedAsync` / session disposal) is pending infra.

---

## 9. Tests

- **Projector leak / serialization test** — build a state with host + players, project for a
  recipient, assert it returns the Contracts view, and **round-trip it through the hub's JSON
  options** (string-enum + case-insensitive). For secret-bearing games, assert player A's view
  never contains player B's secret. Explicitly assert any string-keyed dict round-trips.
- **Hub command tests** — call `((IGameCommandHandler)engine).HandleCommandAsync(...)` and
  assert the mutation + that `ProjectFor` reflects it. **Include a host-only command invoked
  with a *fresh* `User` instance carrying the host's id** (guards the reference-equality footgun).
- **Update existing tests** — change `using` for moved types to `KnockBox.{Game}.Contracts`;
  add a Contracts `ProjectReference` to the test csproj.
- **Lobby lifecycle is already covered** by `sdk/KnockBox.PlatformTests/Unit/HubLobbyLifecycleTests.cs`
  (host-leave close, grace, reconnect, host-only close) — game-agnostic; you don't re-test it.

---

## 10. Verify

```
dotnet build sdk/KnockBox.Sdk.slnx
dotnet build host/KnockBox.Host.slnx     # stages games/{Game}/ + games/{Game}/client/
dotnet test  host/KnockBox.Host.slnx
dotnet publish host/KnockBox/KnockBox.csproj -c Release   # WASM client must trim green
```
Then `dotnet run` and check at runtime (these catch the load/serve footguns that build/test miss):
- `GET /api/games` includes the game with `"hasClientUi": true` (else the plugin failed to
  load — check startup logs for `FileNotFoundException`, see step 6).
- `GET /_plugins/{route}/client/manifest.json` lists the contracts under `dependencies`.
- `GET /_content/KnockBox.{Game}/css/{game}.css` returns `200 text/css`.
- Two browsers: create + join, play, leave. The game UI downloads only on room entry; the
  host leaving closes the lobby and bounces the other player home; projected payloads contain
  no hidden state.

> **Footgun — green build/test ≠ working.** Every load/serve/CSS/JS footgun above passes
> `dotnet build`, `dotnet test`, and `dotnet publish` and only manifests at runtime in the
> browser. Always do the `dotnet run` checks above.

---

## Per-game checklist

- [ ] `{Game}.Contracts` project: moved data types, view DTO (with `RecipientId`), command
      constants, source-gen context (`UseStringEnumConverter`), string-keyed wire dicts.
- [ ] Server: `{Game}StateProjector` (default-deny); engine implements `IGameStateProjector`
      + `IGameCommandHandler`; command payload JSON options; **`User.Id` comparisons**.
- [ ] Server csproj: Contracts `ProjectReference` + **`<PluginPrivateAssembly>`**.
- [ ] `{Game}.Client` project: csproj (`StaticWebAssetsEnabled=false`, `<ClientContractAssembly>`,
      import `Directory.Client.targets`), `_Imports`, `GameClientModule`, `GameRoot : HubLobbyPageBase<TView>`.
- [ ] CSS de-scoped under a root class in server `wwwroot/css`, `<link>`ed from `GameRoot`;
      header `flex-shrink:0`; JS as ES module.
- [ ] Custom header recreated (WASM-safe `RoomCodeButton`, `OnLeave="LeaveAsync"`, `Code="@LobbyCode"`).
- [ ] `plugin.json` client fields; host csproj client `ProjectReference`; slnx entries.
- [ ] `WasmRouteTable` prefix; `RuntimeGameLobby` serves the route.
- [ ] Server UI deleted (pages, header, `GetCustomHeader`, orphaned `_Imports`).
- [ ] Tests: projector leak/serialization, hub commands (incl. fresh-User host check), usings updated.
- [ ] Verified: build, test, publish-trim, **and the runtime `dotnet run` checks**.
