# Making a KnockBox game plugin

End-to-end guide for building a KnockBox party-game plugin against the published NuGet packages. If you're contributing a game to the KnockBox monorepo itself, see the root [`README.md`](../README.md) — it describes the in-repo workflow where plugins live alongside the host in a single solution. This document covers the **external workflow**: you develop your plugin as its own repository, reference KnockBox as NuGet packages, and ship the plugin as DLLs that drop into a host's `games/` folder.

## Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [SemVer + version coupling](#semver--version-coupling)
4. [Step 1 — Scaffold](#step-1--scaffold)
5. [Step 2 — Understand the generated files](#step-2--understand-the-generated-files)
6. [The manifest, capabilities, and `IPluginRegistration`](#the-manifest-capabilities-and-ipluginregistration)
7. [Step 3 — Add game state](#step-3--add-game-state)
8. [Step 4 — Add engine commands](#step-4--add-engine-commands)
9. [Step 5 — Render the game](#step-5--render-the-game)
10. [Step 6 — Run the DevHost](#step-6--run-the-devhost)
11. [Step 7 — Write tests](#step-7--write-tests)
12. [Step 8 — Ship](#step-8--ship)
13. [Advanced patterns](#advanced-patterns)
14. [Invariants checklist](#invariants-checklist)
15. [Troubleshooting](#troubleshooting)
16. [Reference](#reference)

---

## Overview

KnockBox uses a **two-package model**:

- **[`KnockBox.Core`](https://www.nuget.org/packages/KnockBox.Core)** — the contract surface every plugin references. It contains `IGameModule`, `AbstractGameEngine`, `AbstractGameState`, Razor component bases, the `Result` / `ValueResult<T>` types, user/session interfaces, navigation, event manager, FSM scaffolding, etc. Nothing in this package is host-specific.
- **[`KnockBox.Platform`](https://www.nuget.org/packages/KnockBox.Platform)** — the hosting SDK. **Only hosts reference this.** It provides `AddKnockBoxPlatform()` / `UseKnockBoxPlatform()`, the lobby service, the home/error/not-found pages, session-token management, plugin discovery, and static-asset mounting.

A plugin's lifecycle in one paragraph: the host's `PluginLoader` scans a directory at startup, reads each plugin folder's `plugin.json` into an `IPluginManifest`, cross-checks it against the module's in-code `Manifest` property, loads the folder into its own `AssemblyLoadContext`, reflects for the `IGameModule` implementation, activates it (5-second ctor budget), and calls `RegisterServices(IPluginRegistration)` so the plugin can wire its engine into DI. The plugin's `wwwroot/` is mounted at `/_content/{PluginName}`. Blazor's router picks up the plugin's assembly so its `@page` components are routable. When a player creates a lobby, the platform resolves the keyed `AbstractGameEngine` for that `RouteIdentifier`, calls `CreateStateAsync`, stashes the returned state on the lobby, and redirects the host to `/room/{RouteIdentifier}/{ObfuscatedRoomCode}`.

**ALC isolation** is why plugins must reference only `KnockBox.Core`. Each plugin gets its own load context rooted at `games/{PluginName}/`, with its own `AssemblyDependencyResolver` resolving transitive deps from that folder. Shared contracts (the types in `KnockBox.Core`, the BCL, logging/DI abstractions) are deferred to the default ALC so type identity is preserved across the host/plugin boundary. A plugin that references `KnockBox.Platform` drags platform types into the plugin's ALC and breaks identity.

---

## Trust model

> [!WARNING]
> **Your plugin runs inside the host process with no sandbox.** When an operator drops your DLL into their `data/games/` folder, your code gets the same privileges as the host binary: full filesystem access, full network access, the ability to read every Blazor circuit's traffic, and a non-collectible `AssemblyLoadContext` that survives for the life of the process. This isn't a constraint we can tighten in code — it's a property of runtime plugin loading. Plugin authors and plugin operators both take on responsibility.

What this means for you as a **plugin author**:

- **Do not bundle secrets.** Anything shipped in your DLL or `wwwroot/` is visible to anyone who runs your plugin.
- **Sign your releases.** GitHub Releases with SHA-256 checksums at minimum; signed NuGet or signed artifacts preferred. Operators have no other way to verify they're running what you shipped.
- **Pin your dependencies.** Transitive deps resolve from your plugin folder via `AssemblyDependencyResolver`. A compromised transitive package ships in your release.
- **Document your trust posture.** Your README should tell operators what your plugin reads, writes, and sends on the network. The ALC isolation does not prevent any of that.
- **Disclose security issues responsibly.** An exploitable bug in your plugin is an exploitable bug in every host that runs it.

What this means for a **plugin operator** installing your plugin:

- The admin third-party-plugins toggle is off by default. Enabling it is an explicit trust decision.
- Only install plugins from sources the operator knows and can verify.
- Protect `data/games/` with filesystem ACLs; anyone with write access can execute arbitrary code inside the host.

See the [host README's "Installing third-party plugins"](../README.md#installing-third-party-plugins) section for the operator-facing checklist.

---

## Prerequisites

- **.NET 10 SDK**
- **An IDE with good Razor support** — Visual Studio, JetBrains Rider, or VS Code with the C# Dev Kit all work.
- **Basic Blazor familiarity** — you should know what `@page`, `@inject`, `[Parameter]`, and `StateHasChanged` do.

---

## SemVer + version coupling

KnockBox.Core follows SemVer. The plugin-facing contract — `IGameModule`, `IPluginManifest`, `IPluginContext`, `IPluginRegistration`, `AbstractGameEngine`, `AbstractGameState` — is stable inside a major. Pin accordingly:

```xml
<PackageReference Include="KnockBox.Core" Version="[1.0.0,2.0.0)" />
```

- **Plugin authors:** pin `KnockBox.Core >=1.0.0 <2.0.0`. A host on SDK `1.x.x` will refuse to load a plugin compiled against a newer Core (`PluginLoader.InspectDepsJson` reads the plugin's `.deps.json` and compares it against the host's Core version), so locking your floor at `1.0.0` and ceiling at `2.0.0` keeps you forward-compatible across all `1.x` hosts.
- **Hosts:** the `KnockBox` host app and the SDK share a major. Host `1.x.x` runs plugins built against SDK `1.x.x`. Breaking any contract above bumps both to `2.0.0` together — don't mix majors.
- Non-breaking additions (new optional members, new helper extension methods) ship as minor bumps; bug fixes as patch bumps.
- **`AssemblyVersion` is pinned at `1.0.0.0` for the entire v1.x line** so consumers pinned at `[1.0.0, 2.0.0)` never need binding redirects when we ship a minor/patch. The compat gate therefore reads the `AssemblyInformationalVersion` (which carries the full SemVer driven by `-p:Version=…` at pack time) rather than the frozen `AssemblyVersion`. If you're building against a locally-built Core (no `-p:Version` passed), both resolve to `1.0.0.0` and the gate is effectively a no-op.

---

## Step 1 — Scaffold

```bash
dotnet new install KnockBox.Templates
dotnet new knockbox-game -n MyGame --routeIdentifier my-game
cd MyGame
```

This produces:

```
MyGame/
├── MyGame.slnx
├── MyGame/                    # the plugin (Razor Class Library)
├── MyGame.DevHost/            # ASP.NET Core dev harness
└── MyGame.Tests/              # MSTest + Moq tests
```

Open `MyGame.slnx` in your IDE. Every generated file has inline comments describing its role; read them once top-to-bottom before you start editing.

---

## Step 2 — Understand the generated files

### `MyGame/MyGameModule.cs`

Your `IGameModule`. The host's plugin loader activates it via reflection, so it **must** have a public parameterless constructor and must return within **5 seconds** — that constructor runs before DI is built, so it should be nearly empty. The module's `Manifest` property is sourced from an embedded `plugin.json` (via `PluginManifest.FromEmbeddedResourceOrThrow(...)`); the loader reads the same file from disk and rejects the plugin if the two copies disagree.

`RegisterServices` receives an `IPluginRegistration` (not a raw `IServiceCollection`) and you call `registration.AddGameEngine<MyGameGameEngine>()` — this registers the engine as a singleton and re-exposes it as a keyed `AbstractGameEngine` under your manifest's `RouteIdentifier`, which is how the platform's `LobbyService` resolves it. See "The `IPluginRegistration` surface" below for what else is callable.

**Edit:** `plugin.json` for identity (`name`, `description`, `routeIdentifier`, `version`, `capabilities`). Keep the in-code `Manifest` property as-is — it reads the embedded copy.
**Leave alone:** the public parameterless ctor.

### `MyGame/MyGameGameState.cs`

Your per-room state; one instance per active lobby. Subclasses `AbstractGameState`, which provides the host, the roster, the `SemaphoreSlim(1,1)` Execute lock, the `StateChangedEventManager`, player register/kick hooks, and lifecycle events (`OnStateDisposed`, `PlayerUnregistered`).

**Edit:** add your game-specific properties here. Keep setters `private` or `internal` and mutate via `Execute` on this class or from the engine.
**Leave alone:** the constructor signature and base-class inheritance.

### `MyGame/MyGameGameEngine.cs`

Your engine — a **stateless singleton**. The framework creates exactly one instance per host process. Every method takes the room's `AbstractGameState` as a parameter; never cache per-room data on the engine. The `(2, 8)` constructor arguments are the minimum and maximum player counts, enforced by the platform when players join.

Three abstract/virtual hooks with fixed signatures in v1:

```csharp
public abstract Task<ValueResult<AbstractGameState>> CreateStateAsync(
    User host, CancellationToken ct = default);

// Base class — plugins override StartAsyncCore, not this.
public Task<Result> StartAsync(
    User caller, AbstractGameState state, CancellationToken ct = default);

// Plugin override point. Host-identity authorization already happened upstream.
protected abstract Task<Result> StartAsyncCore(
    AbstractGameState state, CancellationToken ct = default);

public virtual Task<bool> CanStartAsync(
    AbstractGameState state, CancellationToken ct = default);
```

`StartAsync(caller, state)` is the public entry point — the base class verifies `caller.Id == state.Host.Id` before delegating to your `StartAsyncCore(state, ct)` override. Plugins never re-check the host identity; that invariant is enforced once, in the platform. If your game needs additional authorization (co-host, party role, etc.), add it at the top of `StartAsyncCore`. `CanStartAsync` composes two protected helpers, `HasValidPlayerCount(state)` and `IsLobbyOpen(state)` — override it and combine them with any game-specific readiness rules.

**Edit:** override `CreateStateAsync` / `StartAsyncCore`, and add your game's commands (`PlaceBid`, `DrawCard`, `Guess`, etc.).
**Leave alone:** the inheritance from `AbstractGameEngine` and the public `StartAsync`.

### `MyGame/Pages/MyGameLobby.razor`

The page players land on after creating or joining a lobby. The `@page` route's middle segment (`my-game`) must match `MyGameModule.RouteIdentifier` verbatim. The page inherits `DisposableComponent`, which gives you a `ComponentDetached` cancellation token and a base `Dispose()` to chain.

**Edit:** the rendering code (the `@if` / `else` branches and the `@code` block's game-specific logic).
**Leave alone:** the `@page` directive's middle segment, the `DisposableComponent` inheritance, the session-validation block at the top of `OnInitializedAsync`, and the subscription-disposal in `Dispose`.

### `MyGame/Components/MyGameTile.razor`

Rendered inside the button the host shows on the home page. The surrounding `<button>` (click handling, disabled state, aria-label) is owned by the host; this component owns the visual content only. Style via scoped CSS (`MyGameTile.razor.css`).

### `MyGame.DevHost/Program.cs`

Your local development host. Uses **explicit** plugin registration (`AddGameModule<MyGameModule>`) so F5 and hot reload work. This is not the production host — ship your plugin as DLLs into a real host's `games/` folder (see Step 8).

### `MyGame.Tests/MyGameGameEngineTests.cs`

MSTest + Moq. Engine tests are the sweet spot — the engine is a plain class with injected loggers, so you can exercise it against a real state without DI. UI tests (Razor pages) need a full Blazor circuit and are better left to manual testing in the DevHost.

---

## The manifest, capabilities, and `IPluginRegistration`

### `plugin.json`

Every plugin folder must contain a `plugin.json`. The template scaffolds one for you; embed the same file in your DLL (as `<EmbeddedResource>`) so `PluginManifest.FromEmbeddedResourceOrThrow(...)` in your `IGameModule` has something to read. The loader parses the on-disk copy before loading the DLL, then cross-checks it against the in-code copy and rejects the plugin on mismatch.

```json
{
  "schemaVersion": 1,
  "name": "My Game",
  "description": "A KnockBox party game.",
  "routeIdentifier": "my-game",
  "version": "1.0.0",
  "entryAssembly": "MyGame",
  "capabilities": []
}
```

Rules:

- `schemaVersion` is pinned at `1`. A future bump will include migration guidance.
- `routeIdentifier` must match `^[a-z0-9-]+$` and be unique across loaded plugins. It is both the URL segment (`/room/{routeIdentifier}/...`) and the DI key for your keyed `AbstractGameEngine`.
- `version` is informational only. Compatibility is gated by the `KnockBox.Core` version the plugin was compiled against, which the loader reads from the plugin's `.deps.json`.
- `entryAssembly` is the simple name of your plugin DLL (no `.dll` extension). The loader resolves `games/{PluginFolder}/{entryAssembly}.dll`.

### Capability gating via `IPluginContext`

`IPluginContext` is a per-plugin bundle of host services (`Logger`, `Configuration`, `Storage`) available to plugin-owned services. Accessing `Configuration` or `Storage` requires the corresponding capability to be declared in `plugin.json`, otherwise first access throws `PluginCapabilityNotGrantedException`:

| Capability | Unlocks |
| --- | --- |
| `config` | `IPluginContext.Configuration` — `IConfiguration` rooted at `Plugins:{routeIdentifier}`. Read host settings without touching the root configuration. |
| `storage` | `IPluginContext.Storage` — `IPluginStorage` rooted at a per-plugin directory under the host's content root. Accepts relative paths only; absolute paths and `..` escapes are rejected with `ArgumentException`. |

`Logger` (`ILogger` with category `Plugins.{routeIdentifier}`) needs no capability.

`IPluginStorage` is a contract-level boundary. Nothing stops your plugin code from calling `System.IO` directly — doing so is an authoring violation, not runtime-enforced. Stick to `IPluginStorage` so the host can relocate the per-plugin root without your plugin breaking.

### The `IPluginRegistration` surface

`RegisterServices(IPluginRegistration registration)` is the **only** way your plugin registers services. You never see a raw `IServiceCollection`. What you can register:

- `registration.AddGameEngine<TEngine>()` — exactly once per plugin. Registers the engine as a singleton and keys the same instance as `AbstractGameEngine` under your manifest's `routeIdentifier`.
- `AddSingleton<TService, TImplementation>()` / `AddScoped<>` / `AddTransient<>` — plugin-private services. Each lifetime has a factory overload (`AddSingleton<T>(Func<IPluginContext, T>)` etc.) for cases where the service needs to capture the plugin's logger or config.

What the registration surface will silently drop (with an error log):

- Any service type in the host's **always-protected set**: `IPluginContext`, `IPluginRegistration`, `IPluginManifest`, `IPluginStorage`, `IGameModule`, `AbstractGameEngine`, `AbstractGameState`, `IConfiguration`, `IHostedService`, `IHostApplicationLifetime`, `ILoggerFactory`, `ILogger`, `ILogger<>`.
- Any service type that was already registered by the host before the plugin loop ran. The host captures a snapshot of its `IServiceCollection` before calling `RegisterServices` on the first plugin; every type in that snapshot is host-owned and cannot be replaced. Closed generics are matched against their open form, so `ILogger<MyPluginService>` is blocked by the host's `ILogger<>` registration.

This is the **denylist** enforced by the sandbox. It is not configurable from plugin code or from host configuration — adding or removing entries requires an SDK release. The rationale: a plugin that can replace host services can change platform behavior for every other plugin in the process.

### Plugin-data directory

When the `storage` capability is declared, `IPluginContext.Storage` reads and writes under `{AppContext.BaseDirectory}/data/plugins/{routeIdentifier}/` (exact layout is host-controlled; the path guard rejects symlink escapes and `..`). Write large artifacts with `OpenWrite(relativePath)`, enumerate with `EnumerateFiles(relativeDir, searchPattern)`.

### Targeting and lifecycle notes

- Plugins target `net10.0`. No multi-targeting. The host is `net10.0` and the ALC isolation only works when runtime identity matches.
- No plugin hot-reload; restarting the host is the only way to pick up new or changed plugins.
- Module constructors must return within **5 seconds**. Hanging ctors log an error and the plugin is skipped.

---

## Step 3 — Add game state

Every mutation must go through `state.Execute(...)` so the room-level lock is held and subscribers are notified afterwards:

```csharp
public class MyGameGameState(User host, ILogger<MyGameGameState> logger)
    : AbstractGameState(host, logger)
{
    public int Round { get; private set; }
    public string? LastWinner { get; private set; }

    // Called from the engine, never from Razor or external callers directly.
    internal void AdvanceRound(string winner) => Execute(() =>
    {
        Round++;
        LastWinner = winner;
    });
}
```

The Execute contract:

1. Acquires the state's `SemaphoreSlim(1,1)`.
2. Runs the lambda.
3. Releases the semaphore.
4. Notifies `StateChangedEventManager` subscribers **after** the lock is released, so handlers can safely call `Execute` again (e.g., a disconnect handler advancing the turn).

For non-mutating reads that need serialization, use `WithExclusiveRead` / `WithExclusiveReadAsync` — those do not fire a notification.

**The `PlayerUnregistered` event** is raised outside the Execute lock. That's deliberate: handlers commonly want to call `Execute` ("advance the turn on disconnect"), which would deadlock if the event fired from inside the lock.

**`SetJoinable(bool)`** flips the lobby's joinable flag and **must be called from inside an `Execute` block**:

```csharp
state.Execute(() => state.SetJoinable(false));
```

Calling it outside the lock is a programmer error — `RegisterPlayer` reads `IsJoinable` from inside its own `Execute`, so an unlocked write races the gate. Every `Execute` always fires `StateChangedEventManager.Notify()` regardless of whether the inner mutation actually changed anything.

**`ScheduleCallback(TimeSpan, Func<Task>)`** schedules a delegate to fire after a delay, bound to the state's lifetime. It returns a `ValueResult<IScheduledCallbackHandle>` — store the handle and call `Cancel()` or `Dispose()` to cancel before the callback fires. Both operations are idempotent and safe to call after the state has been disposed.

---

## Step 4 — Add engine commands

Engine methods are how Razor pages request a mutation. They type-check the state, validate the request, call `state.Execute(...)`, and return a `Result` or `ValueResult<T>`:

```csharp
public Result PlaceBid(AbstractGameState state, User player, int bid)
{
    if (state is not MyGameGameState s)
        return Result.FromError("Invalid state type.", "Internal error.");

    if (bid < 0)
        return Result.FromError("Bid must be non-negative.");

    return s.Execute(() =>
    {
        // Mutate s's fields here.
    });
}
```

Callers consume the result via `TryGetSuccess(out var value)`, `TryGetFailure(out var error)`, or `IsCanceled`. Prefer returning failures over throwing — exceptions in engine code blow up the whole request pipeline rather than surfacing a clean error to the player.

---

## Step 5 — Render the game

The lobby page is the only page your plugin owns. It typically looks like this:

```razor
@page "/room/my-game/{ObfuscatedRoomCode}"
@inherits DisposableComponent

<HeadContent>
    <link href="_content/MyGame/MyGame.styles.css" rel="stylesheet" />
</HeadContent>

@if (GameState is null)
{
    <div>Loading...</div>
}
else if (GameState.IsJoinable)
{
    <LobbyUI State="GameState" OnStart="StartGame" />
}
else
{
    <GameUI State="GameState" Engine="GameEngine" />
}

@code {
    [Inject] MyGameGameEngine GameEngine { get; set; } = default!;
    [Inject] IGameSessionService GameSessionService { get; set; } = default!;
    [Inject] INavigationService NavigationService { get; set; } = default!;
    [Inject] IUserService UserService { get; set; } = default!;

    [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

    private MyGameGameState? GameState;
    private IDisposable? _stateSubscription;   // MUST be disposed

    protected override async Task OnInitializedAsync()
    {
        if (UserService.CurrentUser is null)
            await UserService.InitializeCurrentUserAsync(ComponentDetached);

        if (!GameSessionService.TryGetCurrentSession(out var session))
        {
            NavigationService.ToHome();
            return;
        }

        if (session.LobbyRegistration.State is not MyGameGameState s)
        {
            NavigationService.ToHome();
            return;
        }

        GameState = s;
        GameState.OnStateDisposed += HandleStateDisposed;
        _stateSubscription = GameState.StateChangedEventManager.Subscribe(
            async () => await InvokeAsync(StateHasChanged));
    }

    private void HandleStateDisposed()
    {
        GameSessionService.LeaveCurrentSession(navigateHome: false);
        NavigationService.ToHome();
    }

    public override void Dispose()
    {
        if (GameState is not null)
            GameState.OnStateDisposed -= HandleStateDisposed;
        _stateSubscription?.Dispose();   // non-negotiable
        base.Dispose();
    }
}
```

Non-obvious details worth calling out:

- **Scoped CSS path.** The `_content/MyGame/MyGame.styles.css` convention is Blazor's RCL static-asset convention; the platform mounts your plugin's `wwwroot/` at `/_content/{AssemblyName}` automatically.
- **Session validation.** If no session exists, go home. If the session's state isn't your type, go home. Users commonly land on game URLs without a live session (refresh after disconnect, shared link, etc.).
- **`InvokeAsync(StateHasChanged)`.** Notifications can fire from any thread (e.g., a background tick handler), so you must marshal back to the render dispatcher. A plain `StateHasChanged` from a non-dispatcher thread throws.
- **Dispose the subscription.** `StateChangedEventManager.Subscribe` returns an `IDisposable`. If you forget to dispose it, the state holds a reference to your component's closure and the circuit leaks.

---

## Step 6 — Run the DevHost

```bash
dotnet run --project MyGame.DevHost
```

Open two browser windows (or one regular + one incognito) and navigate to the printed URL. Create a lobby in the first, copy the join code, join in the second, click **Start Game** in the host window, and play.

The DevHost uses `PluginDiscoveryMode.Explicit` with a direct `ProjectReference` to your plugin, so Razor edits hot-reload and break points land in your plugin code. In production the host uses directory discovery and ALC isolation — this is why you can't reference `KnockBox.Platform` from the plugin project (it would pull platform types into the plugin's load context and break identity).

---

## Step 7 — Write tests

Engine tests are the focus. The engine is a plain class with injected loggers; you can instantiate it directly with `Mock.Of<ILogger<T>>()`:

```csharp
[TestClass]
public class MyGameGameEngineTests
{
    private MyGameGameEngine _engine = null!;

    [TestInitialize]
    public void Setup()
    {
        _engine = new MyGameGameEngine(
            Mock.Of<ILogger<MyGameGameEngine>>(),
            Mock.Of<ILogger<MyGameGameState>>());
    }

    [TestMethod]
    public async Task StartAsync_FlipsJoinableOff()
    {
        var host = UserFactory.Create("Host", Guid.CreateVersion7().ToString());
        var createResult = await _engine.CreateStateAsync(host);
        Assert.IsTrue(createResult.TryGetSuccess(out var state));

        var startResult = await _engine.StartAsync(host, state!);

        Assert.IsTrue(startResult.IsSuccess);
        Assert.IsFalse(state!.IsJoinable);
    }
}
```

Tips:

- **Test against a real state**, not a mocked one. `AbstractGameState` is a regular class; its lock is in-process and costs nothing in tests.
- **Use `TryGetSuccess(out var value)`** to unpack `ValueResult<T>` — that's the canonical success-path assertion.
- **Use `UserFactory.Create(name, id)`** to build `User` fixtures. The `User` ctor is `internal` as of v1.0 — the factory is the supported escape hatch for test code. In production, consumers resolve `IUserService.CurrentUser` instead of constructing users directly.
- **`StartAsync(caller, state)` enforces host-identity at the base class.** To test the non-host rejection path, pass a non-host `User` as the first argument and assert `IsFailure`. Plugins override `StartAsyncCore(state, ct)` and never re-check the caller identity.
- **Don't test Razor pages here.** They need a full circuit; exercise them manually in the DevHost or with bUnit in a separate integration-test project.

---

## Step 8 — Ship

To hand your plugin to a production KnockBox host:

1. `dotnet publish MyGame/MyGame.csproj -c Release` — produces the DLL set in `MyGame/bin/Release/net10.0/publish/`.
2. Copy the publish output into the host's plugin folder as `games/MyGame/`. The host expects the folder's `plugin.json` to declare the DLL name in `entryAssembly` (`games/MyGame/MyGame.dll`). Alongside it you want:
   - `plugin.json` — the manifest. The loader reads this *before* the DLL loads, so shape errors (missing fields, unknown capabilities, bad route-identifier) surface as a clean rejection.
   - `MyGame.dll` — the plugin assembly.
   - `MyGame.deps.json` — dependency manifest. `PluginLoader.InspectDepsJson` inspects it for the plugin's `KnockBox.Core` version (plugins compiled against a newer Core than the host are rejected) and for forbidden dependencies.
   - `MyGame.styles.css` — scoped-CSS bundle (if you used scoped CSS).
   - `MyGame.pdb` — optional, useful if ops want symbolicated logs.
   - `wwwroot/` — subfolder with any static assets the plugin serves.
   - Any transitive dependency DLLs that aren't already in the host's default ALC.
3. Restart the host. On startup `PluginLoader` scans the plugin folder, parses `plugin.json` into an `IPluginManifest`, loads `MyGame.dll` into its own ALC, reflects for `IGameModule`, activates `MyGameModule` (5-second ctor budget), cross-checks the module's in-code `Manifest` against the on-disk one, calls `RegisterServices`, and mounts `wwwroot/` at `/_content/MyGame`. KnockBox does **not** support plugin hot-reload — operators restart the host to pick up new plugins.

---

## Advanced patterns

### Phased state

For games that progress through discrete phases (Lobby → BuyIn → InProgress → GameOver), implement `IPhasedGameState<TPhase>`:

```csharp
public enum MyGamePhase { Lobby, InProgress, GameOver }

public class MyGameGameState : AbstractGameState, IPhasedGameState<MyGamePhase>
{
    public MyGamePhase Phase { get; private set; } = MyGamePhase.Lobby;
    public void SetPhase(MyGamePhase phase) => Execute(() => Phase = phase);
    // ...
}
```

Your Razor page can then branch on `state.Phase` and render a dedicated component per phase.

### Tunable config

For games with host-adjustable settings (round count, timers, difficulty), the preferred shape is an **immutable record** held behind a private setter on the state, mutated through an `Execute`-wrapped helper. This makes the lock contract structural — callers can't write to `Settings` without going through the helper, so the state mutation always fires the change notification:

```csharp
public sealed record MyGameSettings
{
    public int Rounds { get; init; } = 5;
    public bool EnableTimers { get; init; } = true;
}

public class MyGameGameState : AbstractGameState
{
    public MyGameSettings Settings { get; private set; } = new();

    public Result UpdateSettings(Func<MyGameSettings, MyGameSettings> mutate) =>
        Execute(() => Settings = mutate(Settings));
}
```

Razor pages then write via `state.UpdateSettings(s => s with { Rounds = 7 })`. See `host/KnockBox.Codeword/Services/State/Games/CodewordGameState.cs` and `host/KnockBox.Codeword/Pages/LobbyPhase.razor.cs` for the full pattern, including localStorage persistence.

> **Legacy:** `IConfigurableGameState<TConfig>` (with a mutable `Config { get; set; }` property) is the older shape used by CardCounter, HiddenAgenda, and DrawnToDress. It still works but lets callers bypass the Execute lock — new games should prefer the immutable-settings shape above.

### FSM-driven state

For games where the command→transition logic is central, use `FiniteStateMachine<TContext, TCommand>` plus `IFsmContextGameState<TContext>`. Each phase is a class implementing `IGameState<TContext, TCommand>` with `OnEnter` / `OnExit` / `HandleCommand`. Use `ITimedGameState<TContext, TCommand>` for phases that advance on a timer.

### Per-player state

For games that track per-player hands, scores, or effects, implement `IPlayerTrackedGameState<TPlayerState>`. The backing `ConcurrentDictionary<string, TPlayerState>` lets background handlers read player entries without taking the state's top-level Execute lock.

### Turn management

`TurnManager` (in `KnockBox.Core.Services.State.Games.Shared.Components`) holds an ordered list of player ids and an index. Pair it with `PlayerUnregistered` so the turn skips disconnected players.

### Scheduled callbacks and tick loops

- **`AbstractGameState.ScheduleCallback`** — schedule a delegate to fire after a delay, bound to the state's lifetime.
- **`ITickService`** (singleton, 20 TPS) — register a callback for fixed-rate logic (animations, timers, periodic state evaluation).

---

## Invariants checklist

Fail any of these and something breaks at load, runtime, or in production:

- ✅ Plugin project references **only** `KnockBox.Core` from the KnockBox package family, pinned `[1.0.0, 2.0.0)`. Referencing `KnockBox.Platform` or another plugin breaks ALC isolation.
- ✅ `plugin.json` is shipped both on disk (next to the DLL) and embedded in the assembly, and the two copies agree. The loader cross-checks them.
- ✅ `IGameModule.Manifest.RouteIdentifier` matches each page's `@page` route segment exactly (same casing, same hyphens). Mismatch = 404 at navigation time.
- ✅ All state mutation flows through `state.Execute` / `ExecuteAsync`. Direct field writes skip the lock and the notification; subscribers stop re-rendering.
- ✅ `SetJoinable` is called **only** from inside an `Execute` block.
- ✅ Every `StateChangedEventManager.Subscribe` return value is disposed in the component's `Dispose()`. Otherwise the circuit leaks. The same applies to `IScheduledCallbackHandle`.
- ✅ Static assets are referenced as `_content/{PluginName}/...`. Match Blazor's RCL convention — custom paths break in production.
- ✅ `IGameModule` implementation has a public parameterless constructor that returns within 5 seconds. The plugin loader uses `Activator.CreateInstance` with no arguments and enforces a timeout.
- ✅ Exactly one `IGameModule` implementation per plugin assembly. Zero = plugin not registered; more than one = platform fails fast.
- ✅ Engine is stateless. One instance, one process — never stash per-room data on the engine.
- ✅ Lobby pages call `engine.StartAsync(UserService.CurrentUser, GameState)`; the base class rejects non-host callers. Plugins override `StartAsyncCore(state, ct)`, never `StartAsync`.
- ✅ Any use of `IPluginContext.Configuration` / `.Storage` is declared in `plugin.json`'s `capabilities`. Undeclared access throws at first read.

---

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| **404 when joining or creating a lobby.** | `RouteIdentifier` doesn't match the page's `@page` route segment. Grep for both in your plugin; they must agree verbatim. |
| **"Game state is not the expected type" / redirect to home.** | The session's state type-check failed. Usually means you stored a new state while the page still held the old one — verify `CreateStateAsync` returns the right type. |
| **Stale UI that never re-renders.** | Missing `StateChangedEventManager.Subscribe(...)`, or missing `InvokeAsync(StateHasChanged)` wrap, or state mutated outside `Execute`. |
| **Circuit leaks / memory grows.** | Missing `Dispose` of the subscription `IDisposable` returned by `Subscribe`. |
| **Assembly load failure / `FileNotFoundException` on plugin boot.** | A transitive dependency is missing from the plugin folder. `dotnet publish` produces the correct set; don't copy only the primary DLL. |
| **Type-identity mismatch / invalid cast at plugin load.** | Plugin referenced `KnockBox.Platform` or a different version of `KnockBox.Core` than the host loaded. Plugins reference only `KnockBox.Core`, same major version as the host. |
| **`Activator.CreateInstance` failure on `IGameModule`.** | Implementation lacks a public parameterless ctor. Remove ctor parameters and move dependencies into `RegisterServices` or the engine. |
| **"Plugin constructor timed out" in host logs.** | Your `IGameModule` ctor took more than 5 seconds. Move any I/O, network, or expensive initialization into `RegisterServices` or into services resolved lazily — the ctor should return almost immediately. |
| **"Plugin manifest mismatch" rejection at load.** | The on-disk `plugin.json` does not match the `Manifest` returned by `IGameModule.Manifest`. Usually because the embedded resource and the disk copy came from different builds, or because the module hard-codes a manifest that disagrees with the file. Embed the same file and read it via `PluginManifest.FromEmbeddedResourceOrThrow`. |
| **`PluginCapabilityNotGrantedException` at runtime.** | Your plugin accessed `IPluginContext.Configuration` or `.Storage` without declaring `config` / `storage` in `plugin.json`'s `capabilities`. Add the capability, rebuild, redeploy. |
| **"Registration dropped: host-owned service type" in logs.** | You called `AddSingleton<IConfiguration, ...>()` or similar on a service in the host's denylist. Plugins cannot replace host services; register plugin-private services instead. |
| **"Plugin compiled against a newer Core" rejection.** | The plugin's `.deps.json` pins `KnockBox.Core >= {X.Y.Z}` where `X.Y.Z` is newer than the host's Core version. Host loads plugins from its own major, same-or-older minor. Rebuild against the host's Core version. |
| **Static asset 404 (CSS / images).** | Wrong path prefix. Use `_content/{PluginAssemblyName}/...` exactly; the assembly name is the project name by default. |

---

## Reference

- [`KnockBox.Core` on NuGet](https://www.nuget.org/packages/KnockBox.Core) — contract package API surface.
- [`KnockBox.Platform` on NuGet](https://www.nuget.org/packages/KnockBox.Platform) — hosting SDK.
- [`KnockBox.Templates` on NuGet](https://www.nuget.org/packages/KnockBox.Templates) — `dotnet new` scaffolding.
- [`host/KnockBox/Specs/knockbox-platform-architecture.md`](../host/KnockBox/Specs/knockbox-platform-architecture.md) — canonical architecture reference (ALC isolation, session lifecycle, DI order, lobby routing). Read this if you need to understand *why* the invariants above exist.
- [Repository root README](../README.md) — in-repo contributor workflow (plugins as in-solution projects).
