# Changelog

All notable changes to the KnockBox SDK packages (`KnockBox.Core`, `KnockBox.Platform`, `KnockBox.Plugins.Analyzer`, `KnockBox.Templates`) are documented here. The host app and first-party plugins share the SDK's major version line.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). See [`docs/making-a-game-plugin.md`](docs/making-a-game-plugin.md#semver--version-coupling) for the plugin-author pinning policy.

## [1.0.0] — Unreleased

First stable release. Freezes the public contract of `IGameModule`, `IPluginManifest`, `IPluginContext`, `IPluginRegistration`, `AbstractGameEngine`, and `AbstractGameState`. Plugin authors should pin `KnockBox.Core [1.0.0, 2.0.0)`.

### Breaking changes

**`AbstractGameEngine`**
- `StartAsync` is now a **sealed public entry point** on the base class with signature `(User caller, AbstractGameState state, CancellationToken ct = default)`. It verifies `caller.Id == state.Host.Id` and then delegates to a new abstract `protected Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)`. Plugins override `StartAsyncCore`, not `StartAsync`; host-identity authorization is enforced once, in the platform, rather than duplicated across every plugin's lobby page. Migration: rename `override StartAsync(AbstractGameState, ...)` → `override StartAsyncCore(AbstractGameState, ...)`; drop any in-engine `host != state.Host` check (the base class now handles it).
- `CanStartAsync` signature is now `(AbstractGameState state, CancellationToken ct = default)`. The new `ct` parameter is `= default`, so overrides may but need not consume it.
- The default `CanStartAsync` composes two protected helpers: `HasValidPlayerCount(state)` (player count within `[MinPlayerCount, MaxPlayerCount]`) AND `IsLobbyOpen(state)` (state is still joinable). Either helper may be used independently by plugin overrides that need to split the two concerns.

**`AbstractGameState`**
- `UpdateJoinableStatus(bool)` is replaced by `SetJoinable(bool)`. `SetJoinable` **must** be called from inside an `Execute` / `ExecuteAsync` block — the setter itself does not lock and readers that gate on `IsJoinable` (for example `RegisterPlayer`) rely on the outer `Execute` to serialize the transition. Debug builds assert the execute lock is held (`Debug.Assert(_executeLock.CurrentCount == 0, ...)`). Migration: `state.UpdateJoinableStatus(false)` → `state.Execute(() => state.SetJoinable(false))`.
- `Players` changes type from `IReadOnlyList<User>` to `IReadOnlyList<PlayerEntry>`, where `PlayerEntry` is a new `public readonly record struct PlayerEntry(User User, string DisplayName, IDisposable Token)`. `DisplayName` is the per-lobby display name (after name-collision disambiguation); `User` is the authoritative identity owned by `IUserService`. Rationale: name-disambiguation now updates the per-lobby `DisplayName` only; `User.Name` is never mutated, so a player renamed "Alice (1)" in one lobby keeps "Alice" in `IUserService.CurrentUser` and in every other lobby. Migration: consumers that iterated `state.Players` for `user.Id` / `user.Name` now read `entry.User.Id` / `entry.DisplayName`. `KickPlayer(...)` still takes a `User`, reachable via `entry.User`.
- `RegisterPlayer` now wraps its entire body (joinable gate, kicked-player check, name-disambiguation, registration token construction) in a single `Execute` to avoid a TOCTOU window. Callers that used to wrap `RegisterPlayer` in their own `Execute` must stop — the non-reentrant semaphore will deadlock. `LobbyService.JoinLobbyAsync` has been updated accordingly. `RegisterPlayer` no longer mutates `player.Name` during disambiguation.
- `ScheduleCallback(TimeSpan, Func<Task>)` now returns `ValueResult<IScheduledCallbackHandle>` instead of `ValueResult<CancellationTokenSource>`. The new handle exposes `Cancel()` and `IDisposable.Dispose()` — both idempotent and safe to call after the owning state has been disposed. Consumers no longer leak the underlying CTS.
- `Execute` / `ExecuteAsync` now catch `ObjectDisposedException` separately from other exceptions and report a unified public message `"State was disposed."` on both the already-disposed and during-execute paths. `StateChangedEventManager.Notify()` fires **only on the success path** — an action that throws (or is canceled) does not trigger a state-changed notification to subscribers.
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
- `User.Name` is now `{ get; internal set; }`. External code mutates the current user's name via the new `IUserService.SetCurrentUserName(string)`. Per-lobby display-name disambiguation lives on `PlayerEntry.DisplayName` and never touches `User.Name`.
- `UserFactory.Create(name, id)` now applies the same normalization as `IUserService.SetCurrentUserName`: trim whitespace and cap at 12 characters. The old no-normalization behavior is available under the new `UserFactory.CreateUnchecked(name, id)` — intended only for tests that exercise pre-normalization paths.
- The public `NameChanged` event on `User` is removed. Subscribe to `IUserService.UserNameChanged` instead. The event is raised after the trim + 12-character cap are applied; a subscriber that throws is isolated (logged and dropped) and does not short-circuit the rest of the invocation list.

### Added

- **Hosted-service shutdown hook on `LobbyService`.** Implements `IHostedService`; `StopAsync` flips an internal shutdown flag, then snapshots `_lobbies`, clears the dictionary, and disposes every open `AbstractGameState` (each in a try/catch). A clean host shutdown now releases all state semaphores and cancels every outstanding `ScheduleCallback`. Post-`StopAsync`, `CreateLobbyAsync` returns a failure `Result` rather than leaking a new lobby whose state would never get disposed.
- **`TimeProvider` support in `SessionServiceProvider`.** A test-friendly constructor accepts an explicit `TimeProvider`; the production ctor defaults to `TimeProvider.System`. `Task.Delay(delay, timeProvider, token)` lets tests advance a `FakeTimeProvider` to drive eviction synchronously.
- **`IScheduledCallbackHandle`** (replacing the leaked `CancellationTokenSource`) — new primitive in `KnockBox.Core.Primitives.Disposable`.
- **`UserFactory.Create(string name, string id)`** — public test-fixture factory for `User`, replacing external `new User(...)` calls. Now trims and caps the name to match production. **`UserFactory.CreateUnchecked(name, id)`** is the escape hatch for tests that need un-normalized input.
- **`PlayerEntry`** record struct in `KnockBox.Core.Services.State.Games.Shared`, returned by `AbstractGameState.Players`. Carries the authoritative `User`, the per-lobby `DisplayName` (after disambiguation), and the registration `Token`.
- **NuGet packaging metadata** across `KnockBox.Core` and `KnockBox.Platform`: `Deterministic`, `ContinuousIntegrationBuild` (CI-conditional), `PublishRepositoryUrl`, `EmbedUntrackedSources`, `IncludeSymbols`, `SymbolPackageFormat=snupkg`, `RepositoryType=git`, a pinned `AssemblyVersion=1.0.0.0` (so consumers pinned at `[1.0.0, 2.0.0)` never need binding redirects when we ship a minor/patch), `FileVersion=$(Version)`, and a `Microsoft.SourceLink.GitHub` reference. `dotnet pack` now emits a companion `.snupkg` for each of those packages; SourceLink metadata embedded in the PDB lets consumers step into SDK source from NuGet.org. The `.github/workflows/release-sdk.yml` workflow pushes both `.nupkg` and `.snupkg` to nuget.org. `PluginLoader` reads `AssemblyInformationalVersion` (which carries the full SemVer driven by `-p:Version=…` at pack time) for the plugin-compat gate, so the gate is not fooled by the frozen `AssemblyVersion`.
- **`SemVer + version coupling`** section in `docs/making-a-game-plugin.md` and a matching policy header comment in `release-sdk.yml` documenting the `[1.0.0, 2.0.0)` plugin pin and the host↔SDK major-share rule.

### Removed

- `UpdateJoinableStatus_SameValue_DoesNotFireStateChanged` test. The new `Execute` shape fires `Notify` on every successful execute; same-value idempotence is not a contract. Callers that want it must gate inside the lambda.

### Internal notes (non-contract)

These do not affect the public API but are relevant to anyone tracking the implementation:

- `PluginLoader.FindForbiddenDependency` was replaced by `InspectDepsJson` which returns both the forbidden-dep hit and the compiled-against Core version in a single pass. Malformed `.deps.json` files now log a warning with the exception + DLL path instead of silently swallowing.
- `PluginLoader.ResolveHostCoreVersion` reads `AssemblyInformationalVersion` first (carries the full SemVer at pack time) and falls back to `AssemblyName.Version`. This decouples the compat gate from the frozen `AssemblyVersion=1.0.0.0`.
- `DefaultPluginStorage` delegates path-traversal and symlink-escape checks to a new shared `PluginPathGuard`; the same guard is used when `MapPluginStaticAssets` validates each plugin's `wwwroot/` root before mounting.
- `SessionServiceProvider`'s eviction timer now runs on `TimeProvider.Delay` so tests can drive it via `FakeTimeProvider.Advance(...)` without real wall-clock waits.
- `Home.razor.cs` previously double-disposed a lobby's state when the host left (`CloseLobbyAsync` already disposes the state; the `disposeAction` closure was disposing it a second time). The redundant call was removed.
