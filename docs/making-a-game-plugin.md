# Making a KnockBox game plugin

End-to-end guide for building a KnockBox party-game plugin against the published NuGet packages. If you're contributing a game to the KnockBox monorepo itself, see the root [`README.md`](../README.md) — it describes the in-repo workflow where plugins live alongside the host in a single solution. This document covers the **external workflow**: you develop your plugin as its own repository, reference KnockBox as NuGet packages, and ship the plugin as DLLs that drop into a host's `games/` folder.

## Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Step 1 — Scaffold](#step-1--scaffold)
4. [Step 2 — Understand the generated files](#step-2--understand-the-generated-files)
5. [Step 3 — Add game state](#step-3--add-game-state)
6. [Step 4 — Add engine commands](#step-4--add-engine-commands)
7. [Step 5 — Render the game](#step-5--render-the-game)
8. [Step 6 — Run the DevHost](#step-6--run-the-devhost)
9. [Step 7 — Write tests](#step-7--write-tests)
10. [Step 8 — Ship](#step-8--ship)
11. [Advanced patterns](#advanced-patterns)
12. [Invariants checklist](#invariants-checklist)
13. [Troubleshooting](#troubleshooting)
14. [Reference](#reference)

---

## Overview

KnockBox uses a **two-package model**:

- **[`KnockBox.Core`](https://www.nuget.org/packages/KnockBox.Core)** — the contract surface every plugin references. It contains `IGameModule`, `AbstractGameEngine`, `AbstractGameState`, Razor component bases, the `Result` / `ValueResult<T>` types, user/session interfaces, navigation, event manager, FSM scaffolding, etc. Nothing in this package is host-specific.
- **[`KnockBox.Platform`](https://www.nuget.org/packages/KnockBox.Platform)** — the hosting SDK. **Only hosts reference this.** It provides `AddKnockBoxPlatform()` / `UseKnockBoxPlatform()`, the lobby service, the home/error/not-found pages, session-token management, plugin discovery, and static-asset mounting.

A plugin's lifecycle in one paragraph: the host's `PluginLoader` scans a directory at startup, loads each plugin folder into its own `AssemblyLoadContext`, reflects for the `IGameModule` implementation, activates it, and calls `RegisterServices` so the plugin can wire its engine into DI. The plugin's `wwwroot/` is mounted at `/_content/{PluginName}`. Blazor's router picks up the plugin's assembly so its `@page` components are routable. When a player creates a lobby, the platform resolves the keyed `AbstractGameEngine` for that `RouteIdentifier`, calls `CreateStateAsync`, stashes the returned state on the lobby, and redirects the host to `/room/{RouteIdentifier}/{ObfuscatedRoomCode}`.

**ALC isolation** is why plugins must reference only `KnockBox.Core`. Each plugin gets its own load context rooted at `games/{PluginName}/`, with its own `AssemblyDependencyResolver` resolving transitive deps from that folder. Shared contracts (the types in `KnockBox.Core`, the BCL, logging/DI abstractions) are deferred to the default ALC so type identity is preserved across the host/plugin boundary. A plugin that references `KnockBox.Platform` drags platform types into the plugin's ALC and breaks identity.

---

## Prerequisites

- **.NET 10 SDK**
- **An IDE with good Razor support** — Visual Studio, JetBrains Rider, or VS Code with the C# Dev Kit all work.
- **Basic Blazor familiarity** — you should know what `@page`, `@inject`, `[Parameter]`, and `StateHasChanged` do.

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

Your `IGameModule`. The host's plugin loader activates it via reflection, so it **must** have a public parameterless constructor. In `RegisterServices` you call `services.AddGameEngine<MyGameGameEngine>(RouteIdentifier)` — this registers the engine as a singleton and re-exposes it as a keyed `AbstractGameEngine` under your `RouteIdentifier`, which is how the platform's `LobbyService` resolves it.

**Edit:** the `Name`, `Description`, and (once, at scaffold time) `RouteIdentifier`.
**Leave alone:** the public parameterless ctor.

### `MyGame/MyGameGameState.cs`

Your per-room state; one instance per active lobby. Subclasses `AbstractGameState`, which provides the host, the roster, the `SemaphoreSlim(1,1)` Execute lock, the `StateChangedEventManager`, player register/kick hooks, and lifecycle events (`OnStateDisposed`, `PlayerUnregistered`).

**Edit:** add your game-specific properties here. Keep setters `private` or `internal` and mutate via `Execute` on this class or from the engine.
**Leave alone:** the constructor signature and base-class inheritance.

### `MyGame/MyGameGameEngine.cs`

Your engine — a **stateless singleton**. The framework creates exactly one instance per host process. Every method takes the room's `AbstractGameState` as a parameter; never cache per-room data on the engine. The `(2, 8)` constructor arguments are the minimum and maximum player counts, enforced by the platform when players join.

**Edit:** override `CreateStateAsync` / `StartAsync`, and add your game's commands (`PlaceBid`, `DrawCard`, `Guess`, etc.).
**Leave alone:** the inheritance from `AbstractGameEngine`.

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
        var host = new User("Host", Guid.CreateVersion7().ToString());
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
- **Don't test Razor pages here.** They need a full circuit; exercise them manually in the DevHost or with bUnit in a separate integration-test project.

---

## Step 8 — Ship

To hand your plugin to a production KnockBox host:

1. `dotnet publish MyGame/MyGame.csproj -c Release` — produces the DLL set in `MyGame/bin/Release/net10.0/publish/`.
2. Copy the publish output into the host's plugin folder as `games/MyGame/`. The host expects the primary DLL to be named after the folder (`games/MyGame/MyGame.dll`). Alongside it you want:
   - `MyGame.dll` — the plugin assembly.
   - `MyGame.deps.json` — dependency manifest (used by `AssemblyDependencyResolver` to load transitive deps from the plugin folder).
   - `MyGame.styles.css` — scoped-CSS bundle (if you used scoped CSS).
   - `MyGame.pdb` — optional, useful if ops want symbolicated logs.
   - `wwwroot/` — subfolder with any static assets the plugin serves.
   - Any transitive dependency DLLs that aren't already in the host's default ALC.
3. Restart the host. On startup `PluginLoader` scans the plugin folder, loads `MyGame.dll` into its own ALC, reflects for `IGameModule`, activates `MyGameModule`, calls `RegisterServices`, and mounts `wwwroot/` at `/_content/MyGame`.

If the host supports hot-swapping plugins, check its docs for the drop-in procedure; a vanilla KnockBox host requires a restart to pick up new plugins.

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

For games with host-adjustable settings (round count, timers, difficulty), implement `IConfigurableGameState<TConfig>`:

```csharp
public record MyGameConfig { public int Rounds { get; init; } = 5; }

public class MyGameGameState : AbstractGameState, IConfigurableGameState<MyGameConfig>
{
    public MyGameConfig Config { get; set; } = new();
    // ...
}
```

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

- ✅ Plugin project references **only** `KnockBox.Core` from the KnockBox package family. Referencing `KnockBox.Platform` or another plugin breaks ALC isolation.
- ✅ All state mutation flows through `state.Execute` / `ExecuteAsync`. Direct field writes skip the lock and the notification; subscribers stop re-rendering.
- ✅ `IGameModule.RouteIdentifier` matches each page's `@page` route segment exactly (same casing, same hyphens). Mismatch = 404 at navigation time.
- ✅ Every `StateChangedEventManager.Subscribe` return value is disposed in the component's `Dispose()`. Otherwise the circuit leaks.
- ✅ Static assets are referenced as `_content/{PluginName}/...`. Match Blazor's RCL convention — custom paths break in production.
- ✅ `IGameModule` implementation has a public parameterless constructor. The plugin loader uses `Activator.CreateInstance` with no arguments.
- ✅ Exactly one `IGameModule` implementation per plugin assembly. Zero = plugin not registered; more than one = platform fails fast.
- ✅ Engine is stateless. One instance, one process — never stash per-room data on the engine.

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
| **Static asset 404 (CSS / images).** | Wrong path prefix. Use `_content/{PluginAssemblyName}/...` exactly; the assembly name is the project name by default. |

---

## Reference

- [`KnockBox.Core` on NuGet](https://www.nuget.org/packages/KnockBox.Core) — contract package API surface.
- [`KnockBox.Platform` on NuGet](https://www.nuget.org/packages/KnockBox.Platform) — hosting SDK.
- [`KnockBox.Templates` on NuGet](https://www.nuget.org/packages/KnockBox.Templates) — `dotnet new` scaffolding.
- [`host/KnockBox/Specs/knockbox-platform-architecture.md`](../host/KnockBox/Specs/knockbox-platform-architecture.md) — canonical architecture reference (ALC isolation, session lifecycle, DI order, lobby routing). Read this if you need to understand *why* the invariants above exist.
- [Repository root README](../README.md) — in-repo contributor workflow (plugins as in-solution projects).
