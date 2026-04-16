# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The repo is split into two solutions (new SLNX format):
- `sdk/KnockBox.Sdk.slnx` — the three NuGet packages (`KnockBox.Core`, `KnockBox.Platform`, `KnockBox.Templates`) plus `KnockBox.CoreTests`.
- `host/KnockBox.Host.slnx` — the `KnockBox` host app, its tests, and the seven first-party game plugins with their tests.

Target framework is `net10.0`.

- Build the SDK: `dotnet build sdk/KnockBox.Sdk.slnx`
- Build the host (also transitively builds + stages every plugin into `games/`): `dotnet build host/KnockBox.Host.slnx` (or the project directly: `dotnet build host/KnockBox/KnockBox.csproj`)
- Run the host locally: `dotnet run --project host/KnockBox/KnockBox.csproj`
- Publish the host (plugins staged into `publish/games/` via `CopyPluginsToPublish`): `dotnet publish host/KnockBox/KnockBox.csproj -c Release`
- Run SDK tests: `dotnet test sdk/KnockBox.Sdk.slnx`
- Run host tests: `dotnet test host/KnockBox.Host.slnx`
- Run tests for one project: `dotnet test sdk/KnockBox.CoreTests/KnockBox.CoreTests.csproj`
- Run a single test: `dotnet test --filter "FullyQualifiedName~LobbyServiceTests.CreateLobbyAsync_ReturnsFailure_WhenRouteUnknown"`
- Docker (compose from repo root): `docker compose up --build`

## Architecture

The authoritative architecture reference is **`host/KnockBox/Specs/knockbox-platform-architecture.md`** — read it before making structural changes. Summary below.

KnockBox is a Blazor Server host (`KnockBox`) that loads each party game as a **runtime plugin**. The host has no compile-time knowledge of which games exist.

### Plugin system — the critical architectural invariant

- `KnockBox.csproj` references `KnockBox.Core` and `KnockBox.Platform` directly (both from `sdk/`), and each game plugin via `<ProjectReference>` with `ReferenceOutputAssembly="false" Private="false"` — those plugin refs exist *only* to force plugins to build transitively. Do not drop those attributes and do not `using` any game-project type from the host.
- Each game project is a Razor Class Library that imports `..\Directory.Plugin.targets` (the shared targets file lives at `host/Directory.Plugin.targets`). The target copies the plugin's primary DLL, `.deps.json`, scoped-CSS bundle, and `wwwroot/` into `host/KnockBox/bin/{Config}/{TFM}/games/{PluginName}/` after `Build`.
- At startup, `Program.cs` calls `PluginLoader.LoadModules(AppContext.BaseDirectory/games)`. Each plugin is loaded into its own `PluginLoadContext` (ALC) rooted at `games/{PluginName}/`; shared contracts (`KnockBox.Core`, BCL, logging/DI abstractions) are deferred to the default ALC so type identity is preserved across the host/plugin boundary.
- Each plugin exposes exactly one `IGameModule` (public parameterless ctor) in `sdk/KnockBox.Core/Plugins/IGameModule.cs`. Its `RegisterServices` typically calls the Core helper `services.AddGameEngine<TEngine>(RouteIdentifier)`, which registers the engine as a singleton *and* as a keyed `AbstractGameEngine` under `RouteIdentifier`. Razor pages inject the concrete engine; `LobbyService.CreateLobbyAsync` resolves the engine generically via `GetKeyedService<AbstractGameEngine>(routeIdentifier)`.
- Plugin `wwwroot/` folders are mounted at `/_content/{PluginName}` by `Program.MapPluginStaticAssets`, matching Blazor's RCL convention. Reference plugin assets as `_content/KnockBox.{GameName}/...`.
- The `RouteIdentifier` on `IGameModule` **must** match the route segment in the plugin's `@page` directive (e.g., `"card-counter"` ↔ `@page "/room/card-counter/{ObfuscatedRoomCode}"`). Mismatch = 404 at navigation time.

### State, engines, and concurrency

- `AbstractGameState` (in `KnockBox.Core`) owns a `SemaphoreSlim(1,1)` and exposes `Execute` / `ExecuteAsync` (mutating, notifies after unlock) and `WithExclusiveRead` / `WithExclusiveReadAsync` (non-mutating, no notification). **All state mutation must go through these** — notification happens *outside* the lock to keep hold time minimal and avoid reentrant deadlocks from subscribers.
- `AbstractGameEngine` subclasses are singletons; they hold no per-room state. Per-room data lives on `AbstractGameState`.
- State change subscriptions use `state.StateChangedEventManager.Subscribe(Func<ValueTask>)`, which returns an `IDisposable` — store it and dispose it in the component's `Dispose()` (inherit `DisposableComponent` from `KnockBox.Core`).
- `PlayerUnregistered` fires *outside* the execute lock so handlers may safely call `Execute` (e.g., advance turn order on disconnect).
- Fallible operations return `Result` / `ValueResult<T>` / `ValueResult<T, TError>`. Use `TryGetSuccess` / `TryGetFailure` / `IsCanceled`. Avoid exceptions for control flow.

### Session lifecycle

- `IUserService` and `IGameSessionService` are scoped per Blazor circuit. `GameSessionState` is transient in DI but cached per user-id by `ISessionServiceProvider`, which keeps the session alive through a **1-minute grace period** when a circuit drops. If the user reconnects within the window the session is re-attached; otherwise `GameSessionState.Dispose()` unregisters the player.
- Lobby URIs are `room/{routeIdentifier}/{guidA}-{guidB}`. Game pages must validate in `OnInitializedAsync` that the session exists and the URL matches `session.LobbyRegistration.Uri`, else redirect home.

### DI registration order (`Program.cs`)

`RegisterRepositories` → `RegisterValidators` → `RegisterStateServices` → `PluginLoader.LoadModules` → `RegisterLogic(pluginLoadResult)` → navigation + drawing services. `RegisterLogic` iterates `pluginLoadResult.Modules`, invokes each module's `RegisterServices`, registers the module as an `IGameModule` singleton (the home page enumerates these to build the game list), and finally registers `GamePluginAssemblies` so `Routes.razor` can bind `AdditionalAssemblies`.

### Adding a new game

Full steps are in `host/KnockBox/Specs/knockbox-platform-architecture.md` under "Adding a New Game". Short version: new Razor Class Library under `host/` referencing only `KnockBox.Core` (`..\..\sdk\KnockBox.Core\KnockBox.Core.csproj`), `<Import Project="..\Directory.Plugin.targets" />`, subclass `AbstractGameState` + `AbstractGameEngine`, add Razor pages under `/room/{route-identifier}/{ObfuscatedRoomCode}` inheriting `DisposableComponent`, implement `IGameModule` calling `AddGameEngine<TEngine>(RouteIdentifier)`. **Do not** add a reference from `KnockBox` to the new project.

## Testing

- MSTest across the solution; `Moq` + `Moq.AutoMock` are available.
- Each production project has a matching `{Name}Tests` project (e.g., `KnockBox.CardCounter` ↔ `KnockBox.CardCounterTests`). `KnockBoxTests` covers the host project and uses `InternalsVisibleTo` (set in `Program.cs`).

## Logging

Serilog is configured in `Program.cs` with console + rolling file sink at `{AppContext.BaseDirectory}/logs/knockbox-.log` (daily roll, 31-day retention). A bootstrap Serilog logger is built separately so `PluginLoader` can log during DI container construction.
