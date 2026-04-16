# KnockBox.Core

**Contract package for KnockBox game plugins.**

KnockBox is a Blazor Server host that loads party games as runtime-discovered plugins. Every game plugin is a Razor Class Library that references **only this package** — the host loads the plugin into its own `AssemblyLoadContext` at startup and resolves shared contracts (the types in this package, the BCL, logging/DI abstractions) against the default ALC so type identity is preserved across the host/plugin boundary.

> **Who references this?** Every game plugin project.
> **Who does NOT reference this?** The host — the host references `KnockBox.Platform` which transitively depends on `KnockBox.Core`.

## Getting started

The fastest path is to scaffold a new plugin from the companion template:

```bash
dotnet new install KnockBox.Templates
dotnet new knockbox-game -n MyGame --routeIdentifier my-game
```

The template generates three projects (`MyGame`, `MyGame.DevHost`, `MyGame.Tests`) and wires them together. Every generated file carries inline comments explaining what it does and where your own code goes.

If you'd rather wire up by hand, a minimal plugin is three types:

```csharp
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.DependencyInjection;

// 1. The module — plugin entry point. Must have a public parameterless ctor.
public sealed class MyGameModule : IGameModule
{
    public string Name => "My Game";
    public string Description => "A tiny example game.";
    public string RouteIdentifier => "my-game";  // must match your @page route

    public void RegisterServices(IServiceCollection services)
        => services.AddGameEngine<MyGameEngine>(RouteIdentifier);

    public RenderFragment GetButtonContent() => b => { /* home-page tile */ };
}

// 2. The state — one instance per lobby.
public sealed class MyGameState(User host, ILogger<MyGameState> logger)
    : AbstractGameState(host, logger)
{
    public int Round { get; private set; }
    internal void AdvanceRound() => Execute(() => Round++);  // mutations via Execute
}

// 3. The engine — stateless singleton.
public sealed class MyGameEngine(ILogger<MyGameEngine> logger,
                                 ILogger<MyGameState> stateLogger)
    : AbstractGameEngine(minPlayers: 2, maxPlayers: 8)
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
        User host, CancellationToken ct = default)
    {
        var state = new MyGameState(host, stateLogger);
        state.UpdateJoinableStatus(true);
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    public override Task<Result> StartAsync(
        User host, AbstractGameState state, CancellationToken ct = default)
    {
        if (state is not MyGameState s || host != s.Host)
            return Task.FromResult(Result.FromError("Only the host can start."));
        return Task.FromResult(s.Execute(() => s.UpdateJoinableStatus(false)));
    }
}
```

Then add a Razor page at `/room/my-game/{ObfuscatedRoomCode}` that injects the engine, subscribes to `state.StateChangedEventManager`, and renders the UI.

## What's in this package

| Type / namespace | What it's for |
| --- | --- |
| `IGameModule` | Plugin entry point. Exactly one per plugin assembly. |
| `AbstractGameEngine` | Base class for game engines (DI singleton; stateless). |
| `AbstractGameState` | Base class for per-room state. Owns the Execute lock. |
| `GameModuleServiceCollectionExtensions.AddGameEngine<T>` | Registers an engine as both a singleton and a keyed `AbstractGameEngine`. |
| `DisposableComponent` | Base for Razor pages. Provides `ComponentDetached` and a virtual `Dispose`. |
| `Result` / `ValueResult<T>` / `ValueResult<T,TError>` | Failure-returning types used across engines and services. |
| `User`, `IUserService` | Current user identity (scoped per Blazor circuit). |
| `IGameSessionService` | Active-session accessor (survives a 1-minute disconnect grace period). |
| `INavigationService` | Typed navigation (`ToHome`, `ToGame`, `GetJoinUri`). |
| `IThreadSafeEventManager` / `ThreadSafeEventManager` | Snapshot-based event dispatch used by `AbstractGameState.StateChangedEventManager`. |
| `FiniteStateMachine<TContext,TCommand>`, `IGameState<,>`, `ITimedGameState<,>` | Optional FSM scaffolding for phase-driven games. |
| `TurnManager` | Helper for games that take strict turns. |
| `IPhasedGameState<T>`, `IConfigurableGameState<T>`, `IFsmContextGameState<T>`, `IPlayerTrackedGameState<T>` | Marker interfaces for advanced state patterns. |

## The concurrency contract

Every mutation on `AbstractGameState` must flow through `Execute(Action)` or `ExecuteAsync(Func<Task>)`. The base class:

1. Acquires the state's `SemaphoreSlim(1,1)`.
2. Runs your lambda.
3. Releases the semaphore.
4. Fires `StateChangedEventManager` **after** the lock is released — so subscribers (e.g., disconnect handlers) can safely re-enter `Execute` without deadlocking.

For serialized non-mutating reads use `WithExclusiveRead` / `WithExclusiveReadAsync` — those do not fire a notification.

The `PlayerUnregistered` event is also raised **outside** the Execute lock, so your handler can call `Execute` (e.g., "advance the turn on disconnect") safely.

## Developer reference

A full end-to-end guide — scaffolding, state, engine, Razor, DevHost, tests, shipping, advanced patterns — lives at:

https://github.com/jcub1011/KnockBox/blob/main/docs/making-a-game-plugin.md

## Related packages

- [`KnockBox.Templates`](https://www.nuget.org/packages/KnockBox.Templates) — `dotnet new` template pack that scaffolds a plugin + dev host + tests in one command.
- [`KnockBox.Platform`](https://www.nuget.org/packages/KnockBox.Platform) — hosting SDK. Reference this from **host** projects, never from plugins.

## License

MIT. See `LICENSE.txt` in the repository.
