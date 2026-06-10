# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The repo is split into two solutions (new SLNX format):
- `sdk/KnockBox.Sdk.slnx` — the SDK NuGet packages (`KnockBox.Core`, `KnockBox.Platform`, `KnockBox.Plugins.Analyzer`, `KnockBox.Templates`, and `KnockBox.Tooling` — optional utility/extension helpers for game authors) plus their test projects (`KnockBox.CoreTests`, `KnockBox.PlatformTests`, `KnockBox.Plugins.AnalyzerTests`).
- `host/KnockBox.Host.slnx` — the `KnockBox` host app, its tests, the ten first-party game plugins (AlphaChain, CardCounter, Codeword, DiceSimulator, DndMapper, DrawnToDress, LinkedList, Operator, Spardle, Tracery), and the WordService library plugin (`KnockBox.WordService` + `KnockBox.WordService.Contracts`), all with their tests.

Target framework is `net10.0`.

- Build the SDK: `dotnet build sdk/KnockBox.Sdk.slnx`
- Build the host (also transitively builds + stages every plugin into `games/`): `dotnet build host/KnockBox.Host.slnx` (or the project directly: `dotnet build host/KnockBox/KnockBox.csproj`)
- Run the host locally: `dotnet run --project host/KnockBox/KnockBox.csproj`
- Publish the host (game plugins staged into `publish/games/` via `CopyPluginsToPublish`, library plugins into `publish/libraries/` via `CopyLibrariesToPublish`): `dotnet publish host/KnockBox/KnockBox.csproj -c Release`
- Run SDK tests: `dotnet test sdk/KnockBox.Sdk.slnx`
- Run host tests: `dotnet test host/KnockBox.Host.slnx`
- Run tests for one project: `dotnet test sdk/KnockBox.CoreTests/KnockBox.CoreTests.csproj`
- Run a single test: `dotnet test --filter "FullyQualifiedName~LobbyServiceTests.CreateLobbyAsync_ReturnsFailure_WhenRouteUnknown"`
- Docker (compose from repo root): `docker compose up --build` — the override file references `${KNOCKBOX_USER_SECRETS_DIR}` and `${KNOCKBOX_HTTPS_DIR}`, so first-time dev setup is `cp .env.example .env` and uncomment the block matching your OS.

## Architecture

The authoritative architecture reference is **`host/KnockBox/Specs/knockbox-platform-architecture.md`** — read it before making structural changes. Summary below.

KnockBox is a Blazor Server host (`KnockBox`) that loads each party game as a **runtime plugin**. The host has no compile-time knowledge of which games exist.

### Plugin system — the critical architectural invariant

- `KnockBox.csproj` references `KnockBox.Core` and `KnockBox.Platform` directly (both from `sdk/`), and each game plugin via `<ProjectReference>` with `ReferenceOutputAssembly="false" Private="false"` — those plugin refs exist *only* to force plugins to build transitively. Do not drop those attributes and do not `using` any game-project type from the host.
- Each game project is a Razor Class Library that imports `..\Directory.Plugin.targets` (the shared targets file lives at `host/Directory.Plugin.targets`). The target copies the plugin's primary DLL, `.deps.json`, scoped-CSS bundle, and `wwwroot/` into `host/KnockBox/bin/{Config}/{TFM}/games/{PluginName}/` after `Build`.
- At startup, `AddKnockBoxPlatform` calls `PluginLoader.LoadModules(...)` with the contents of `options.LibrariesPaths` (default `["libraries"]`) and `options.PluginsPaths` (default `["games"]`). Each plugin is loaded into its own `PluginLoadContext` (ALC) rooted at the plugin folder; shared contracts (`KnockBox.Core`, BCL, logging/DI abstractions) are deferred to the default ALC so type identity is preserved across the host/plugin boundary.
- Each plugin exposes exactly one `IPluginModule` (public parameterless ctor) — `IGameModule` for games (`sdk/KnockBox.Core/Plugins/IGameModule.cs`) or `ILibraryModule` for libraries (`sdk/KnockBox.Core/Plugins/ILibraryModule.cs`). A game module's `RegisterServices` typically calls `registration.AddGameEngine<TEngine>()` on the supplied `IPluginRegistration`, which registers the engine as a singleton *and* as a keyed `AbstractGameEngine` under the manifest's `RouteIdentifier`. Razor pages inject the concrete engine; `LobbyService.CreateLobbyAsync` resolves the engine generically via `GetKeyedService<AbstractGameEngine>(routeIdentifier)`.
- Plugin `wwwroot/` folders are mounted at `/_content/{PluginName}` by `Program.MapPluginStaticAssets`, matching Blazor's RCL convention. Reference plugin assets as `_content/KnockBox.{GameName}/...`.
- The `RouteIdentifier` on `IGameModule` **must** match the route segment in the plugin's `@page` directive (e.g., `"card-counter"` ↔ `@page "/room/card-counter/{ObfuscatedRoomCode}"`). Mismatch = 404 at navigation time.

### Library plugins

- Library plugins live in `host/{LibraryName}/` and stage to `libraries/{LibraryName}/` (parallel to `games/`) via `host/Directory.Library.targets`. Their manifest declares `"kind": "library"` and an `"exportedContracts": [...]` list of contract assembly simple names.
- A library plugin pairs with a sibling **contracts assembly** (`host/{LibraryName}.Contracts/`) that holds public interfaces consumer plugins reference at compile time. The contracts project has zero `KnockBox.*` references and no plugin-targets import.
- The loader **promotes every listed contracts DLL into the default ALC at startup, before any plugin ALC is constructed**, so consumer plugins in different ALCs resolve identical CLR types for the contract interfaces.
- The loader enforces **all libraries finish before any game starts** across three phases: contract promotion → module activation → service registration. A game plugin whose required library failed to load fails loudly at lobby-creation with a DI resolution error.
- Library plugins use **strict Major.Minor.Patch SemVer**. Same `Major.Minor` with different `Patch` → highest Patch wins (loser is logged as superseded). Different Major or Minor → both load side-by-side; consumer plugins bind to a specific version via compiled metadata reference. Folder convention for side-by-side: `libraries/{entryAssembly}.v{Major}.{Minor}/`.
- Library plugins' `IPluginRegistration` is the same surface games use; the host-owned-service denylist drops plugin registrations that would shadow built-ins, library-exported services (game→library shadow), or earlier libraries' registrations (library→library shadow via per-library snapshot rebuild). The "two games shadow each other" behavior is unchanged.

### State, engines, and concurrency

- `AbstractGameState` (in `KnockBox.Core`) owns a `SemaphoreSlim(1,1)` and exposes `Execute` / `ExecuteAsync` (mutating, notifies after unlock) and `WithExclusiveRead` / `WithExclusiveReadAsync` (non-mutating, no notification). **All state mutation must go through these** — notification happens *outside* the lock to keep hold time minimal and avoid reentrant deadlocks from subscribers.
- `AbstractGameEngine` subclasses are singletons; they hold no per-room state. Per-room data lives on `AbstractGameState`.
- State change subscriptions use `state.StateChangedEventManager.Subscribe(Func<ValueTask>)`, which returns an `IDisposable` — store it and dispose it in the component's `Dispose()` (inherit `DisposableComponent` from `KnockBox.Core`).
- `PlayerUnregistered` fires *outside* the execute lock so handlers may safely call `Execute` (e.g., advance turn order on disconnect).
- Fallible operations return `Result` / `ValueResult<T>` / `ValueResult<T, TError>`. Use `TryGetSuccess` / `TryGetFailure` / `IsCanceled`. Avoid exceptions for control flow.

### Session lifecycle

- `IUserService` and `IGameSessionService` are scoped per Blazor circuit. `GameSessionState` is transient in DI but cached per user-id by `ISessionServiceProvider`, which keeps the session alive through a **1-minute grace period** when a circuit drops. If the user reconnects within the window the session is re-attached; otherwise `GameSessionState.Dispose()` unregisters the player.
- Lobby URIs are `room/{routeIdentifier}/{guidA}-{guidB}`. Game pages must validate in `OnInitializedAsync` that the session exists and the URL matches `session.LobbyRegistration.Uri`, else redirect home.

### Persistence and storage

- `IStoragePathService` (default impl `DefaultStoragePathService` in `sdk/KnockBox.Platform/Services/Logic/Storage/`) anchors persisted state at `{KNOCKBOX_DATA_ROOT}` if that env var is set, otherwise `{AppContext.BaseDirectory}/data`. Resolved **once** at process start — mutating the env var mid-run does nothing.
- Layout under the data root: `admin/` (settings + games-state), `logs/` (Serilog daily rolls), `plugins/{routeIdentifier}/` (per-plugin storage).
- First-party plugins always load from `{install}/games/` (staged by `Directory.Plugin.targets`). Third-party plugins load from `{KNOCKBOX_DATA_ROOT}/games/` and **only** when the admin "third-party plugins" toggle is on. `Program.cs` reads that toggle from disk via `AdminSettingsService.ReadThirdPartyToggleFromDisk` *before* `AddKnockBoxPlatform`, so the discovery path list is final by the time DI is built.
- Plugins should write through `IPluginStorage` (`sdk/KnockBox.Core/Plugins/IPluginStorage.cs`) — paths are relative-only; absolute paths and `..` traversal are rejected. This is a **contract-level boundary, not a runtime sandbox**: plugins can still call `System.IO` directly, which is an authoring violation rather than a runtime block.
- `KnockBox.Plugins.Analyzer` ships KB1001–KB1004 Roslyn analyzers that flag sandbox-escape APIs (filesystem, HTTP, process, environment) at compile time. First-party plugins reference it through `host/Directory.Plugin.targets`, which adds a `ProjectReference ... OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to every plugin csproj that imports the targets file. The analyzer also ships with the public `dotnet new knockbox-game` template (`KnockBox.Templates`) so third-party authors get the same build-time checks. The analyzer project lives in both `sdk/KnockBox.Sdk.slnx` and `host/KnockBox.Host.slnx` so it's part of the host build graph.

### DI registration order (inside `AddKnockBoxPlatform`)

The orchestration lives inside the `AddKnockBoxPlatform` extension method (`sdk/KnockBox.Platform/KnockBoxPlatformExtensions.cs`), which `Program.cs` calls during host setup. The order is `RegisterRepositories` → `RegisterValidators` → `RegisterStateServices` → `PluginLoader.LoadModules` → `RegisterLogic(pluginLoadResult)` → navigation + drawing services. `RegisterLogic` runs in **two passes**: pass 1 registers every library plugin (`ILibraryModule` instance) with a per-library snapshot rebuild between iterations so libraries can't shadow each other's services; pass 2 registers every game plugin (`IGameModule` instance) using a post-library snapshot so games can't shadow library-exported services. Game modules are registered as `IGameModule` singletons (the home page enumerates these to build the game list); library modules as `ILibraryModule` singletons (not shown on the home page). Finally `GamePluginAssemblies` is registered so `Routes.razor` can bind `AdditionalAssemblies`.

### Adding a new game

Full steps are in `host/KnockBox/Specs/knockbox-platform-architecture.md` under "Adding a New Game". Short version: new Razor Class Library under `host/` referencing only `KnockBox.Core` (`..\..\sdk\KnockBox.Core\KnockBox.Core.csproj`), `<Import Project="..\Directory.Plugin.targets" />`, subclass `AbstractGameState` + `AbstractGameEngine`, add Razor pages under `/room/{route-identifier}/{ObfuscatedRoomCode}` inheriting `DisposableComponent`, implement `IGameModule` calling `AddGameEngine<TEngine>(RouteIdentifier)`. **Do not** add a reference from `KnockBox` to the new project.

### Adding a library plugin

Full steps in the "Adding a New Library Plugin" section of the architecture doc. Short version: create TWO projects under `host/` — a contracts project `host/{LibraryName}.Contracts/` (pure interfaces/POCOs, NO `KnockBox.*` refs) and the library plugin `host/{LibraryName}/` (references `KnockBox.Core` + the contracts project, imports `..\Directory.Library.targets`, declares the contracts DLL via a `<PluginExportedContract>` MSBuild item, implements `ILibraryModule`, has a `plugin.json` with `"kind": "library"` and `"exportedContracts": ["{LibraryName}.Contracts"]`, uses strict Major.Minor.Patch SemVer). Add a transitive `<ProjectReference ... ReferenceOutputAssembly="false" Private="false" />` to `host/KnockBox/KnockBox.csproj` for the library (the contracts assembly builds transitively as a dep). Add both projects to `host/KnockBox.Host.slnx`. Consumer game plugins reference the contracts project via `ProjectReference` (first-party) or `PackageReference` (third-party). The first instance of this pattern in the repo is `host/KnockBox.WordService` + `host/KnockBox.WordService.Contracts`.

## Testing

- MSTest across the solution; `Moq` + `Moq.AutoMock` are available.
- Each production project has a matching `{Name}Tests` project (e.g., `KnockBox.CardCounter` ↔ `KnockBox.CardCounterTests`). `KnockBoxTests` covers the host project and uses `InternalsVisibleTo` (set in `Program.cs`).

## Logging

Serilog is configured in `Program.cs` with console + rolling file sink at `{AppContext.BaseDirectory}/logs/knockbox-.log` (daily roll, 31-day retention). A bootstrap Serilog logger is built separately so `PluginLoader` can log during DI container construction.
