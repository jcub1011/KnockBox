# Changelog

All notable changes to the KnockBox SDK packages (`KnockBox.Core`, `KnockBox.Platform`, `KnockBox.Plugins.Analyzer`, `KnockBox.Templates`) are documented here. The host app and first-party plugins share the SDK's major version line.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). See [`docs/making-a-game-plugin.md`](docs/making-a-game-plugin.md#semver--version-coupling) for the plugin-author pinning policy.

## [1.0.0] — Unreleased

First stable release. Freezes the public contract of `IGameModule`, `IPluginManifest`, `IPluginContext`, `IPluginRegistration`, `AbstractGameEngine`, and `AbstractGameState`. Plugin authors should pin `KnockBox.Core [1.0.0, 2.0.0)`.

### Breaking changes

**`AbstractGameEngine`**
- `StartAsync` signature is now `(AbstractGameState state, CancellationToken ct = default)`. The `User host` parameter was removed. Caller-identity enforcement ("only the host may start the game") moves to each plugin's lobby page — mirror the existing `KickPlayer` pattern there. The host is still reachable on the engine side via `state.Host` if needed.
- `CanStartAsync` signature is now `(AbstractGameState state, CancellationToken ct = default)`. The new `ct` parameter is `= default`, so overrides may but need not consume it.
- New `protected bool HasValidPlayerCount(AbstractGameState state)` helper factored out of the default `CanStartAsync`. Override `CanStartAsync` and compose with the helper to add game-specific readiness rules.

**`AbstractGameState`**
- `UpdateJoinableStatus(bool)` is replaced by `SetJoinable(bool)`. `SetJoinable` **must** be called from inside an `Execute` / `ExecuteAsync` block — the setter itself does not lock and readers that gate on `IsJoinable` (for example `RegisterPlayer`) rely on the outer `Execute` to serialize the transition. Migration: `state.UpdateJoinableStatus(false)` → `state.Execute(() => state.SetJoinable(false))`.
- **Minor behavior change:** every `Execute` / `ExecuteAsync` now fires `StateChangedEventManager.Notify()` when it releases the lock, regardless of whether the inner mutation actually changed anything. The pre-1.0 `UpdateJoinableStatus` suppressed the notify when the value was unchanged. Callers that need same-value idempotence must gate inside the lambda.
- `RegisterPlayer` now wraps its entire body (joinable gate, kicked-player check, name-disambiguation, registration token construction) in a single `Execute` to avoid a TOCTOU window. Callers that used to wrap `RegisterPlayer` in their own `Execute` must stop — the non-reentrant semaphore will deadlock. `LobbyService.JoinLobbyAsync` has been updated accordingly.
- `ScheduleCallback(TimeSpan, Func<Task>)` now returns `ValueResult<IScheduledCallbackHandle>` instead of `ValueResult<CancellationTokenSource>`. The new handle exposes `Cancel()` and `IDisposable.Dispose()` — both idempotent and safe to call after the owning state has been disposed. Consumers no longer leak the underlying CTS.
- `Execute` / `ExecuteAsync` now catch `ObjectDisposedException` separately from other exceptions and report a unified public message `"State was disposed."` on both the already-disposed and during-execute paths.
- `PlayerUnregistered` and `OnStateDisposed` invocation lists are now iterated with per-handler error isolation — one throwing subscriber does not short-circuit the rest.

**`IGameModule`** (Phase 2 precursor — already landed in the v1 design, re-documented here for completeness)
- The interface no longer exposes `Name`, `Description`, or `RouteIdentifier` directly. Those fields moved to `IPluginManifest`, reachable via `IGameModule.Manifest`. A plugin typically populates the manifest from an embedded `plugin.json` via `PluginManifest.FromEmbeddedResourceOrThrow(typeof(MyModule).Assembly)`; the loader reads the same file from disk and rejects the plugin on mismatch.
- `RegisterServices` now receives an `IPluginRegistration`, not a raw `IServiceCollection`. Plugins call `registration.AddGameEngine<TEngine>()` (no explicit route key — it uses the plugin's own manifest), plus `AddSingleton` / `AddScoped` / `AddTransient` for plugin-private services. Factory overloads accept `Func<IPluginContext, T>` so plugin services can close over their per-plugin logger / configuration / storage.

**Plugin sandbox (host-side)**
- Plugin registrations targeting **host-owned service types** are silently dropped and logged at error level. The denylist is the union of a static `AlwaysProtectedTypes` set (plugin-system primitives + Microsoft.Extensions fundamentals like `IConfiguration`, `IHostedService`, `ILogger<>`) and a dynamic snapshot of the host's `IServiceCollection` captured before the plugin loop runs. Closed generics are matched against their open form. The denylist is not host-configurable; adding entries requires an SDK release.
- Plugins whose `.deps.json` pins `KnockBox.Core` to a version **newer** than the host's are rejected by `PluginLoader.InspectDepsJson`. SemVer prerelease and build-metadata suffixes are stripped before the compare.
- Plugin module constructors are subject to a **5-second timeout**. Hanging ctors log an error and the plugin is skipped.
- `IPluginContext.Configuration` and `.Storage` are capability-gated. Plugins must declare `config` / `storage` in their `plugin.json` `capabilities` array; otherwise first access throws `PluginCapabilityNotGrantedException`.

**`User` / `IUserService`**
- `User`'s constructor is now `internal`. External test code constructs fixtures via `UserFactory.Create(name, id)` in `KnockBox.Core.Services.State.Users`. In-repo tests in the SDK's test projects use `InternalsVisibleTo`.
- `User.Name` is now `{ get; internal set; }`. External code mutates the current user's name via the new `IUserService.SetCurrentUserName(string)`.
- The public `NameChanged` event on `User` is removed. Subscribe to `IUserService.UserNameChanged` instead. The event is raised after the trim + 12-character cap are applied; a subscriber that throws is isolated (logged and dropped) and does not short-circuit the rest of the invocation list.

### Added

- **Hosted-service shutdown hook on `LobbyService`.** Implements `IHostedService`; `StopAsync` snapshots `_lobbies`, clears the dictionary, and disposes every open `AbstractGameState` (each in a try/catch). A clean host shutdown now releases all state semaphores and cancels every outstanding `ScheduleCallback`.
- **`TimeProvider` support in `SessionServiceProvider`.** A test-friendly constructor accepts an explicit `TimeProvider`; the production ctor defaults to `TimeProvider.System`. `Task.Delay(delay, timeProvider, token)` lets tests advance a `FakeTimeProvider` to drive eviction synchronously.
- **`IScheduledCallbackHandle`** (replacing the leaked `CancellationTokenSource`) — new primitive in `KnockBox.Core.Primitives.Disposable`.
- **`UserFactory.Create(string name, string id)`** — public test-fixture factory for `User`, replacing external `new User(...)` calls.
- **NuGet packaging metadata** across `KnockBox.Core` and `KnockBox.Platform`: `Deterministic`, `ContinuousIntegrationBuild` (CI-conditional), `PublishRepositoryUrl`, `EmbedUntrackedSources`, `IncludeSymbols`, `SymbolPackageFormat=snupkg`, `RepositoryType=git`, and a `Microsoft.SourceLink.GitHub` reference. `dotnet pack` now emits a companion `.snupkg` for each of those packages; SourceLink metadata embedded in the PDB lets consumers step into SDK source from NuGet.org. The `.github/workflows/release-sdk.yml` workflow pushes both `.nupkg` and `.snupkg` to nuget.org.
- **`SemVer + version coupling`** section in `docs/making-a-game-plugin.md` and a matching policy header comment in `release-sdk.yml` documenting the `[1.0.0, 2.0.0)` plugin pin and the host↔SDK major-share rule.

### Removed

- Pre-v1 engine-level "non-host caller rejected" tests across the 8 first-party plugin test projects (7 tests total). The behavior moved to the lobby page in the new `StartAsync` shape, so those engine contracts no longer exist. Razor-page-level enforcement is currently untested — flagged as a follow-up.
- `UpdateJoinableStatus_SameValue_DoesNotFireStateChanged` test. With the new shape, every `Execute` fires `Notify` regardless of whether the inner mutation changed anything (see the minor behavior change note above).

### Internal notes (non-contract)

These do not affect the public API but are relevant to anyone tracking the implementation:

- `PluginLoader.FindForbiddenDependency` was replaced by `InspectDepsJson` which returns both the forbidden-dep hit and the compiled-against Core version in a single pass. Malformed `.deps.json` files now log a warning with the exception + DLL path instead of silently swallowing.
- `DefaultPluginStorage` delegates path-traversal and symlink-escape checks to a new shared `PluginPathGuard`; the same guard is used when `MapPluginStaticAssets` validates each plugin's `wwwroot/` root before mounting.
- `SessionServiceProvider`'s eviction timer now runs on `TimeProvider.Delay` so tests can drive it via `FakeTimeProvider.Advance(...)` without real wall-clock waits.
- `Home.razor.cs` previously double-disposed a lobby's state when the host left (`CloseLobbyAsync` already disposes the state; the `disposeAction` closure was disposing it a second time). The redundant call was removed.
