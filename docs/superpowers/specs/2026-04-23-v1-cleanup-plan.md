# KnockBox Pre-v1.0 Cleanup — Implementation Plan

## Status at a glance

| Phase | Scope | Status |
|---|---|---|
| **Phase 1** | `AbstractGameState` / `AbstractGameEngine` API breaking changes | ✅ Complete |
| **Phase 2** | Plugin-loader & sandbox hardening | ✅ Complete |
| **Phase 3** | Operational hygiene (shutdown, eviction, TimeProvider) | ✅ Complete |
| **Phase 4** | `User` API tightening | ✅ Complete |
| **Phase 5** | Packaging & release metadata | ✅ Complete |
| **Phase 6** | Docs & release notes | ✅ Complete |

Totals after Phase 6: **1565 tests passing** (426 SDK + 1139 host), 0 failures, 7 skipped (pre-existing Windows symlink tests). Phase 6 is docs-only — same test count as end of Phase 4.

---

## Context

KnockBox ships the SDK (`KnockBox.Core`, `KnockBox.Platform`, `KnockBox.Templates`) as independently versioned NuGet packages; third-party plugins take a source dependency on `KnockBox.Core`. Once v1.0 tags the SDK, the public surface of `AbstractGameEngine`, `AbstractGameState`, `IGameModule`, `IPluginManifest`, `IPluginContext`, and `IPluginRegistration` is effectively frozen under semver. This plan absorbs the breaking changes and hygiene fixes that are cheap now and expensive later.

SDK targeting stays `net10.0` only; this plan does not multi-target.

Work is split into six phases, ordered by blast radius. Phase 1 ripples into every first-party plugin, so it goes first.

---

## Phase 1 — `AbstractGameState` / `AbstractGameEngine` API breaking changes — ✅ Complete

- [x] **1.1** Replace `ScheduleCallback`'s leaked CTS with `IScheduledCallbackHandle` (`sdk/KnockBox.Core/Primitives/Disposable/IScheduledCallbackHandle.cs` new; private `ScheduledCallbackHandle` nested inside `AbstractGameState`)
- [x] **1.2** Move joinable-status writes inside execute lock — `UpdateJoinableStatus` → `SetJoinable`, `RegisterPlayer` now wraps its entire body (gate + mutation) in `Execute`, `LobbyService.JoinLobbyAsync` drops its outer `Execute` wrap to avoid deadlocking the non-reentrant semaphore
- [x] **1.3** Give `Execute`/`Dispose` race a specific error — all four overloads catch `ObjectDisposedException` separately; unified public message "State was disposed." across both already-disposed and during-execute paths
- [x] **1.4** Per-handler error isolation — added private `SafeInvoke` / `SafeInvoke<T>` helpers; applied to both `PlayerUnregistered` sites (line 218, 258) and `OnStateDisposed` (line 515)
- [x] **1.5** Add `CancellationToken ct = default` to `CanStartAsync`
- [x] **1.6** Drop `User host` from `StartAsync` — caller-identity check moved into each plugin's lobby page (lobby pages already had `UserService.CurrentUser` and `State.Host`; same pattern as existing `KickPlayer` check)
- [x] **1.7** Factor `HasValidPlayerCount` helper out of default `CanStartAsync`
- [x] **1.8** Propagate new signatures into **8** first-party plugins — CardCounter, Codeword, DiceSimulator, DrawnToDress, HiddenAgenda, Operator, Spardle, TaskMaster
- [x] **Phase 1 tests** — new `Phase1VerificationTests.cs` with 7 tests: dispose-race specific error (sync + async), `IScheduledCallbackHandle` idempotence + survival across state dispose, handler-chain non-short-circuit on both `PlayerUnregistered` and `OnStateDisposed`, concurrent-worker smoke test for `SetJoinable`/`RegisterPlayer` serialization

### Phase 1 audit corrections worked during execution

- Plan-draft static `SetJoinable` wouldn't compile — engines aren't subclasses of `AbstractGameState`. Made public with strong XML doc ("must be called inside Execute") instead.
- Plan-draft "gate under `WithExclusiveRead`, mutation outside" had a TOCTOU: another thread could flip `IsJoinable` between gate and add. Corrected by running the entire `RegisterPlayer` body inside a single `Execute`.
- 7 obsolete engine-level "non-host caller rejected" tests deleted across plugin test projects. The behavior moved to the lobby page; those tests no longer represent a meaningful engine contract. Razor-page-level enforcement is now an untested gap — follow-up.
- Deleted `UpdateJoinableStatus_SameValue_DoesNotFireStateChanged`. With the new shape, `Execute` always fires `Notify` regardless of whether the inner mutation changed anything. Minor behavior change; CHANGELOG entry needed.

---

## Phase 2 — Plugin-loader & sandbox hardening — ✅ Complete

- [x] **2.1** Host-service denylist — hybrid design: small static `AlwaysProtectedTypes` (plugin-system primitives + Microsoft.Extensions fundamentals) UNION dynamic `IServiceCollection` snapshot captured at the top of `LogicRegistrations.RegisterLogic`. Self-maintaining — any future platform service registered before the plugin loop is protected automatically. Reordered `AddKnockBoxPlatform` so `INavigationService` + `ISvgClipboardService` register before `RegisterLogic`.
- [x] **2.2** Plugin version guard — `PluginLoader.InspectDepsJson` (replacing static `FindForbiddenDependency`) returns both the forbidden-dep hit and the plugin's compiled-against `KnockBox.Core` version. Plugins compiled against a Core version newer than the host's are rejected with a clear error. SemVer prerelease/build suffixes (`-alpha`, `+build`) stripped before `Version.TryParse`.
- [x] **2.3** Path-traversal defense for wwwroot mounts — new `sdk/KnockBox.Platform/Plugins/PluginPathGuard.cs` factored from inline checks in `DefaultPluginStorage`. `MapPluginStaticAssets` now validates every plugin's wwwroot against the plugins root before mounting; symlink/junction escapes are logged and skipped.
- [x] **2.4** Plugin module constructor timeout — 5-second timeout on `Activator.CreateInstance` via `Task.Run` + `Wait(timeout)`. Hanging ctor logs error and the module is skipped.
- [x] **2.5** `.deps.json` parse failure now logs — `InspectDepsJson`'s `catch (Exception)` changed from silent-return to a warning log with the exception and DLL path.
- [ ] **2.6** Document `ForbiddenPluginDependencies` is not configurable — deferred to Phase 6 (docs).
- [x] **Phase 2 tests** — `DefaultPluginRegistrationTests` (9 tests, covering both deny-set sources + plugin-private success + `AddGameEngine` always works + `CaptureHostOwnedServiceTypes` closed-to-open-generic promotion); `PluginPathGuardTests` (7 tests); `InspectDepsJson` tests rewritten (4 tests including warning-log on malformed JSON); 2 new `LoadModules` tests for version rejection (newer-Core rejected, older-Core still loads).

### Phase 2 open gaps

- **Ctor-timeout has no end-to-end verification test.** A slow-ctor `IGameModule` fixture in `CoreTests` would slow every `LoadModules` test by 5 seconds because `FindMatchingModule` activates every discovered module type. The right home is a separate fixture assembly (extending `EmbeddedManifestFixture` or a new `SlowCtorFixture`). The `Task.Run` + `Wait(timeout)` code path itself is straightforward and exercised indirectly by every activation.
- **`MapPluginStaticAssets` wwwroot-reject branch untested.** Full test requires a `WebApplication`; the guard's inputs are covered by `PluginPathGuardTests` but the branch in `MapPluginStaticAssets` is untested. Candidate once `bUnit`/`WebApplicationFactory` enters the test pipeline.

---

## Phase 3 — Operational hygiene — ✅ Complete

- [x] **3.1** Graceful lobby cleanup on host shutdown — `LobbyService` now implements `IHostedService`. `StopAsync` snapshots `_lobbies`, clears the dictionary, disposes every state (each in a try/catch). Registered as concrete singleton with `ILobbyService` and `IHostedService` bridges so there's exactly one instance.
- [x] **3.2** Host-eviction closes lobby — **already wired via the `disposeAction` closure in `Home.razor.cs`**. When `GameSessionState.Dispose()` fires after the eviction grace period, it runs `TakeCurrentSession()?.Dispose()` → the closure → `LobbyService.CloseLobbyAsync`. Fixed a pre-existing double-dispose bug (removed `lobby.State.Dispose()` from the closure — `CloseLobbyAsync` already disposes). Documented the eviction trigger in the closure's comment. No new event plumbing introduced.
- [x] **3.3** Inject `TimeProvider` into `SessionServiceProvider` — added test-friendly constructor overload accepting `TimeProvider`; production ctor defaults to `TimeProvider.System`. `StartEvictionTimer` now uses the `Task.Delay(delay, timeProvider, token)` overload so `FakeTimeProvider.Advance(...)` drives eviction synchronously.
- [x] **Phase 3 tests** — `SessionServiceProviderTests` (3 tests covering reconnect-before-grace, reconnect-after-grace with dispose verified, reconnect-exactly-before-deadline + cancellation of eviction timer); `LobbyServiceShutdownTests` (1 test: create lobby, `StopAsync`, assert state disposed + dict empty). Added `Microsoft.Extensions.TimeProvider.Testing` to `KnockBox.PlatformTests.csproj`.

### Phase 3 open gaps

- **No end-to-end test for the host-eviction-closes-lobby chain.** Chain crosses the Blazor scoped-service boundary (`GameSessionState.Dispose` → `TakeCurrentSession()` → `Home.razor.cs` closure → `LobbyService.CloseLobbyAsync`). Individual pieces are covered: `SessionServiceProviderTests` verifies eviction fires and disposes the scoped service; `LobbyServiceShutdownTests` verifies the lobby-cleanup side. Full chain needs `bUnit` or `WebApplicationFactory`.

### Phase 3 answered question

- "Does an empty lobby with no non-host players get auto-closed after N minutes, or only closed when the host evicts?" — **Only when the host evicts.** No timer for empty-lobby cleanup.

---

## Phase 4 — `User` API tightening — ✅ Complete

- [x] **4.1** Lock down `User` construction and mutation — ctor now `internal`; `Name` is `{ get; internal set; }`; the public `NameChanged` event on `User` is gone. Notification moved to `IUserService.UserNameChanged` (new event) with per-handler isolation. Trim + 12-char cap moved into new `IUserService.SetCurrentUserName(string)`; `Home.razor.cs` now calls that instead of assigning `CurrentUser.Name` directly. The internal setter on `Name` is still used by `AbstractGameState.RegisterPlayer`'s name-disambiguation pass (same assembly, so `internal` access works).
- [x] **4.2** Added `UserFactory.Create(name, id)` in `KnockBox.Core.Services.State.Users` as the public escape hatch. Plugin-author test code constructs `User` via the factory; in-repo tests migrated off `new User(...)` across ~60 files (the only remaining in-repo `new User(...)` is the single production site in `UserService.InitializeCurrentUserAsync`).
- [x] **Phase 4 tests** — new `sdk/KnockBox.PlatformTests/Unit/UserServiceTests.cs` with 12 tests. Covers:
  - reflection pins on `User` ctor + `Name` setter being `internal` and `Id` having no setter;
  - `UserFactory.Create` basic construction;
  - `SetCurrentUserName` trim, 12-char cap, event fire with correct previous/new args, idempotence when trimmed value equals current;
  - persistence to local storage via the fire-and-forget `SaveNameAsync`;
  - one-bad-handler-doesn't-short-circuit fan-out (mirrors Phase 1.4 pattern);
  - null-`CurrentUser` and post-dispose no-ops.

### Phase 4 audit corrections worked during execution

- Plan-draft ripple list flagged `host/KnockBox.DrawnToDress/Pages/OutfitCustomizationPhase.razor[.cs]` as a `NameChanged` subscriber. It isn't — the `OnOutfitNameChangedAsync` method there is the **outfit** name's handler (a draft-name-for-a-drawing), unrelated to `User.NameChanged`. Ripple was limited to `UserService` itself.
- The plan's "compile check on an external-style test that tries to construct `User`" is not expressible as a runtime test. Replaced with reflection-based pins: `User_Constructor_IsInternal`, `User_NameSetter_IsInternal`, `User_IdSetter_DoesNotExist`. These fail the moment the accessibility weakens, which is the practical guarantee we wanted.

---

## Phase 5 — Packaging & release metadata — ✅ Complete

- [x] **5.1** Added Microsoft-recommended NuGet metadata to `KnockBox.Core.csproj`, `KnockBox.Platform.csproj`, `KnockBox.Templates.csproj`. Properties: `Deterministic`, `ContinuousIntegrationBuild` (conditional on `CI=true`), `PublishRepositoryUrl`, `EmbedUntrackedSources`, `IncludeSymbols`, `SymbolPackageFormat=snupkg`, `RepositoryType=git`. Templates has no compiled output so only got the repro/CI/repo flags (no `IncludeSymbols` block — would be a no-op). `Microsoft.SourceLink.GitHub 8.0.0` added to Core and Platform with `PrivateAssets="all"`. Verified locally: `dotnet pack -c Release` emits `.nupkg` + `.snupkg` for Core and Platform; Core's PDB contains a SourceLink JSON document that maps `C:\Users\...\KnockBox\*` → `https://raw.githubusercontent.com/jcub1011/KnockBox/{HEAD}/*`, and the nuspec embeds `<repository type="git" url="https://github.com/jcub1011/KnockBox" commit="..."/>`.
- [x] **5.2** Updated `.github/workflows/release-sdk.yml` to push both `*.nupkg` and `*.snupkg` to nuget.org. Uses `shopt -s nullglob` so the Templates job (which emits no `.snupkg`) doesn't try to push a literal glob. Artifact-upload step now captures both file types, and the GitHub Release step attaches both. GitHub Actions sets `CI=true` automatically, so the `ContinuousIntegrationBuild` conditional activates without workflow-level changes; added a comment on the Pack step explaining this.
- [x] **5.3** SemVer policy documented in the `release-sdk.yml` file header and in `docs/making-a-game-plugin.md` under a new "SemVer + version coupling" section placed between Prerequisites and Step 1. Plugin authors pin `KnockBox.Core [1.0.0, 2.0.0)`; host and SDK share a major; breaking contracts bumps both to `2.0.0`.

### Phase 5 notes

- `KnockBox.Plugins.Analyzer` is packable but is not in the release workflow's matrix and not in the plan's file list, so was left untouched. If it starts shipping, the same metadata block should be added there (minus `IncludeSymbols` — analyzer is `IncludeBuildOutput=false`, so symbols would go unpacked).
- Deterministic-build flag requires the packing runner to set `CI=true`. Local `dotnet pack` does NOT set it, so local builds still have absolute paths in PDBs (SourceLink maps them anyway). Production releases on GitHub Actions get full reproducibility.
- No test count change — Phase 5 is csproj/yaml/docs only.

---

## Phase 6 — Docs & release notes — ✅ Complete

- [x] **6.1** Plugin-author guide (`docs/making-a-game-plugin.md`) refreshed — new top-level section "The manifest, capabilities, and `IPluginRegistration`" (covers `plugin.json` shape, capability gating via `IPluginContext`, the `IPluginRegistration` surface + host-service denylist including the static always-protected set, plugin-data directory layout, 5-second ctor budget, net10.0 only, no hot-reload, caller-identity-check-is-the-page's-job, and `ForbiddenPluginDependencies` is not configurable); Step 2 Module/Engine descriptions + Step 3 state prose updated with v1.0 signatures (`SetJoinable` inside `Execute`, `IScheduledCallbackHandle`, `StartAsync(state, ct)` and `CanStartAsync(state, ct)` with `HasValidPlayerCount` helper); Step 7 test snippet migrated to `StartAsync(state!)` + `UserFactory.Create`; Step 8 ship checklist gained `plugin.json` and `.deps.json` entries with their rejection paths; Invariants checklist extended with `plugin.json` both-copies-agree, `SetJoinable` inside `Execute`, `IScheduledCallbackHandle` disposal, 5s ctor budget, caller-identity, and capability-declaration rules; Troubleshooting table gained 5 new rows covering ctor timeout, manifest mismatch, capability-not-granted, denylist-dropped, newer-Core rejection.
- [x] **6.2** Architecture doc (`host/KnockBox/Specs/knockbox-platform-architecture.md`) synced — Solution Structure table lists all 8 first-party plugins + SDK NuGets + analyzer + template pack; `IGameModule` contract replaced with manifest-based shape; new subsections on `plugin.json` + `IPluginManifest` and on `IPluginContext`/`IPluginRegistration`; `PluginLoader` description rewritten to reflect plugin.json parsing, deps.json inspection with forbidden-dep + Core-version gates, 5s ctor timeout, cross-check; `AddGameEngine` helper moved to `IPluginRegistration`; `AbstractGameEngine` code block updated (`StartAsync(state, ct)`, `CanStartAsync` with CT + `HasValidPlayerCount` helper); `AbstractGameState` code block updated (`SetJoinable`, `IScheduledCallbackHandle`), plus a paragraph on the always-fire-notify behavior change and `ObjectDisposedException` unified message; `User` section rewritten for internal ctor + `UserFactory.Create`; `IUserService` code block extended with `UserNameChanged` + `SetCurrentUserName` + `ResetIdentityAsync`; call-flow diagram and Start Game / Adding a New Game sections updated to match Phase 1 signatures; new sections on host-service denylist, lobby lifecycle hooks (`IHostedService` shutdown + eviction-closes-lobby chain), and SDK versioning / compatibility policy; Technology Stack table updated (ScheduleCallback → `IScheduledCallbackHandle`, plugin sandbox row added).
- [x] **6.3** Created `CHANGELOG.md` at repo root with an unreleased `[1.0.0]` entry organized into Breaking changes (engine, state, module, sandbox, User/IUserService), Added (shutdown hook, TimeProvider, IScheduledCallbackHandle, UserFactory, NuGet packaging metadata + SourceLink, SemVer doc), Removed (7 obsolete plugin tests + 1 same-value-notify test), and Internal notes. Includes the explicit minor-behavior-change note on `Execute` always firing `Notify` regardless of whether the inner mutation changed anything.
- [x] **6.4** Fixed `CLAUDE.md`'s plugin count: "seven first-party game plugins" → "eight first-party game plugins (CardCounter, Codeword, DiceSimulator, DrawnToDress, HiddenAgenda, Operator, Spardle, TaskMaster)". Also extended the `sdk/KnockBox.Sdk.slnx` line to include the analyzer and PlatformTests. The empty `KnockBox.ConsultTheCard` folders were removed by the user during Phase 6 — no leftover references in docs.

### Phase 6 notes

- Phase 6 is docs-only. No csproj, code, or workflow changes; test count is unchanged at 1565.
- `OutfitCustomizationPhase.razor[.cs]` did NOT subscribe to `User.NameChanged` — the `OnOutfitNameChangedAsync` handler is the outfit name, unrelated to the user's name. The Phase 4 ripple list in earlier plan drafts was wrong about that site; no migration needed there.

---

## Out-of-scope but flagged for follow-up

1. **`OnStateDisposed` handler contract:** `Dispose` fires `OnStateDisposed` before `_disposeCts.Cancel()` and well before `_executeLock.Dispose()`. A subscriber that calls `Execute` from inside `OnStateDisposed` will succeed but race with the rest of disposal. Document the contract explicitly in the v1.0 XML doc so plugin authors know what's legal.
2. **`_kickedPlayers` is never cleared:** kicked-set grows unbounded for the lobby's lifetime. Probably intentional ("kicked stays kicked"), but should be an explicit behavior contract somewhere.
3. **Razor-page-level host-check for `StartAsync` has no test coverage.** The engine no longer enforces it; each plugin's lobby page does. Follow-up integration test via `bUnit` or `WebApplicationFactory`.
4. **Ctor-timeout has no end-to-end verification test** (Phase 2.4). Needs a separate slow-ctor fixture assembly.
5. **`MapPluginStaticAssets` wwwroot-reject branch untested** (Phase 2.3).
6. **End-to-end host-eviction-closes-lobby test** (Phase 3.2). Needs `bUnit`/`WebApplicationFactory`.

---

## Critical files

| Area | Files |
|---|---|
| State/engine primitives | `sdk/KnockBox.Core/Services/State/Games/Shared/AbstractGameState.cs`, `sdk/KnockBox.Core/Services/Logic/Games/Engines/Shared/AbstractGameEngine.cs` |
| Scheduled callback handle | `sdk/KnockBox.Core/Primitives/Disposable/IScheduledCallbackHandle.cs` |
| Sandbox enforcement | `sdk/KnockBox.Platform/Plugins/DefaultPluginRegistration.cs`, `sdk/KnockBox.Core/Plugins/IPluginRegistration.cs`, `sdk/KnockBox.Platform/Services/Registrations/Logic/LogicRegistrations.cs` |
| Plugin loader | `sdk/KnockBox.Core/Plugins/PluginLoader.cs`, `sdk/KnockBox.Core/Plugins/IPluginManifest.cs` |
| Path guards | `sdk/KnockBox.Platform/Plugins/PluginPathGuard.cs`, `sdk/KnockBox.Platform/Plugins/DefaultPluginStorage.cs`, `sdk/KnockBox.Platform/KnockBoxPlatformExtensions.cs` |
| Lobby / session lifetime | `sdk/KnockBox.Platform/Services/Logic/Games/Shared/LobbyService.cs`, `sdk/KnockBox.Platform/Services/State/Shared/SessionServiceProvider.cs`, `sdk/KnockBox.Platform/Components/Pages/Home/Home.razor.cs` |
| User API (Phase 4) | `sdk/KnockBox.Core/Services/State/Users/IUserService.cs`, `sdk/KnockBox.Platform/Services/State/Users/UserService.cs` |
| Plugin engines (Phase 1 ripple, 8 plugins) | `host/KnockBox.{CardCounter,Codeword,DiceSimulator,DrawnToDress,HiddenAgenda,Operator,Spardle,TaskMaster}/**/*Engine.cs` |
| Packaging (Phase 5) | all three SDK `.csproj`, `.github/workflows/release-sdk.yml` |
| Docs (Phase 6) | `docs/making-a-game-plugin.md`, `host/KnockBox/Specs/knockbox-platform-architecture.md`, `CHANGELOG.md` (new) |

---

## Verification

Run after each phase; all must stay green.

1. **SDK tests:** `dotnet test sdk/KnockBox.Sdk.slnx` — last run (end of Phase 3): 414 pass / 0 fail / 7 skipped.
2. **Host tests:** `dotnet test host/KnockBox.Host.slnx` — last run (end of Phase 3): 1139 pass / 0 fail.
3. **End-to-end smoke (Phase 6):** `dotnet run --project host/KnockBox/KnockBox.csproj`; create a lobby in browser A, join in browser B, start the game, close browser A, wait 90s, confirm the lobby is removed and no unhandled exceptions in logs.
4. **Packaging (Phase 5):** `dotnet pack sdk/KnockBox.Sdk.slnx -c Release`; inspect both `.nupkg` and `.snupkg` with NuGet Package Explorer; confirm SourceLink metadata in the PDB and deterministic-build flag in the `.nuspec`.
5. **Pre-tag rehearsal (Phase 5):** tag `0.9.0-rc.1`, run `release-sdk.yml`, confirm both `.nupkg` and `.snupkg` are published, before the real `1.0.0` tag.
