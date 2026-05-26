# KnockBox — Game Host Platform Architecture

## Vision

A Blazor Server web application that hosts multiple browser-based party games under a single platform. Players create or join lobbies using short lobby codes, similar to Jackbox-style party games. The architecture treats lobby management as shared infrastructure and individual games as pluggable modules, allowing new games to be added with minimal changes to the core platform.

---

## Solution Structure

The solution is split into the host project, a shared core library, one class library per game, and their corresponding test projects:

| Project | Type | Purpose |
|---|---|---|
| `KnockBox` | ASP.NET Core Web App (Blazor Server) | Entry point: host-side routing, DI bootstrapping, database, middleware, plugin discovery |
| `KnockBox.Core` | Class Library (SDK NuGet) | Shared platform infrastructure: `IGameModule` / `IPluginManifest` / `IPluginContext` / `IPluginRegistration` / `PluginLoader`, `AbstractGameState`, `AbstractGameEngine`, session services, navigation, `IRandomNumberService`, result types, thread-safety utilities, `DisposableComponent` |
| `KnockBox.Platform` | Class Library (SDK NuGet) | Hosting SDK: `AddKnockBoxPlatform` / `UseKnockBoxPlatform`, `LobbyService`, `SessionServiceProvider`, home/error pages, default plugin-context wiring, static-asset mounting |
| `KnockBox.Plugins.Analyzer` | netstandard2.0 Roslyn analyzer (SDK NuGet) | Build-time lints KB1001-KB1004 flagging filesystem / network / process / env sandbox-escaping APIs in plugin projects |
| `KnockBox.Templates` | `dotnet new` template pack (SDK NuGet) | `knockbox-game` template: scaffolds a plugin RCL + DevHost + tests with `plugin.json` and analyzer reference pre-wired |
| `KnockBox.CardCounter` | Class Library (game plugin) | Card Counter game logic, state, Razor pages |
| `KnockBox.Codeword` | Class Library (game plugin) | Codeword game logic, state, Razor pages |
| `KnockBox.DiceSimulator` | Class Library (game plugin) | Dice Simulator game logic, state, Razor pages |
| `KnockBox.DndMapper` | Class Library (game plugin) | DnD Mapper game logic, state, Razor pages |
| `KnockBox.DrawnToDress` | Class Library (game plugin) | Drawn To Dress game logic, state, Razor pages |
| `KnockBox.HiddenAgenda` | Class Library (game plugin) | Hidden Agenda game logic, state, Razor pages |
| `KnockBox.Operator` | Class Library (game plugin) | Operator game logic, state, Razor pages |
| `KnockBox.Spardle` | Class Library (game plugin) | Spardle game logic, state, Razor pages |
| `KnockBox.TaskMaster` | Class Library (game plugin) | TaskMaster game logic, state, Razor pages |
| `KnockBox.CoreTests` | MSTest | Unit tests for `KnockBox.Core` |
| `KnockBox.PlatformTests` | MSTest | Unit tests for `KnockBox.Platform` |
| `KnockBox.Plugins.AnalyzerTests` | MSTest | Analyzer rule tests (custom Roslyn harness) |
| `KnockBox.{Game}Tests` | MSTest | One per first-party plugin — unit and integration tests for that game |
| `KnockBoxTests` | MSTest | Integration tests for the main `KnockBox` project (repository layer, etc.) |

The repository ships **nine first-party game plugins**: CardCounter, Codeword, DiceSimulator, DndMapper, DrawnToDress, HiddenAgenda, Operator, Spardle, TaskMaster.

**`KnockBox` references only `KnockBox.Core`.** Game projects are *not* referenced at compile time — they are loaded at runtime from the `games/` subdirectory alongside the host's binaries (see **Plugin System**). Every game project references `KnockBox.Core` only. Adding, removing, or renaming a game never requires a change to `KnockBox` or to any other game.

---

## Plugin System

Games are true runtime plugins, discovered and loaded at application startup. The host project has no compile-time knowledge of which games exist.

### Runtime Directory Layout

Every plugin lives in its own subfolder under `{host}/games/`:

```
KnockBox/bin/{Config}/{TFM}/
├── KnockBox.dll
├── KnockBox.Core.dll
└── games/
    ├── KnockBox.CardCounter/
    │   ├── KnockBox.CardCounter.dll
    │   ├── <transitive deps...>
    │   └── wwwroot/           (optional)
    ├── KnockBox.DiceSimulator/
    │   └── ...
    └── KnockBox.Operator/
        └── ...
```

`PluginLoader` loads only the primary assembly `{PluginName}.dll` per folder into a dedicated per-plugin `PluginLoadContext`. Transitive dependencies are resolved from the plugin's own folder via `AssemblyDependencyResolver` (`{PluginName}.deps.json`), isolating version conflicts between plugins. Shared-contract assemblies already loaded by the host (`KnockBox.Core`, logging/DI abstractions, BCL) are deferred to the default `AssemblyLoadContext` so type identity is preserved across the host/plugin boundary. Loose DLLs directly under `games/` are ignored; the per-subdirectory layout is the only supported shape.

### `IGameModule` Contract

Each plugin exposes exactly one `IGameModule` implementation with a public parameterless constructor (`sdk/KnockBox.Core/Plugins/IGameModule.cs`):

```csharp
public interface IGameModule
{
    IPluginManifest Manifest { get; }
    void RegisterServices(IPluginRegistration registration);
    RenderFragment? GetCustomHeader() => null;
}
```

Identity — name, description, `RouteIdentifier`, `Version`, `EntryAssembly`, declared capabilities, and the home-page `TileAsset` SVG path — all live on the `IPluginManifest` returned by `Manifest`. The manifest is typically read from an embedded `plugin.json` resource so the in-code copy and the on-disk copy come from the same source file:

```csharp
public class CardCounterModule : IGameModule
{
    public IPluginManifest Manifest { get; } =
        PluginManifest.FromEmbeddedResourceOrThrow(typeof(CardCounterModule).Assembly);

    public void RegisterServices(IPluginRegistration registration)
        => registration.AddGameEngine<CardCounterGameEngine>();
}
```

The home-page tile is rendered by the host from `Manifest.TileAsset` — a plugin-relative path to an SVG in the plugin's `wwwroot/` (e.g. `"tile.svg"`). If the manifest has no `tileAsset`, the host shows a hot-pink fallback labeled with `Manifest.Name`. Plugins flagged `workInProgress: true` get a shared "Work In Progress" hazard-tape overlay rendered on top.

### `plugin.json` + `IPluginManifest`

Every plugin folder contains a `plugin.json` that declares the plugin's identity and the host capabilities it wants access to (`PluginManifest` in `sdk/KnockBox.Core/Plugins/PluginManifest.cs`):

```json
{
  "schemaVersion": 1,
  "name": "Card Counter",
  "description": "High stakes blackjack style counting.",
  "routeIdentifier": "card-counter",
  "version": "1.0.0",
  "entryAssembly": "KnockBox.CardCounter",
  "capabilities": []
}
```

`schemaVersion` is pinned at 1. `routeIdentifier` must match `^[a-z0-9-]+$` and doubles as the URL segment (`/room/{routeIdentifier}/...`) and the DI key for the keyed `AbstractGameEngine`. `capabilities` is a list of zero or more of `config` / `storage`; each entry unlocks the matching `IPluginContext` surface at runtime (`IPluginContext.Configuration`, `IPluginContext.Storage`). Accessing an un-declared capability throws `PluginCapabilityNotGrantedException` on first read.

The loader reads `plugin.json` before it loads the plugin DLL, then cross-checks the parsed manifest against the module's in-code `Manifest` property. Any disagreement (different name, mismatched route, different schema, etc.) rejects the plugin.

### `IPluginContext` and `IPluginRegistration`

`RegisterServices` does **not** receive a raw `IServiceCollection` — it receives an `IPluginRegistration` (`sdk/KnockBox.Core/Plugins/IPluginRegistration.cs`), the plugin's only handle on DI:

- `AddGameEngine<TEngine>()` — exactly once per plugin. Registers `TEngine` as a singleton and the same instance as a keyed `AbstractGameEngine` under the plugin's own route identifier.
- `AddSingleton<TService, TImplementation>()` / `AddScoped` / `AddTransient` — plugin-private registrations.
- Factory overloads (`AddSingleton<T>(Func<IPluginContext, T>)` etc.) — lets plugin services close over their `IPluginContext` (per-plugin `ILogger`, `IConfiguration`, `IPluginStorage`) captured at registration time.

Every registration goes through `DefaultPluginRegistration` (`sdk/KnockBox.Platform/Plugins/DefaultPluginRegistration.cs`), which silently drops registrations that target host-owned service types. See **Sandbox Surface / Host-service denylist** below.

### `PluginLoader`

`PluginLoader` (`sdk/KnockBox.Core/Plugins/PluginLoader.cs`) is invoked from `AddKnockBoxPlatform` before `RegisterLogic`. It:

1. Scans subdirectories of `games/` and, for each subfolder, parses `plugin.json` into an `IPluginManifest`. Manifest shape failures (unknown capabilities, bad route identifier, missing fields, wrong schema version) reject the plugin before any DLL loads.
2. Inspects the plugin's `{EntryAssembly}.deps.json` via `InspectDepsJson`. This surfaces two gates:
   - **Forbidden dependencies** — plugins that list `KnockBox.Platform` (or any entry from the static forbidden-dependencies set) in their compile graph are rejected; they would drag platform types into the plugin ALC and break type identity. Adding or removing entries from the forbidden set requires an SDK release — it is not host-configurable.
   - **`KnockBox.Core` version gate** — the loader reads the Core version the plugin was compiled against and rejects plugins compiled against a newer Core than the host. Prerelease / build-suffixes on the version string (`-alpha`, `+build`) are stripped before parsing.
3. Loads each `{EntryAssembly}.dll` into its own `PluginLoadContext` (`sdk/KnockBox.Core/Plugins/PluginLoadContext.cs`), which uses `AssemblyDependencyResolver` to satisfy transitive deps from the plugin folder while deferring shared-contract assemblies (anything already resolved by the host) to the default ALC.
4. Reflects over each assembly for non-abstract types assignable to `IGameModule`, handling `ReflectionTypeLoadException` gracefully.
5. Activates the single matching module via `Activator.CreateInstance` **inside a 5-second timeout** (`Task.Run` + `Wait(timeout)`). Hanging ctors log an error and the plugin is skipped.
6. Cross-checks the activated module's `Manifest` against the on-disk manifest; rejects mismatches.
7. De-duplicates by `RouteIdentifier` (case-insensitive) — first wins, subsequent duplicates are logged as errors.
8. Returns a `PluginLoadResult(IReadOnlyList<LoadedPlugin> Plugins, IReadOnlyList<Assembly> Assemblies)` where each `LoadedPlugin` carries the module, its manifest, and a pointer to its load context.

A single misbehaving plugin (missing DLL, malformed `plugin.json`, type load failure, ctor throw, ctor timeout, manifest mismatch, forbidden dep, newer Core) is logged and skipped; it does not prevent the host from starting.

### `AddGameEngine<TEngine>` Helper

`IPluginRegistration.AddGameEngine<TEngine>()` does two registrations under the plugin's own route identifier:

```csharp
services.AddSingleton<TEngine>();
services.AddKeyedSingleton<AbstractGameEngine>(
    routeIdentifier,
    (sp, _) => sp.GetRequiredService<TEngine>());
```

The concrete engine is a singleton, and the same instance is exposed as a keyed `AbstractGameEngine`. This lets Razor pages inject the concrete engine directly (`@inject CardCounterGameEngine Engine`) while `LobbyService` resolves generically by route key via `IServiceProvider.GetKeyedService<AbstractGameEngine>(routeIdentifier)` — a single instance serves both paths. `AddGameEngine` must be called **exactly once** per plugin; zero or multiple calls are flagged by the loader and the plugin is marked unreachable.

### `GamePluginAssemblies`

After discovery, the list of plugin assemblies is exposed as a `GamePluginAssemblies` singleton so downstream infrastructure (e.g., Razor component discovery) can enumerate the loaded plugin assemblies without reaching back into `PluginLoader`.

### Static Asset Mounting

Each plugin's `wwwroot/` is mounted dynamically at application startup by `Program.MapPluginStaticAssets`, which iterates `games/{PluginName}/wwwroot/` and calls `UseStaticFiles` with a `PhysicalFileProvider` rooted at that directory and a `RequestPath` of `/_content/{PluginName}`. This matches the path convention Blazor would use for a referenced Razor Class Library, so scoped CSS bundles (`{PluginName}.styles.css`), images, and scripts referenced from plugin Razor components resolve naturally. Duplicate plugin folder names are skipped with a warning; individual mount failures are logged per-plugin and do not abort startup.

### Build Glue — `Directory.Plugin.targets`

The repo root contains a shared MSBuild target imported by every game `.csproj`:

```xml
<Import Project="..\Directory.Plugin.targets" />
```

After `Build`, the target copies each plugin's `TargetDir` (primary DLL + transitive deps), scoped-CSS bundle, and `wwwroot/` assets into `KnockBox\bin\{Config}\{TFM}\games\{TargetName}\`. This is what makes the host's runtime `games/` folder appear during local dev and in Docker builds without any project-to-project references.

### Plugin Trust Model

The `games/` directory is **trust-equivalent to the host binary**. There is no signature check, no manifest allowlist, and no sandbox: `PluginLoader` activates any type implementing `IGameModule` with a public parameterless constructor that it discovers in `games/{name}/{name}.dll`, and each plugin is loaded into a non-collectible `AssemblyLoadContext` that cannot be unloaded for the lifetime of the process. Adding such a DLL to `games/` is sufficient to execute arbitrary code at host startup, inside the host process, with the host's full privileges.

Deployment consequences:

- **Do not bind-mount `games/`** from a user-writable volume in Docker. Bake plugins into the image at build time so the running container's `games/` is read-only from the host's perspective.
- Treat `games/` in the published artifact the same way you treat `KnockBox.dll` itself — changes require a full release review, not a hot-patch drop.
- Third-party plugins are opt-in via the admin toggle (off by default) and are loaded from `data/games/` (volume-mounted). The trust model does **not** change for third-party plugins — they run with the same full-host privileges as first-party code. Operators enabling the toggle accept the same trust posture as shipping the plugin in the first-party release artifact. See [`../../../README.md#installing-third-party-plugins`](../../../README.md) for the operator-facing warnings and [`../../../docs/making-a-game-plugin.md#trust-model`](../../../docs/making-a-game-plugin.md) for the plugin-author guidance.

---

---

## Plugin Kinds — Games and Libraries

The plugin system supports two **kinds** of plugin, distinguished by the manifest's `kind` field:

- **`game`** (the default): a user-facing plugin that exposes a route, a tile on the home page, and exactly one `AbstractGameEngine`. The `IGameModule` interface above represents this kind.
- **`library`**: a non-user-facing plugin that registers shared services other plugins consume — e.g. `KnockBox.WordService` exposing `IWordListService`. Library plugins have no home-page tile, no game engine, and ship in a separate `libraries/` folder.

Library plugins were added so games sharing infrastructure (the obvious example: a 100k-line word dictionary used by Spar-dle and other future word games) can load that infrastructure once instead of once per game.

### `ILibraryModule` and `IPluginModule`

The module-type hierarchy is rooted at `IPluginModule` (`sdk/KnockBox.Core/Plugins/IPluginModule.cs`), which carries the two universal members:

```csharp
public interface IPluginModule
{
    IPluginManifest Manifest { get; }
    void RegisterServices(IPluginRegistration registration);
}
```

The two specializations live in the same folder:

- `IGameModule : IPluginModule` adds `RenderFragment? GetCustomHeader()`.
- `ILibraryModule : IPluginModule` is a marker interface — no additional members.

`PluginLoader` reflects for `IPluginModule` inside each plugin assembly and enforces that the discovered type matches the manifest's `kind` (game manifest → must be `IGameModule`; library manifest → must be `ILibraryModule`). Mismatches reject the plugin with a clear log entry.

### Contracts sidecar pattern

A library plugin pairs with a **sibling contracts assembly** that holds the public interfaces and POCOs consumer plugins reference at compile time. The library and contracts are separate csproj projects so the contracts assembly has zero `KnockBox.*` references and no plugin-targets import — it's a pure interface-only sidecar.

The library's `plugin.json` declares its contracts assembly's simple name(s):

```json
{
  "schemaVersion": 1,
  "name": "Word Service",
  "description": "Shared word-existence and dictionary lookup service for word games.",
  "routeIdentifier": "word-service",
  "version": "1.0.0",
  "entryAssembly": "KnockBox.WordService",
  "kind": "library",
  "exportedContracts": ["KnockBox.WordService.Contracts"],
  "capabilities": []
}
```

At host startup, `PluginLoader` promotes every listed contracts DLL into the default `AssemblyLoadContext` *before* any plugin ALC is constructed. As a result:

- Every consumer plugin (game or library), regardless of which ALC it lives in, resolves the contract interfaces from the same promoted assembly.
- The CLR sees one `IWordListService` type, not one per consumer ALC.
- DI registration of `IWordListService` against `WordListService` (in the library's own ALC) is resolvable by a game plugin in a different ALC because the *interface* type identity matches.

The contracts assembly is staged into the library plugin's folder via `Directory.Library.targets` reading a `PluginExportedContract` MSBuild item group in the library csproj. Consumer plugins reference the contracts via `ProjectReference` (first-party) or `PackageReference` (third-party, once the contracts are published as a NuGet package).

### Library Plugin Loading and Ordering

Loading runs in three sequenced phases. The invariant — **all libraries finish before any game starts** — is enforced at each phase:

1. **Contract promotion** (`PluginLoader.PromoteExportedContracts`). Reads every library manifest's `exportedContracts`, validates each DLL exists and isn't colliding with a host-shipped identity, and calls `AssemblyLoadContext.Default.LoadFromAssemblyPath` on it. If a library's contracts can't be promoted, the entire library is skipped — without its contracts, consumer plugins can't share types, so loading the impl would be worse than skipping.

2. **Assembly load + module activation** (`PluginLoader.LoadModules`). Libraries are activated first, then games. The resulting `PluginLoadResult.Plugins` collection preserves library-first order so downstream callers don't need to re-sort. A library plugin that fails to load is skipped *before* any game plugin is touched.

3. **Service registration** (`LogicRegistrations.RegisterLogic`). Two passes: pass 1 registers every library plugin's services; pass 2 registers every game plugin's services and engines. A game plugin that depends on a library service whose library failed to load will fail loudly at lobby-creation time with a clear DI resolution error — games are never "partially loaded" against a missing library.

Note on interaction with `PromoteShareableDependencies`: `PluginLoader` runs two distinct promotion passes against `AssemblyLoadContext.Default`. **Contract promotion** (per-contract, identity-dedup on `name + AssemblyName.Version + token`) runs first and only sees library plugins. **Shareable-dependency promotion** (per-dep, first-wins by Major.Minor) runs *after* contract promotion and operates on the union of every surviving library + game subdir. The simple names contract promotion adds to the promoted-assemblies set are visible to shareable-dep promotion, so a contract that's also a shareable dep never gets double-promoted. The two passes use different dedup keys by design: contracts require exact-identity sharing across ALCs, whereas multi-shipper deps tolerate compatible-version variance.

`exportedContracts` is allowed to be empty for a library. A library that exports zero contracts can still register services keyed by types defined in `KnockBox.Core` (e.g. internal infrastructure shared across the host's own subsystems); contract promotion skips it and the activation phase treats it like any other library.

### Service Shadowing Rules

`DefaultPluginRegistration` (`sdk/KnockBox.Platform/Plugins/DefaultPluginRegistration.cs`) silently drops registrations targeting service types in its host-owned denylist and logs an error naming the plugin and the offending service type. The denylist is a union of:

- `AlwaysProtectedTypes`: plugin-system primitives (`IPluginContext`, `IPluginRegistration`, `IPluginManifest`, `IPluginStorage`, `IPluginModule`, `IGameModule`, `ILibraryModule`, `AbstractGameEngine`, `AbstractGameState`) plus Microsoft.Extensions fundamentals.
- A *snapshot* of the host's `IServiceCollection` captured at a specific moment.

The snapshot moment differs per phase to give the right shadowing protection:

| Attempt | Result | Mechanism |
| --- | --- | --- |
| Library plugin → built-in/platform service (e.g., `IRandomNumberService`) | Dropped + logged | Pre-pass-1 snapshot |
| Library plugin → `AlwaysProtectedTypes` member | Dropped + logged | Static set |
| Library plugin → service registered by an *earlier-loaded* library plugin | Dropped + logged | Per-library snapshot rebuild between iterations of pass 1 |
| Game plugin → built-in/platform service | Dropped + logged | Post-library snapshot |
| Game plugin → library-exported service (e.g., `IWordListService`) | Dropped + logged | Post-library snapshot |
| Game plugin → service registered by an earlier-loaded game plugin | Permitted (last-wins) | By design; matches today's behavior |

The "two games can shadow each other" behavior is unchanged from before library plugins existed and is pinned by a regression test so a future refactor doesn't silently break it.

### Library Versioning and Coexistence

Library manifests must use strict `Major.Minor.Patch` SemVer. The parser rejects 2-component or 4-component-with-revision versions for libraries; game manifests keep the permissive version validation they had before.

Coexistence policy:

- **Same Major.Minor, different Patch**: the highest Patch wins. The lower patch's folder is logged as superseded at Information level and dropped.
- **Different Major or different Minor**: both versions load side-by-side. Each library's contracts assembly is promoted to the default ALC as a distinct assembly identity (`Name + Version + PublicKeyToken`). The CLR treats `IWordListService` from v1.2 contracts as a *different* type from `IWordListService` from v1.3 contracts; a game plugin's compiled metadata reference pins which version it gets at runtime, and DI resolves correctly because the registered service types differ.

Identity-equal contracts shared across two side-by-side library versions (e.g. v1.2 and v1.3 of the same library both shipping `Contracts v1.0.0.0` because the contract surface didn't change) are promoted once and reused. A library trying to export a contract whose simple name collides with a host-shipped assembly is rejected — the host owns that identity.

Folder convention: `libraries/{entryAssembly}.v{Major}.{Minor}/` when multiple versions ship; single-version libraries may use `libraries/{entryAssembly}/`. The folder name is convention only; the manifest authoritatively decides identity.

Constraints:

- A single library csproj produces one version. Side-by-side versions require separate csproj/NuGet packages.
- The contracts csproj `<Version>` and the library csproj `<Version>` should be pinned to the same MSBuild variable in `Directory.Library.targets` so they can't drift.

### Adding a New Library Plugin

Mirror of the "Adding a New Game" section below, but with library-specific conventions.

1. **Create the contracts project** at `host/{LibraryName}.Contracts/` containing only interfaces and POCOs the contracts assembly exposes. No `KnockBox.*` references. No `Directory.*.targets` import.

2. **Create the library plugin project** at `host/{LibraryName}/`:
   - csproj references both `KnockBox.Core` and the sibling `*.Contracts` project.
   - `<Import Project="..\Directory.Library.targets" />`
   - Declare the contracts DLL in a `PluginExportedContract` MSBuild item group:
     ```xml
     <ItemGroup>
       <PluginExportedContract Include="$(TargetDir){LibraryName}.Contracts.dll" />
     </ItemGroup>
     ```
   - Add a `plugin.json` with `"kind": "library"` and `"exportedContracts": ["{LibraryName}.Contracts"]`. Use strict `Major.Minor.Patch` SemVer. The manifest's `routeIdentifier` must still be present and match `^[a-z0-9-]+$`, but for a library it's only a per-plugin DI key — it never appears in navigation, so any unique kebab-case slug is fine (use the library's own name).
   - Implement `ILibraryModule` in a class with a public parameterless constructor. Its `RegisterServices` calls only `AddSingleton/AddScoped/AddTransient` for the contracts types — do **not** call `AddGameEngine`.

3. **Wire into the host build graph**: add a `<ProjectReference ... ReferenceOutputAssembly="false" Private="false" />` to `host/KnockBox/KnockBox.csproj` so building the host transitively builds the library. The contracts assembly is built as a transitive dependency of the library plugin and consumers; no explicit host reference needed.

4. **Add to the solution**: add both projects to `host/KnockBox.Host.slnx`.

5. **Consumers reference the contracts**: a game plugin csproj adds `<ProjectReference Include="..\{LibraryName}.Contracts\{LibraryName}.Contracts.csproj" />` for first-party, or `<PackageReference Include="{LibraryName}.Contracts" Version="1.2.*" />` for third-party.

The library plugin's `KnockBox.Plugins.Analyzer` still fires (libraries can hit `File`/`HttpClient`/`Process`/`Environment` just as easily as games) — the same KB1001–KB1004 rules apply.

---

## System Context

All users connect to a single Blazor Server instance. Each browser tab maintains a persistent WebSocket circuit to the server. Because all circuits share the same process, game state lives entirely in memory with no need for external message buses or database-backed state during gameplay. The application is deployed as a Docker container with a PostgreSQL database available for persistent data (currently used only for scaffolding entities).

```
 ┌──────────┐  ┌──────────┐  ┌──────────┐
 │ Browser  │  │ Browser  │  │ Browser  │
 │ (Tab A)  │  │ (Tab B)  │  │ (Tab C)  │
 └────┬─────┘  └────┬─────┘  └────┬─────┘
      │ WebSocket    │ WebSocket   │ WebSocket
      │ (Circuit)    │ (Circuit)   │ (Circuit)
      │              │             │
      │  ┌───────────┴─────────┐  │
      │  │  Scoped per circuit │  │
      │  │  ┌────────────────┐ │  │
      │  │  │ UserService    │ │  │
      │  │  │ GameSession    │ │  │
      │  │  │ CircuitHandler │ │  │
      │  │  │ Navigation     │ │  │
      │  │  └────────────────┘ │  │
      │  └─────────────────────┘  │
      │                           │
 ┌────┴───────────────────────────┴──────┐
 │         Blazor Server Process         │
 │                                       │
 │  ┌─────────────────────────────────┐  │
 │  │     LobbyService (Singleton)    │  │
 │  │  ┌─────────┐ ┌─────────┐       │  │
 │  │  │ Lobby 1 │ │ Lobby 2 │ ...   │  │
 │  │  └─────────┘ └─────────┘       │  │
 │  └─────────────────────────────────┘  │
 │                                       │
 │  ┌─────────────────────────────────┐  │
 │  │  Game Engines (Singleton / DI)  │  │
 │  │  One per loaded plugin          │  │
 │  │  (discovered from games/ dir)   │  │
 │  └─────────────────────────────────┘  │
 │                                       │
 │  ┌─────────────────────────────────┐  │
 │  │  Support Services (Singleton)   │  │
 │  │  LobbyCodeService │ Profanity   │  │
 │  │  RandomNumberService            │  │
 │  └─────────────────────────────────┘  │
 └───────────────────────────────────────┘
```

---

## Control Flow

The following diagram shows the lifecycle of a single player action during gameplay. The Razor page calls a method on the game engine (resolved via DI), the engine calls `state.Execute` which acquires the lock, runs the mutation, releases the lock, and then notifies all subscribers to re-render.

```
  Player A (Razor Page)          Game Engine              Game State           Player B (Razor Page)
  ─────────────────────          ───────────              ──────────           ─────────────────────
          │                           │                        │                        │
          │  engine.RollDice(         │                        │                        │
          │    player, state,         │                        │                        │
          │    action)                │                        │                        │
          │ ─────────────────────────>│                        │                        │
          │                           │                        │                        │
          │                           │  state.Execute(() =>   │                        │
          │                           │    { ... })            │                        │
          │                           │ ──────────────────────>│                        │
          │                           │                        │                        │
          │                           │               ┌────────────────────┐             │
          │                           │               │  Acquire lock      │             │
          │                           │               │  Run mutation      │             │
          │                           │               │  Release lock      │             │
          │                           │               │  NotifyChanged()   │             │
          │                           │               └────────────────────┘             │
          │                           │                        │                        │
          │           Result          │                        │                        │
          │ <─────────────────────────│                        │                        │
          │                           │                        │                        │
          │              callback: re-render with updated state│                        │
          │ <──────────────────────────────────────────────────│                        │
          │                           │                        │                        │
          │                           │                        │  callback: re-render   │
          │                           │                        │  with updated state    │
          │                           │                        │ ──────────────────────>│
          │                           │                        │                        │
       [UI updates]                   │                        │                  [UI updates]
```

Key points: the `LobbyService` is not involved during gameplay. The Razor page injects the game engine via DI and holds a reference to the game state obtained when joining the lobby. `Execute`/`ExecuteAsync` acquires the lock, runs the mutation, releases the lock, and *then* notifies subscribers — keeping the lock held for the minimum duration and preventing reentrant deadlocks from listener callbacks. Listeners are invoked with error isolation so a failing subscriber cannot break notification for others. All fallible operations return `Result` or `ValueResult<T>` rather than throwing exceptions.

---

## Core Components

### LobbyService

A singleton service that acts as the lobby registry. Owns a `ConcurrentDictionary<string, LobbyRegistration>` of all active lobbies. Responsibilities include creating lobbies (delegating state creation to the appropriate `AbstractGameEngine`), issuing unique lobby codes via `ILobbyCodeService`, constructing obfuscated lobby URIs, validating join requests, registering players on the game state, and closing lobbies when the host requests it. The `LobbyService` is not involved during gameplay — once a player has joined and received the game state reference, all gameplay flows directly between the Razor page, the game engine, and the state.

Game engine resolution is performed dynamically at runtime. `LobbyService.CreateLobbyAsync` resolves the engine via `IServiceProvider.GetKeyedService<AbstractGameEngine>(routeIdentifier)`; see **Plugin System** for how `IGameModule` implementations are discovered and how engines are registered as keyed services. Lobby codes are 6-character uppercase alphanumeric strings, generated cryptographically and filtered through `IProfanityFilter`. Code generation and release are handled by `ILobbyCodeService`.

```csharp
public interface ILobbyService
{
    Task<ValueResult<LobbyRegistration>> CreateLobbyAsync(User host, string routeIdentifier, CancellationToken ct = default);
    Task<ValueResult<UserRegistration>> JoinLobbyAsync(User user, string lobbyCode, CancellationToken ct = default);
    Task<Result> CloseLobbyAsync(User user, LobbyRegistration registration, CancellationToken ct = default);
}
```

### LobbyRegistration

A lightweight container representing a single game session. Holds the lobby code (6-char string), the lobby URI (used for navigation), the game's name and its `RouteIdentifier`, and a reference to the `AbstractGameState`. The lobby does not track a status enum — the game's joinability is owned by the state via the `IsJoinable` property, and lobby lifetime is tied to the host's circuit. When a player joins, the lobby provides them with the game state reference via `UserRegistration`.

```csharp
public class LobbyRegistration(string lobbyCode, string lobbyUri, string gameName, string routeIdentifier, AbstractGameState state)
{
    public readonly string Code = lobbyCode;
    public readonly string Uri = lobbyUri;       // e.g. "room/dice-simulator/{guidA}-{guidB}"
    public readonly string GameName = gameName;
    public readonly string RouteIdentifier = routeIdentifier;
    public readonly AbstractGameState State = state;
}
```

### User

Players are identified by a `User` class containing `Name` (max 12 characters, trimmed) and `Id` (a UUIDv7 string). The `Id` is unique per Blazor circuit and is used for all authorization checks, action routing, and player tracking. `Name` is the player's chosen display name, persisted to browser `localStorage` via the `IUserService` when it changes.

The `User` type is intentionally narrow in v1:

```csharp
public class User
{
    internal User(string name, string id);     // internal ctor
    public string Name { get; internal set; }  // internal setter
    public string Id { get; }
}

public static class UserFactory
{
    public static User Create(string name, string id);  // test fixture factory
}
```

The constructor is `internal` so external code cannot bypass `IUserService` to mint arbitrary users. Plugin test code constructs `User` fixtures via `UserFactory.Create(name, id)` in `KnockBox.Core.Services.State.Users`. In-repo callers that need the internal setter (the name-disambiguation pass in `AbstractGameState.RegisterPlayer` is the only one) live in `KnockBox.Core` and access it directly. Production mutation goes through `IUserService.SetCurrentUserName`.

### UserRegistration

A record class that ties a `User` to a specific lobby session. Holds a reference to the `User`, an `UnregistrationToken` (`IDisposable` that removes the player from the game state when disposed), and the `LobbyRegistration`. Implements `IDisposable` by disposing the unregistration token. Scoped per circuit inside `GameSessionService`.

```csharp
public record class UserRegistration(
    User User,
    IDisposable UnregistrationToken,
    LobbyRegistration LobbyRegistration) : IDisposable
{
    public void Dispose() => UnregistrationToken.Dispose();
}
```

### AbstractGameEngine

An abstract base class that every game extends. Defines player limits (`MaxPlayerCount`, `MinPlayerCount`), an async factory method for creating the game's concrete state, a `StartAsync` lifecycle method, and a `CanStartAsync` validation predicate. Each concrete engine exposes **game-specific methods** (e.g., `RollDice`, `DrawCard`) that the game's Razor pages call directly. These methods receive a reference to the game state, mutate it via `state.Execute`/`state.ExecuteAsync`, and return `Result` or `ValueResult<T>`.

Game engines are **singletons** registered in DI. They hold no per-room state — all mutable data lives on the `AbstractGameState`. Razor pages inject their concrete engine directly (e.g., `@inject DiceSimulatorGameEngine Engine`).

```csharp
public abstract class AbstractGameEngine
{
    public int MaxPlayerCount { get; }
    public int MinPlayerCount { get; }

    public abstract Task<ValueResult<AbstractGameState>> CreateStateAsync(
        User host, CancellationToken ct = default);

    // Public entry point — sealed in the base class. Verifies
    // caller.Id == state.Host.Id before delegating to StartAsyncCore.
    public Task<Result> StartAsync(
        User caller, AbstractGameState state, CancellationToken ct = default);

    // Plugin override point. Host-identity authorization has already happened.
    protected abstract Task<Result> StartAsyncCore(
        AbstractGameState state, CancellationToken ct = default);

    public virtual Task<bool> CanStartAsync(
        AbstractGameState state, CancellationToken ct = default)
        => Task.FromResult(HasValidPlayerCount(state) && IsLobbyOpen(state));

    protected bool HasValidPlayerCount(AbstractGameState state)
        => MinPlayerCount <= state.Players.Count && state.Players.Count <= MaxPlayerCount;

    protected bool IsLobbyOpen(AbstractGameState state) => state.IsJoinable;
}
```

`StartAsync(caller, state)` is sealed in the base class and verifies that `caller.Id == state.Host.Id` before calling the plugin's `StartAsyncCore` override. Host-identity authorization is enforced once by the platform — plugins never re-check it. The same pattern is used by `KickPlayer`, so plugins get a consistent auth story across command paths.

`HasValidPlayerCount` and `IsLobbyOpen` are `protected` helpers that `CanStartAsync` composes; plugins override `CanStartAsync` with game-specific readiness rules and combine them with the baseline checks.

`AbstractGameEngine` is purely server-side, concerned only with game logic and state transitions. It has no knowledge of UI components.

### AbstractGameState

An abstract base class that defines the minimal contract shared across all games — the host, the player list, joinability status, a concurrency lock, a built-in event manager, and a scheduled callback mechanism. Each game implements its own concrete subclass with strongly-typed properties for that game's specific state.

The game state instance is created when the lobby is created via the engine's `CreateStateAsync()` factory method, and the same instance is used from lobby through gameplay. Players join and subscribe to this state immediately. When the host starts the game, `StartAsync` mutates the existing state in place — there is no state replacement or re-subscription required.

All state mutations go through `Execute` (sync) or `ExecuteAsync` (async), which acquire a per-state `SemaphoreSlim(1, 1)`, execute the mutation, release the lock, and then notify all subscribers. Notification happens *after* the lock is released to keep lock duration minimal and to prevent reentrant deadlocks from listener callbacks. Each listener is invoked with error isolation so that a failing subscriber does not prevent others from being notified.

**Documented exception — per-keystroke pending-clue write.** `Codeword.Pages.CluePhase.OnClueInput` writes `CodewordPlayerState.PendingClue` directly without going through `Execute`. This is intentional: notifying subscribers on every keystroke would re-render every connected client and is exactly the storm the lock is meant to dampen. The write is safe because reference assignment of a `string` is atomic in .NET, and the timeout reader in `CluePhaseState.Tick` only ever observes a complete prior value (worst case: the timeout grabs the previous keystroke). New code should *not* rely on this exception — it exists solely for the per-frame UI-input hot path. Grep `Execute bypass` to find the call-site.

Subscriptions are obtained via `state.StateChangedEventManager.Subscribe(Func<ValueTask>)`, which returns an `IDisposable`. Razor components store the subscription and dispose it when the component is detached, preventing dead callbacks from accumulating.

The `PlayerUnregistered` event is fired after a player is successfully removed from the game (disconnected, left, or kicked). It is raised *outside* the execute lock so subscribers may safely call `Execute` in response.

#### Notify outside the lock — load-bearing rule

`StateChangedEventManager.Notify()` (and `NotifyStateChanged()` on `AbstractGameState`, which forwards to it) **must only be called after the executeLock has been released**. The framework already does this for you: `Execute` / `ExecuteAsync` fire exactly one `Notify` in their `finally` block after `Release()`. Game code should not raise an additional inline notification from inside an `Execute`-wrapped mutator.

Concretely, `SetPhase`-style methods on per-game `AbstractGameState` subclasses must look like:

```csharp
// CORRECT — relies on Execute's post-release Notify
public void SetPhase(GamePhase phase) => Phase = phase;

// WRONG — inline Notify holds the executeLock through subscriber work
public void SetPhase(GamePhase phase) { Phase = phase; NotifyStateChanged(); }
```

Why this is load-bearing, not stylistic: subscribers are usually Razor components that call `InvokeAsync(StateHasChanged)`. When the calling thread is already on a Blazor circuit dispatcher (the common case — the mutation originated from a player's UI event), `InvokeAsync` runs the work item synchronously, and `StateHasChanged` runs the renderer synchronously, **including child-component disposal and JS-interop teardown**, before returning. If the executeLock is still held during that chain, the next mutation on the same state (or even a re-entrant call from a Dispose handler) blocks on the lock, and the dispatcher cannot make progress — a hard deadlock. This was the root cause of the `OutfitCustomization → VotingRoundSetup` regression in Drawn To Dress: the in-lock Notify fired while transitioning out of a phase whose subtree owned SVG canvases with JS interop, and the synchronous Dispose chain blocked indefinitely.

Calling `Notify` inline also defeats Execute's coalescing: multiple mutations inside one `Execute` should produce a single fan-out, not one per field write.

The same rule applies to any FSM-state code that calls `context.State.StateChangedEventManager.Notify()` directly — that call site is *inside* `Execute` (every engine wraps FSM work in `state.Execute(...)`), so it is just as unsafe and equally redundant. Prefer ending the FSM transition normally and letting `Execute`'s `finally` do the notification.

If you have a genuine need to mid-Execute publish progress to subscribers (rare; the only legitimate motivation we've found is the per-keystroke draft-update path), use the documented `Execute bypass` pattern instead — see the `Codeword.Pages.CluePhase.OnClueInput` exception above.

#### Exclusive Read Access

The state exposes `WithExclusiveRead` and `WithExclusiveReadAsync` for non-mutating reads that still need serialization with the execute lock. Unlike `Execute`/`ExecuteAsync`, these do *not* call `NotifyStateChanged` after releasing the lock.

#### Scheduled Callbacks

The state exposes a `ScheduleCallback` method that allows game engines to schedule delayed state transitions. It accepts a `TimeSpan` delay and a `Func<Task>` action, and returns a `ValueResult<IScheduledCallbackHandle>` (`sdk/KnockBox.Core/Primitives/Disposable/IScheduledCallbackHandle.cs`). The handle exposes `Cancel()` and `IDisposable.Dispose()` — both idempotent and safe to call after the owning state has been disposed; neither leaks the underlying `CancellationTokenSource`. Internally the scheduled action is invoked via `ExecuteAsync` when the delay elapses, so it follows the same locking and notification semantics as any player-driven mutation. All outstanding callbacks are automatically cancelled when the state is disposed.

```csharp
public abstract class AbstractGameState(User host, ILogger logger) : IDisposable
{
    public bool IsDisposed { get; }
    public event Action? OnStateDisposed;
    public event Action<User>? PlayerUnregistered;
    public IThreadSafeEventManager StateChangedEventManager { get; }
    public bool IsJoinable { get; }
    public User Host => host;
    public IReadOnlyList<User> Players { get; }
    public IReadOnlyList<User> KickedPlayers { get; }

    public ValueResult<IDisposable> RegisterPlayer(User player);
    public Result KickPlayer(User player);

    /// MUST be called from inside Execute / ExecuteAsync. The setter itself
    /// does not take a lock; callers rely on the outer Execute to serialize
    /// against the IsJoinable gate in RegisterPlayer.
    public void SetJoinable(bool isJoinable);

    public ValueTask<Result> ExecuteAsync(Func<ValueTask> action, CancellationToken ct = default);
    public Result Execute(Action action);
    public ValueResult<TReturn> Execute<TReturn>(Func<TReturn> action);
    public ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default);
    public Result WithExclusiveRead(Action action);
    public ValueResult<IScheduledCallbackHandle> ScheduleCallback(TimeSpan delay, Func<Task> action);
}
```

`Execute` always fires `StateChangedEventManager.Notify()` when it releases the lock, regardless of whether the inner mutation changed anything. The pre-v1 `UpdateJoinableStatus(bool)` suppressed the notify when the value was unchanged — `SetJoinable` does not. Callers that care about same-value idempotence must gate on the inside.

Exceptions during `Execute` / `ExecuteAsync` are caught and the dispose race is reported as a specific `ObjectDisposedException`-class failure; the unified public error message is "State was disposed." across both already-disposed and in-flight-disposal paths. `PlayerUnregistered` and `OnStateDisposed` invocation lists are iterated with per-handler error isolation — one throwing subscriber does not short-circuit the rest.

### IUserService

A scoped service (one per Blazor circuit) that manages the current user's identity. On `InitializeCurrentUserAsync`, loads the stored username from browser `localStorage` (falls back to "Not Set") and creates a `User` with a UUIDv7 ID. Name changes flow through `SetCurrentUserName`, which owns trimming, the 12-character cap, the `UserNameChanged` event fan-out (per-handler error isolation), and the fire-and-forget persistence back to `localStorage`.

```csharp
public interface IUserService
{
    User? CurrentUser { get; }
    event Action? UserInitialized;
    event Action<UserNameChangedArgs>? UserNameChanged;

    Task InitializeCurrentUserAsync(CancellationToken ct = default);
    Task ResetIdentityAsync(CancellationToken ct = default);
    void SetCurrentUserName(string name);
}
```

### IGameSessionService / GameSessionState

`IGameSessionService` is a **scoped** proxy (one per Blazor circuit) that provides circuit-level concerns — navigation via `INavigationService` — while delegating all persistent session state to a user-id-backed `GameSessionState` instance retrieved from `ISessionServiceProvider`.

`GameSessionState` is a **transient** state holder registered in the DI container so `ISessionServiceProvider` can cache exactly one instance per user session id, surviving Blazor circuit breaks. It owns the `UserRegistration` field and implements `IDisposable`: when the session provider disposes it after the post-disconnect grace period, it removes the user from the game state without requiring an active circuit.

This two-layer design means a user who temporarily loses their WebSocket connection (network hiccup, page refresh) is **not** removed from the game lobby — the `GameSessionState` instance persists in `ISessionServiceProvider` until a new circuit connects for the same user id, keeping the lifecycle token active.

```csharp
public interface IGameSessionService
{
    bool TryGetCurrentSession(out UserRegistration? currentSession);
    Result SetCurrentSession(UserRegistration session);   // also navigates to game page
    Result LeaveCurrentSession(bool navigateHome = true); // also optionally navigates home
}
```

### INavigationService

A scoped service that wraps Blazor's `NavigationManager`. Provides `ToHome()`, `ToGame(LobbyRegistration)`, and URI-building helpers (`GetGameUri`, `GetHomeUri`). Scoped because `NavigationManager` itself is scoped per circuit.

```csharp
public interface INavigationService
{
    string GameBaseRoute { get; }
    string GetHomeUri();
    void ToHome();
    string GetGameUri(LobbyRegistration lobbyRegistration);
    void ToGame(LobbyRegistration lobbyRegistration);
}
```

### DisposableComponent

A base class for all Blazor pages and components. Extends `ComponentBase` and implements `IDisposable`. Provides a `ComponentDetached` `CancellationToken` that cancels when the component is removed from the render tree. All game lobby pages and the home page inherit from this class.

```csharp
public class DisposableComponent : ComponentBase, IDisposable
{
    protected CancellationToken ComponentDetached { get; }
    public virtual void Dispose();
}
```

### ThreadSafeEventManager

The primary component communication mechanism. `AbstractGameState` owns one `ThreadSafeEventManager` (non-generic) instance as its `StateChangedEventManager`. It is also available as a generic `ThreadSafeEventManager<TEventArgs>` for typed payloads.

Key design decisions:
- Listeners are stored as a **copy-on-write array**. Subscribe/unsubscribe both clone the array under a `Lock`, so notification never needs the lock.
- `Subscribe` returns an `IDisposable` (`DisposableAction`) that removes the callback when disposed — enabling clean scoped subscriptions.
- `NotifyAsync` takes a snapshot of listeners, then fans out to all of them concurrently. Already-completed `ValueTask`s skip allocation. Errors in individual listeners are swallowed and logged.
- `Notify` (fire-and-forget) spawns `Task.Run` and delegates to `NotifyAsync`. Used by `AbstractGameState` after releasing the execute lock.

```csharp
// Non-generic version (used by AbstractGameState.StateChangedEventManager):
public interface IThreadSafeEventManager
{
    IDisposable Subscribe(Func<ValueTask> callback);
    Task NotifyAsync();
    void Notify();
}

// Generic version (available for typed event payloads):
public interface IThreadSafeEventManager<TEventArgs>
{
    IDisposable Subscribe(Func<TEventArgs, ValueTask> callback);
    Task NotifyAsync(TEventArgs args);
    void Notify(TEventArgs args);
}
```

---

## Cross-Cutting Patterns

### Result / ValueResult Railway Error Handling

All fallible service operations return `Result`, `ValueResult<TValue>`, or `ValueResult<TValue, TError>` rather than throwing exceptions for control flow. Callers use `TryGetSuccess(out value)` / `TryGetFailure(out error)` / `IsCanceled` to discriminate outcomes. `Result.Success` is a shared static instance for void success cases. This pattern is used throughout `LobbyService`, `AbstractGameEngine`, `AbstractGameState`, `GameSessionService`, and all game engine methods.

### Disposable Subscription Pattern

All event subscriptions return an `IDisposable`. When the disposable is disposed, the subscription is automatically removed. This is enforced by `ThreadSafeEventManager` and backed by `DisposableAction` — a helper that invokes an `Action` exactly once when disposed, using `Interlocked.Exchange` to prevent double invocation. Player registrations also follow this pattern: `RegisterPlayer` returns an `IDisposable` that removes the player when disposed.

### Threading Utilities

Thread safety is first-class throughout the application:
- **`Lock`** (C# 13 `System.Threading.Lock`): Used in `ThreadSafeEventManager`, `LobbyCodeService`, `AbstractGameState` (dispose, scheduled callbacks, player management).
- **`SemaphoreSlim(1, 1)`**: Used as an async mutex in `AbstractGameState._executeLock`. All game mutations are serialized through this.
- **`ConcurrentDictionary`**: Used in `LobbyService._lobbies`, `DiceSimulatorGameState._playerStats`, `CardCounterGameState.GamePlayers`.
- **`Interlocked`**: Used for atomic flag swaps in `AbstractGameState._disposed`, `GameSessionState._currentSession`, and `DisposableAction._disposeAction`.
- **`ThreadSafeList<T>`**: A full `IList<T>` implementation backed by `List<T>` + `ReaderWriterLockSlim`.
- **`CancellationTokenSource` patterns**: `AbstractGameState._disposeCts` (linked into all scheduled callbacks), `IDBackedServiceProvider` disposal timers (per-user grace period), `DisposableComponent._cts` (component detach token).

---

## Routing & Game-Owned Pages

Each game owns its own routable Razor pages. Lobby URIs are constructed as `room/{routeIdentifier}/{guidA}-{guidB}` where `{routeIdentifier}` comes from the `IGameModule` implementation.

Game pages declare matching `@page` directives, e.g., `@page "/room/dice-simulator/{ObfuscatedRoomCode}"`. The route identifier must match the route segment used in the game's page directive.

Each game controls its full user experience: the lobby layout, gameplay phases, transitions, and any game-specific sub-flows. The platform imposes no UI constraints on games.

**Security check on navigation:** When a user lands on a game page, the page validates in `OnInitializedAsync` that (1) the user has an active session in `IGameSessionService` and (2) the URI in the URL matches the session's registered lobby URI. If either check fails, the user is redirected home.

All first-party lobby pages share this validation through `LobbyPageBase<TGameState>` (in `KnockBox.Core`), which extracts the trailing obfuscated room code from `session.LobbyRegistration.Uri` via `LobbyUriHelper.TryExtractObfuscatedRoomCode` and compares it to the incoming `{ObfuscatedRoomCode}` route parameter. The base also validates that `session.LobbyRegistration.State` matches the plugin's expected state type and that the state is not disposed — any mismatch redirects home. New lobby pages should inherit `LobbyPageBase<TGameState>` so the validation stays uniform.

---

## Join-to-Start Flow

The following diagram shows the complete lifecycle from a player joining an existing lobby through the host starting the game.

```
  Player (Browser)         Home Page          LobbyService        GameState / Session     Host (Browser)
  ────────────────         ─────────          ────────────        ──────────────────────   ──────────────
        │                      │                    │                      │                      │
        │  Enter lobby code    │                    │                      │                      │
        │ ────────────────────>│                    │                      │                      │
        │                      │                    │                      │                      │
        │                      │  JoinLobbyAsync(   │                      │                      │
        │                      │    user, code)     │                      │                      │
        │                      │ ──────────────────>│                      │                      │
        │                      │                    │                      │                      │
        │                      │                    │  state.Execute(() => │                      │
        │                      │                    │    RegisterPlayer()) │                      │
        │                      │                    │ ────────────────────>│                      │
        │                      │                    │                      │                      │
        │                      │  ValueResult<      │                      │                      │
        │                      │    UserRegistration│                      │                      │
        │                      │    >               │                      │                      │
        │                      │ <──────────────────│                      │                      │
        │                      │                    │                      │                      │
        │                      │  GameSessionService│                      │                      │
        │                      │  .SetCurrentSession│                      │                      │
        │                      │  (userRegistration)│                      │                      │
        │                      │  → navigates to    │                      │                      │
        │                      │  game page         │                      │                      │
        │                      │                    │                      │                      │
        │  Navigate to /room/  │                    │                      │                      │
        │  {type}/{obfuscated} │                    │                      │                      │
        │ <────────────────────│                    │                      │                      │
        │                      │                    │                      │                      │
        │  Game page loads                                                 │                      │
        │  Inject engine from DI                                           │                      │
        │  Subscribe to state ────────────────────────────────────────────>│                      │
        │  (store IDisposable)                                             │                      │
        │                                                                  │                      │
        │                                              NotifyChanged()     │                      │
        │  callback: re-render (lobby view) <──────────────────────────────│─────────────────────>│
        │                                                                  │  callback: re-render │
        │                                                                  │                      │
        │                                                                  │  Host clicks Start   │
        │                                                                  │ <─────────────────────│
        │                                                                  │                      │
        │                                                                  │  engine.StartAsync(  │
        │                                                                  │    state)            │
        │                                                        ┌─────────────────────────┐      │
        │                                                        │  Execute:                │      │
        │                                                        │    Acquire lock          │      │
        │                                                        │    SetJoinable(false)    │      │
        │                                                        │    Release lock          │      │
        │                                                        │    NotifyChanged()       │      │
        │                                                        └─────────────────────────┘      │
        │                                                                  │                      │
        │  callback: re-render (gameplay view) <───────────────────────────│─────────────────────>│
        │                                                                  │  callback: re-render │
```

---

## Key Flows

### Create Lobby

1. Player selects a game type from the home page (dynamically generated from `IGameModule` implementations).
2. `LobbyService.CreateLobbyAsync` resolves the selected game's `AbstractGameEngine` from DI via `IServiceProvider.GetKeyedService<AbstractGameEngine>(routeIdentifier)`, calls `engine.CreateStateAsync(host)` to obtain the concrete game state, generates a unique 6-character lobby code via `ILobbyCodeService` (cryptographically random, profanity-filtered), constructs an obfuscated lobby URI (`room/{routeIdentifier}/{guidA}-{guidB}`), creates a `LobbyRegistration`, and stores it in the `ConcurrentDictionary`.
3. The host's `GameSessionService.SetCurrentSession` stores the `LobbyRegistration` reference and navigates to the game page.
4. The game page loads, subscribes to the state, and renders the lobby view.

### Join Lobby

1. Player enters a lobby code on the home page.
2. `LobbyService.JoinLobbyAsync` normalizes the code (trim + uppercase), looks up the `LobbyRegistration` in the `ConcurrentDictionary`, calls `state.Execute(() => state.RegisterPlayer(user))` within the execute lock, and returns a `UserRegistration` containing the user, an unregistration `IDisposable`, and the `LobbyRegistration`.
3. The player's `GameSessionService.SetCurrentSession` stores the `UserRegistration` and navigates to the game page.
4. The game page validates the session and URL, subscribes to the state, and renders.
5. The state notifies all subscribers, updating all components to show the new player.

Players cannot join a lobby once the game state's `IsJoinable` is set to `false`. The `AbstractGameState.RegisterPlayer` enforces this check. Players who have been kicked are tracked in a `HashSet<User>` and are prevented from rejoining.

### Start Game

1. Host clicks start in the game's lobby view.
2. The lobby page calls `engine.StartAsync(UserService.CurrentUser, state)`. The base-class `StartAsync` verifies `caller.Id == state.Host.Id` and returns a failure Result for non-host callers.
3. On authorized calls, the base delegates to the plugin's `StartAsyncCore(state)` override, which calls `state.Execute(...)` to initialize game data and close the lobby (`state.SetJoinable(false)` inside the Execute lambda).
4. The state notifies all subscribers, causing all circuits to re-render.

### Gameplay

1. A player performs an action (e.g., rolls dice, draws a card).
2. The game's Razor page calls a method on the injected game engine (e.g., `engine.RollDice(player, state, action)`).
3. The engine method calls `state.Execute(() => { ... })`, which acquires the lock, runs the mutation, releases the lock, and then notifies all subscribers.
4. All subscribed Razor components re-render with the updated state via `InvokeAsync(StateHasChanged)`.

### Player Disconnect

1. A player's browser tab closes or their circuit drops.
2. `GameSessionService` is disposed, disposing the `LifecycleToken` that keeps the `GameSessionState` alive.
3. If no other circuit is holding a token for the user, `ISessionServiceProvider` starts a 1-minute grace period timer.
4. If the user reconnects within 1 minute (new circuit with the same session id), the `LifecycleToken` is re-acquired, cancelling the timer and the `GameSessionState` is retained — the player rejoins the game lobby seamlessly.
5. If the timer expires, `ISessionServiceProvider` disposes all cached services for that token, including `GameSessionState`. `GameSessionState.Dispose()` calls `TakeCurrentSession()?.Dispose()`, which disposes the `UserRegistration`. The unregistration token disposes, removing the player from the game state and notifying subscribers. The `PlayerUnregistered` event is also fired, allowing game engines to react (e.g., `CardCounterGameEngine` uses this to advance the turn order).
6. The host is fixed — there is no host transfer on disconnect.

---

## Adding a New Game

Adding a game requires these steps:

1. **Create a new class-library project** `KnockBox.{GameName}` that references `KnockBox.Core` only. Do *not* add a reference from `KnockBox` to the new project — the host discovers games at runtime.

2. **Import the shared plugin build target** in the new `.csproj`:
   ```xml
   <Import Project="..\Directory.Plugin.targets" />
   ```
   This copies build output into `KnockBox\bin\{Config}\{TFM}\games\{TargetName}\` after each build so the host can load it.

3. **Subclass `AbstractGameState`** — define a concrete state class with the strongly-typed properties your game needs. The host, player list, lock, subscription, notification, and scheduled callback infrastructure is inherited.

4. **Subclass `AbstractGameEngine`** — implement `CreateStateAsync(User host, CancellationToken)` to return the concrete state instance (wrapped in `ValueResult`), implement `StartAsync(AbstractGameState state, CancellationToken)` to begin gameplay (the engine does not receive a `User` — caller-identity checks live in the lobby page), and add game-specific action methods. Each method calls `state.Execute`/`state.ExecuteAsync` — locking and notification are handled automatically. Use `SetJoinable` from inside an `Execute` lambda to close the lobby. Optionally override `CanStartAsync(state, ct)` and compose with the protected `HasValidPlayerCount(state)` helper.

5. **Create Razor page(s)** — add one or more pages inheriting `DisposableComponent` with `@page "/room/{route-identifier}/{ObfuscatedRoomCode}"`. Inject the concrete engine via DI, subscribe to `state.StateChangedEventManager`, validate the session in `OnInitializedAsync`, enforce host-only `StartAsync` at the page layer, and dispose the subscription in `Dispose()`. Any `wwwroot/` assets are served automatically from `/_content/KnockBox.{GameName}`.

6. **Author `plugin.json`** — create a `plugin.json` at the plugin project root with `schemaVersion`, `name`, `description`, `routeIdentifier`, `version`, `entryAssembly`, and `capabilities`. Optionally add `tileAsset` (path to an SVG in your plugin's `wwwroot/` — rendered as the home-page tile) and `workInProgress: true` (overlays a shared "Work In Progress" band). Mark `plugin.json` as an `<EmbeddedResource>` in the csproj so `PluginManifest.FromEmbeddedResourceOrThrow` can read it, and `Directory.Plugin.targets` will also copy it alongside the DLL into `games/`. The `routeIdentifier` must match the route segment used in the game's `@page` directives.

7. **Author the tile SVG** — drop a `tile.svg` (300×200 viewBox recommended; the host enforces a 3:2 aspect ratio on the rendered tile) into `wwwroot/` and reference it from `plugin.json`'s `tileAsset`. The plugin's `wwwroot/` is staged into `games/{PluginName}/wwwroot/` and mounted at `/_content/{PluginName}/`. If you skip this, the home page renders a hot-pink fallback with the plugin's name.

8. **Implement `IGameModule`** — add a class to the game project with a public parameterless constructor (returning within 5 seconds). Expose the manifest via `PluginManifest.FromEmbeddedResourceOrThrow(typeof(MyModule).Assembly)`, and in `RegisterServices(IPluginRegistration registration)` call `registration.AddGameEngine<YourEngine>()` (plus any other game-specific DI). Optionally override `GetCustomHeader()` to replace the host's default in-room header.

No changes to `KnockBox`, `KnockBox.Core`, or any other game project are required. After a rebuild the platform discovers the new plugin, registers its engine, mounts its static assets, and the game appears on the home page automatically.

### Sandbox Surface

Plugin projects are analyzed at build time by `KnockBox.Plugins.Analyzer` (rules KB1001–KB1004). The analyzer flags direct filesystem access (KB1001 — use `IPluginContext.Storage` instead; the path-accepting `StreamReader(string)` / `StreamWriter(string)` ctors are flagged, but stream-accepting overloads are exempt), outbound network traffic and name resolution (KB1002 — outbound network from plugins is not supported, covering `HttpClient`, raw sockets, `Dns`, `Ping`, `NetworkInterface`, `SmtpClient`), process launch or host shutdown (KB1003 — not permitted, including `Process.Start` and `Environment.Exit` / `Environment.FailFast`), and raw environment-variable reads (KB1004 — use `IPluginContext.Configuration`). Rules ship as warnings, not errors; use `#pragma warning disable KBxxxx` with a justification comment when you have a considered reason (e.g., reading a bundled read-only CSV via `<Content CopyToOutputDirectory>`). These are build-time lints and do not block reflection-based bypass (`Activator.CreateInstance`, `Type.GetType`) — the plugin trust model above still applies as the authoritative security boundary.

### Host-service denylist

`DefaultPluginRegistration` (`sdk/KnockBox.Platform/Plugins/DefaultPluginRegistration.cs`) is what a plugin's `IPluginRegistration` calls land on. Every `AddSingleton` / `AddScoped` / `AddTransient` / `AddGameEngine` is routed through an `IsHostOwned(Type)` check that consults two sets:

1. **Static `AlwaysProtectedTypes`** — a `FrozenSet<Type>` of plugin-system primitives (`IPluginContext`, `IPluginRegistration`, `IPluginManifest`, `IPluginStorage`, `IGameModule`, `AbstractGameEngine`, `AbstractGameState`) and Microsoft.Extensions fundamentals (`IConfiguration`, `IHostedService`, `IHostApplicationLifetime`, `ILoggerFactory`, `ILogger`, `ILogger<>`). These are blocked regardless of what's in the `IServiceCollection` at plugin-registration time.
2. **Dynamic snapshot** — `DefaultPluginRegistration.CaptureHostOwnedServiceTypes(IServiceCollection)` is called at the top of `LogicRegistrations.RegisterLogic`, before the plugin loop starts. It walks every descriptor and records each `ServiceType`, promoting closed generics to their open definition. The result is a `FrozenSet<Type>` passed to every `DefaultPluginRegistration` constructor. Any type the host registered *before* `RegisterLogic` (repositories, validators, state services, navigation, drawing, `LobbyService`, `SessionServiceProvider`, etc.) is automatically protected — the denylist is self-maintaining.

Closed generics are reduced to their open definition before the check, so a plugin's `ILogger<MyPluginService>` registration is rejected because the host's `ILogger<>` is in `AlwaysProtectedTypes`. Rejected registrations are dropped silently and logged at error level — the plugin continues loading without the rogue registration.

The denylist is not host-configurable. Adding or removing entries requires an SDK release because they affect the plugin contract.

### Lobby lifecycle hooks

`LobbyService` implements `IHostedService` (registered both as `ILobbyService` and as a hosted service, backed by a single concrete singleton registration). On `StopAsync` it snapshots the lobby dictionary, clears it, and disposes every open `AbstractGameState` — each in a try/catch so one bad state doesn't orphan the rest. This ensures a clean host shutdown releases state semaphores, cancels scheduled callbacks, and flushes any downstream subscriptions.

The host-eviction-closes-lobby chain runs outside `StopAsync` and covers the "host's circuit dropped and the 1-minute grace period elapsed" case:

1. `GameSessionService` is disposed with the host's circuit; its `LifecycleToken` on `GameSessionState` releases.
2. `SessionServiceProvider`'s eviction timer fires after the grace period (driven by `TimeProvider.Delay`, so tests can advance a `FakeTimeProvider` synchronously).
3. `GameSessionState.Dispose()` runs `TakeCurrentSession()?.Dispose()` → the `disposeAction` closure in `Home.razor.cs` → `LobbyService.CloseLobbyAsync(host, lobby)`.
4. `CloseLobbyAsync` removes the lobby from the dictionary, releases the lobby code, disposes the state (which disposes the semaphore, cancels scheduled callbacks, fires `OnStateDisposed`, and propagates to every subscriber's re-render, which navigates non-host players home).

### SDK versioning and compatibility

`KnockBox.Core` follows SemVer. Inside a major the plugin-facing contract (`IGameModule`, `IPluginManifest`, `IPluginContext`, `IPluginRegistration`, `AbstractGameEngine`, `AbstractGameState`) is stable. The host, the SDK packages, and the template pack all share a major — host `1.x.x` runs plugins compiled against SDK `1.x.x`. Plugins pin `KnockBox.Core [1.0.0, 2.0.0)` and are rejected by a host on SDK `1.x.x` if their `.deps.json` reports a Core version newer than the host's.

Breaking changes to any contract above bump the SDK and host to `2.0.0` in lock-step. Non-breaking additions ship as minor; bug fixes as patch. The SDK-side authorial guide is [`docs/making-a-game-plugin.md`](../../../docs/making-a-game-plugin.md).

---

## DI Registration

DI is organized into registration extension methods called from `AddKnockBoxPlatform` (in `KnockBox.Platform`) in this order: `RegisterRepositories` → `RegisterValidators` → `RegisterStateServices` → *(`PluginLoader.LoadModules` runs)* → `RegisterLogic(pluginLoadResult)` → navigation and drawing services registered directly on `builder.Services`.

### RegisterLogic(PluginLoadResult) — Singletons

| Interface | Implementation | Purpose |
|---|---|---|
| `IProfanityFilter` | `ProfanityFilter` | Aho-Corasick profanity detection |
| `ILobbyCodeService` | `LobbyCodeService` | Lobby code generation and release |
| `IRandomNumberService` | `RandomNumberService` | Fast and secure random number generation |

Before the plugin loop starts, `RegisterLogic` calls `DefaultPluginRegistration.CaptureHostOwnedServiceTypes(services)` and stores the resulting `FrozenSet<Type>` — this is the dynamic half of the denylist described in **Host-service denylist** above.

After the core singletons, `RegisterLogic` iterates `pluginLoadResult.Plugins` and for each one:
- Constructs a per-plugin `DefaultPluginRegistration` (with the plugin's manifest and the host-owned type snapshot) and invokes `module.RegisterServices(registration)`, which typically calls `registration.AddGameEngine<TEngine>()` — registering the concrete engine as a singleton *and* exposing it as a keyed `AbstractGameEngine` under the manifest's `RouteIdentifier`.
- Asserts the plugin called `AddGameEngine` exactly once; zero or more-than-one calls flag the plugin as unreachable.
- Registers a keyed `IPluginContext` under the plugin's `RouteIdentifier` so plugin-private services resolved with a factory receive the right per-plugin logger / configuration section / storage root.
- Adds the module instance itself as an `IGameModule` singleton so the home page can enumerate available games.

Finally, `RegisterLogic` adds a `GamePluginAssemblies` singleton wrapping `pluginLoadResult.Assemblies`.

### RegisterStateServices() — Mixed

| Interface | Implementation | Lifetime | Purpose |
|---|---|---|---|
| `ILobbyService` | `LobbyService` | Singleton | Lobby registry |
| `ISessionServiceProvider` | `SessionServiceProvider` | Singleton | Session-scoped persistent service cache |
| `IUserService` | `UserService` | Scoped | Per-circuit user identity |
| `IGameSessionService` | `GameSessionService` | Scoped | Per-circuit session proxy; delegates state to `GameSessionState` |
| *(concrete)* | `GameSessionState` | Transient | User-id-backed session state; cached by `ISessionServiceProvider` |

### RegisterRepositories() — Mixed

| Interface | Implementation | Lifetime | Purpose |
|---|---|---|---|
| `ISessionStorageService` | `SessionStorageService` | Scoped | Browser sessionStorage |
| `ILocalStorageService` | `LocalStorageService` | Scoped | Browser localStorage |
| `IEntityKeyProvider<TestEntity, ApplicationDbContext>` | `TestEntityKeyProvider` | Singleton | Entity-to-DbSet mapping |
| `IRepository<>` | `BaseRepository<>` | Singleton | Generic CRUD (open generic) |

### RegisterValidators() — Singleton

| Interface | Implementation | Purpose |
|---|---|---|
| *(concrete)* | `TestEntityValidator` | FluentValidation for TestEntity |

### Direct in Program.cs

| Interface | Implementation | Lifetime | Purpose |
|---|---|---|---|
| `INavigationService` | `NavigationService` | Scoped | Blazor NavigationManager wrapper |
| `ISvgClipboardService` | `SvgClipboardService` | Singleton | SVG clipboard support for drawing-based games |
| `IDbContextFactory<ApplicationDbContext>` | EF Core + Npgsql | Factory | Database context creation |

**Lifetime rules:**
- Lobby state (`LobbyService`, `ISessionServiceProvider`, game engines, lobby code service) is **Singleton** — all users share the same active lobby registrations, engine instances, and session-scoped service cache.
- Per-circuit concerns (`UserService`, `GameSessionService`, `NavigationService`, client storage) are **Scoped** (one instance per Blazor circuit / browser connection).
- Per-user session state (`GameSessionState`) is **Transient** in the DI container but cached as a single instance per user id by `ISessionServiceProvider`, surviving circuit breaks.
- Infrastructure (repositories, key providers) are **Singleton** because `IDbContextFactory` handles per-operation context lifetime.

---

## State Change Propagation

State changes propagate through the system as follows:

1. `AbstractGameState.Execute()` acquires the `SemaphoreSlim`, runs the mutation, releases the lock.
2. `StateChangedEventManager.Notify()` is called (fire-and-forget).
3. `ThreadSafeEventManager.Notify` spawns `Task.Run` which calls `NotifyAsync`.
4. `NotifyAsync` snapshots the listener array, then invokes all listeners concurrently.
5. Each subscribed component's callback calls `InvokeAsync(StateHasChanged)` to marshal the re-render onto the Blazor synchronization context.
6. The component re-renders, reading the latest state.

```csharp
// In a game page's OnInitializedAsync:
_stateSubscription = GameState.StateChangedEventManager.Subscribe(
    async () => await InvokeAsync(StateHasChanged));

// In Dispose():
_stateSubscription?.Dispose();
```

---

## Blob Sharing

The platform exposes a cross-circuit binary-blob delivery channel — a stable `/blob-share/{token}` URL backed by an IndexedDb blob held in the originating Blazor circuit. Used by DnD Mapper's display view to render player-uploaded map images on a separate browser without the host pushing the bytes through SignalR per-frame, and available to any plugin that needs the same shape via `IndexedDbBlob.PublishShare(...)`.

### Path

`GET /blob-share/{token:guid}` lives in `KnockBox.Platform` (`sdk/KnockBox.Platform/Services/Storage/IndexedDb/BlobShareEndpoint.cs`) and is mapped from `Program.cs` via `MapBlobShareEndpoint()`. The endpoint resolves the token against the singleton `BlobShareRegistry`, opens a one-shot `IJSStreamReference` against the originating circuit, drains the bytes to RAM, populates a process-wide LRU `BlobShareByteCache`, and writes the response.

### Invariants (do not regress)

These are the protections that keep one slow JS-stream open from killing the host circuit. The cluster was added after a production incident where a multi-image display view fanned out N concurrent stream opens, starved Blazor's JS dispatcher past its internal pipe timeout (~60 s), and the `TimeoutException` escalated to `CircuitHost.UnhandledException` — tearing the host down even though the endpoint caught it.

- **Per-circuit serialization gate.** `BlobShareEntry.CircuitScopeId` (sourced from the originating `IndexedDbInterop.ScopeId`) keys a refcounted `SemaphoreSlim` in `BlobShareRegistry`. The endpoint holds it while opening a stream so only one `IJSStreamReference` is in flight per circuit at a time.
- **Per-token single-flight.** `BlobShareRegistry.RunSingleFlight` coalesces concurrent same-token cache-miss requests onto one underlying stream-and-store task. Cache populated as a side effect; followers serve from the cache.
- **45 s watchdog.** A linked `CancellationTokenSource` cancels the read at `BlobShareEndpoint.PerStreamTimeout`, comfortably below Blazor's internal pipe timeout. Cancellation via our CT surfaces as `OperationCanceledException` — Blazor does NOT escalate that to a fatal circuit exception. The watchdog is reset after the gate-wait so the full window applies to the actual drain, not to queueing.
- **Full-buffer-then-write.** The endpoint reads the JS stream into a pre-sized `byte[entry.Length]` before touching the response. Failure paths land 503/410/500 with empty body rather than a truncated 200; on EOF short of advertised length, the endpoint surfaces an error rather than serving truncated bytes.

### Authoring rule

Any new code constructing a `BlobShareEntry` must populate `CircuitScopeId` from the originating circuit's `IndexedDbInterop.ScopeId`. Without it the entry shares a gate with everything else under `Guid.Empty` and the per-circuit serialization invariant collapses. The `IndexedDbBlobImpl.PublishShare` path already wires this; new producers should follow it.

### Pointers

- Endpoint and watchdog: `sdk/KnockBox.Platform/Services/Storage/IndexedDb/BlobShareEndpoint.cs`
- Registry, scope gate, single-flight: `sdk/KnockBox.Platform/Services/Storage/IndexedDb/BlobShareRegistry.cs`
- Process-wide LRU cache: `sdk/KnockBox.Platform/Services/Storage/IndexedDb/BlobShareByteCache.cs`
- Circuit scope id: `sdk/KnockBox.Platform/Services/Storage/IndexedDb/IndexedDbInterop.cs` (`ScopeId`)
- Display-side `onerror` fallback: `host/KnockBox.DndMapper/wwwroot/js/dndMapperDisplayImageFallback.js`

---

## Constraints & Trade-offs

**Single server only.** All state is in-memory within one process. This is appropriate for a party game platform with moderate concurrent usage. Scaling to multiple servers would require replacing the in-memory `ConcurrentDictionary` with a distributed store and reintroducing external pub/sub.

**No persistence during gameplay.** If the server restarts, all active lobbies are lost. This is acceptable for short-lived party game sessions. The PostgreSQL database and repository layer exist as infrastructure scaffolding for future persistent features (game history, leaderboards, user accounts).

**Thread safety is per-state.** The `ConcurrentDictionary` protects lobby creation and lookup. Each `AbstractGameState` instance owns a `SemaphoreSlim` lock, and all mutations go through `Execute`/`ExecuteAsync`. The subscriber list is independently thread-safe via the copy-on-write array in `ThreadSafeEventManager`.

**Notification after lock release.** Subscriber notification happens after the lock is released. This means a listener that reads the state could theoretically see a subsequent mutation's result if another `Execute` call completes between lock release and notification. In practice this is acceptable for UI rendering — the component renders the latest state. The benefit is that listener callbacks cannot deadlock the state, and lock hold times are minimized.

**Fixed host.** The host is the player who created the lobby and is set at creation time. There is no host transfer if the host disconnects.

**1-minute disconnect grace period.** When a circuit drops, the player is not immediately removed. `ISessionServiceProvider` starts a 1-minute timer before disposing the user's cached services (including `GameSessionState`). If the user reconnects within that window their session is preserved seamlessly.

**JS interop for client storage and file export.** Browser `localStorage` is used for persisting the user's display name across sessions, and a JS module handles CSV file downloads for the Dice Simulator.

**Route convention is runtime-enforced.** The route identifier from the `IGameModule` must match the route segment used in the game's Razor page `@page` directive. A mismatch will result in a 404 at navigation time.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core / .NET 10 |
| Frontend | Blazor Server (Interactive Server render mode, prerender disabled) |
| Real-time updates | `IDisposable` event subscriptions via `ThreadSafeEventManager` |
| State storage | In-memory (`ConcurrentDictionary`, per-state `SemaphoreSlim` locking) |
| Scheduled transitions | `ScheduleCallback` on `AbstractGameState` (returns `IScheduledCallbackHandle`) |
| Game plugin system | `PluginLoader` reads `plugin.json` into `IPluginManifest`, inspects `.deps.json` for forbidden deps + Core-version gate, activates `IGameModule` inside a 5-second ctor budget, cross-checks the in-code manifest against on-disk, loads the DLL into a per-plugin `AssemblyLoadContext`; keyed `AbstractGameEngine` DI via `IPluginRegistration.AddGameEngine<T>()`; per-plugin `wwwroot` mounted at `/_content/{PluginName}`; build glue in `Directory.Plugin.targets` |
| Plugin sandbox | Host-service denylist (`AlwaysProtectedTypes` static set + dynamic `IServiceCollection` snapshot captured before the plugin loop); `IPluginStorage` path guard rejects `..` / absolute paths / reparse-point escapes; `KnockBox.Plugins.Analyzer` Roslyn warnings KB1001-KB1004 at build time |
| Game UI | Game-owned Razor pages at `/room/{route-identifier}/{obfuscated-code}` |
| Database | PostgreSQL via EF Core (Npgsql) |
| Logging | Serilog (structured, console sink) |
| Validation | FluentValidation |
| Deployment | Docker (docker-compose) |
| Language | C# 13 / .NET 10 |
