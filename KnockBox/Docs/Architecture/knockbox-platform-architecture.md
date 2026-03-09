# KnockBox — Game Host Platform Architecture

## Vision

A Blazor Server web application that hosts multiple browser-based party games under a single platform. Players create or join lobbies using short lobby codes, similar to Jackbox-style party games. The architecture treats lobby management as shared infrastructure and individual games as pluggable modules, allowing new games to be added with minimal changes to the core platform.

---

## Solution Structure

The solution is split into four main projects plus their corresponding test projects:

| Project | Type | Purpose |
|---|---|---|
| `KnockBox` | ASP.NET Core Web App (Blazor Server) | Entry point: Razor pages, routing, DI bootstrapping, database, middleware |
| `KnockBox.Core` | Class Library | Shared platform infrastructure: extensions, `AbstractGameState`, `AbstractGameEngine`, `IRandomNumberService`, result types, thread-safety utilities |
| `KnockBox.CardCounter` | Class Library | Card Counter game logic and state |
| `KnockBox.DiceSimulator` | Class Library | Dice Simulator game logic and state |
| `KnockBox.CoreTests` | MSTest | Unit tests for `KnockBox.Core` |
| `KnockBox.CardCounterTests` | MSTest | Unit tests for `KnockBox.CardCounter` |
| `KnockBox.DiceSimulatorTests` | MSTest | Unit and integration tests for `KnockBox.DiceSimulator` |
| `KnockBoxTests` | MSTest | Integration tests for the main `KnockBox` project (repository layer, etc.) |

`KnockBox` references all three class libraries. `KnockBox.CardCounter` and `KnockBox.DiceSimulator` each reference `KnockBox.Core`.

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
 │  │  DiceSimulatorGameEngine        │  │
 │  │  CardCounterGameEngine          │  │
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

Game engine resolution uses `IServiceProvider.GetService<T>()` with a `GameType` switch expression. Lobby codes are 6-character uppercase alphanumeric strings, generated cryptographically and filtered through `IProfanityFilter`. Code generation and release are handled by `ILobbyCodeService`.

```csharp
public interface ILobbyService
{
    Task<ValueResult<LobbyRegistration>> CreateLobbyAsync(User host, GameType gameType, CancellationToken ct = default);
    Task<ValueResult<UserRegistration>> JoinLobbyAsync(User user, string lobbyCode, CancellationToken ct = default);
    Task<Result> CloseLobbyAsync(User user, LobbyRegistration registration, CancellationToken ct = default);
}
```

### LobbyRegistration

A lightweight container representing a single game session. Holds the lobby code (6-char string), the lobby URI (used for navigation), the `GameType` enum value, and a reference to the `AbstractGameState`. The lobby does not track a status enum — the game's joinability is owned by the state via the `IsJoinable` property, and lobby lifetime is tied to the host's circuit. When a player joins, the lobby provides them with the game state reference via `UserRegistration`.

```csharp
public class LobbyRegistration(string lobbyCode, string lobbyUri, GameType gameType, AbstractGameState state)
{
    public readonly string Code = lobbyCode;
    public readonly string Uri = lobbyUri;       // e.g. "room/dice-simulator/{guidA}-{guidB}"
    public readonly GameType GameType = gameType;
    public readonly AbstractGameState State = state;
}
```

### User

Players are identified by a `User` class containing `Name` (max 12 characters, trimmed) and `Id` (a UUIDv7 string). The `Id` is unique per Blazor circuit and is used for all authorization checks, action routing, and player tracking. `Name` is the player's chosen display name, persisted to browser `localStorage` via JS interop. The `User` class fires a `NameChanged` event when the name is mutated.

```csharp
public class User(string name, string id)
{
    public string Name { get; set; }  // Capped to 12 chars, trimmed, fires NameChanged
    public string Id => id;
    public event Action<UserNameChangedArgs>? NameChanged;
}
```

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

    public abstract Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default);
    public abstract Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default);

    public virtual Task<bool> CanStartAsync(AbstractGameState state)
    {
        return Task.FromResult(
            MinPlayerCount <= state.Players.Count
            && state.Players.Count <= MaxPlayerCount
            && state.IsJoinable);
    }
}
```

`AbstractGameEngine` is purely server-side, concerned only with game logic and state transitions. It has no knowledge of UI components.

### AbstractGameState

An abstract base class that defines the minimal contract shared across all games — the host, the player list, joinability status, a concurrency lock, a built-in event manager, and a scheduled callback mechanism. Each game implements its own concrete subclass with strongly-typed properties for that game's specific state.

The game state instance is created when the lobby is created via the engine's `CreateStateAsync()` factory method, and the same instance is used from lobby through gameplay. Players join and subscribe to this state immediately. When the host starts the game, `StartAsync` mutates the existing state in place — there is no state replacement or re-subscription required.

All state mutations go through `Execute` (sync) or `ExecuteAsync` (async), which acquire a per-state `SemaphoreSlim(1, 1)`, execute the mutation, release the lock, and then notify all subscribers. Notification happens *after* the lock is released to keep lock duration minimal and to prevent reentrant deadlocks if a listener callback triggers another mutation. Each listener is invoked with error isolation so that a failing subscriber does not prevent others from being notified.

Subscriptions are obtained via `state.StateChangedEventManager.Subscribe(Func<ValueTask>)`, which returns an `IDisposable`. Razor components store the subscription and dispose it when the component is detached, preventing dead callbacks from accumulating.

The `PlayerUnregistered` event is fired after a player is successfully removed from the game (disconnected, left, or kicked). It is raised *outside* the execute lock so subscribers may safely call `Execute` in response.

#### Exclusive Read Access

The state exposes `WithExclusiveRead` and `WithExclusiveReadAsync` for non-mutating reads that still need serialization with the execute lock. Unlike `Execute`/`ExecuteAsync`, these do *not* call `NotifyStateChanged` after releasing the lock.

#### Scheduled Callbacks

The state exposes a `ScheduleCallback` method that allows game engines to schedule delayed state transitions. `ScheduleCallback` accepts a `TimeSpan` delay and a `Func<Task>` action, and returns a `ValueResult<CancellationTokenSource>` that the caller can use to cancel the scheduled callback before it fires. Internally, the scheduled action is executed via `ExecuteAsync` when the delay elapses, ensuring it follows the same locking and notification semantics as any player-driven mutation. All outstanding callbacks are automatically cancelled when the state is disposed.

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
    public void UpdateJoinableStatus(bool isJoinable);

    public ValueTask<Result> ExecuteAsync(Func<ValueTask> action, CancellationToken ct = default);
    public Result Execute(Action action);
    public ValueResult<TReturn> Execute<TReturn>(Func<TReturn> action);
    public ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default);
    public Result WithExclusiveRead(Action action);
    public ValueResult<CancellationTokenSource> ScheduleCallback(TimeSpan delay, Func<Task> action);
}
```

### IUserService

A scoped service (one per Blazor circuit) that manages the current user's identity. On `InitializeCurrentUserAsync`, loads the stored username from browser `localStorage` (falls back to "Not Set") and creates a `User` with a UUIDv7 ID. Persists name changes back to `localStorage` by subscribing to the `User.NameChanged` event.

```csharp
public interface IUserService
{
    User? CurrentUser { get; }
    Task InitializeCurrentUserAsync(CancellationToken ct = default);
}
```

### IGameSessionService / GameSessionState

`IGameSessionService` is a **scoped** proxy (one per Blazor circuit) that provides circuit-level concerns — navigation via `INavigationService` — while delegating all persistent session state to a user-id-backed `GameSessionState` instance retrieved from `IIDBackedServiceProvider`.

`GameSessionState` is a **transient** state holder registered in the DI container so `IIDBackedServiceProvider` can cache exactly one instance per user session id, surviving Blazor circuit breaks. It owns the `UserRegistration` field and implements `IDisposable`: when the ID-backed provider disposes it after the post-disconnect grace period, it removes the user from the game state without requiring an active circuit.

This two-layer design means a user who temporarily loses their WebSocket connection (network hiccup, page refresh) is **not** removed from the game lobby — the `GameSessionState` instance persists in `IIDBackedServiceProvider` until a new circuit connects for the same user id, cancelling the disposal timer.

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

### IDBackedCircuitHandler

A scoped `CircuitHandler` that bridges Blazor circuit lifecycle events to `IIDBackedServiceProvider`. On `OnConnectionUpAsync` it calls `NotifyCircuitActive`, and on `OnCircuitClosedAsync` it calls `NotifyCircuitClosed`, using the id from `IUserService.CurrentUser`. When all circuits for a user close, `IIDBackedServiceProvider` starts its 5-minute disposal timer; if the user reconnects within that window the timer is cancelled and the per-user services (including `GameSessionState`) are retained.

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

Each game owns its own routable Razor pages. Lobby URIs are constructed as `room/{gameType}/{guidA}-{guidB}` where `{gameType}` comes from the `NavigationString` attribute on the `GameType` enum and the GUID pair is an opaque identifier (it does *not* contain the lobby join code).

```csharp
public enum GameType
{
    [Description("Split The Deck")]
    [NavigationString("split-the-deck")]
    SplitTheDeck,

    [Description("Dice Simulator")]
    [NavigationString("dice-simulator")]
    DiceSimulator,

    [Description("Card Counter")]
    [NavigationString("card-counter")]
    CardCounter,
}
```

Game pages declare matching `@page` directives, e.g., `@page "/room/dice-simulator/{ObfuscatedRoomCode}"`. The `NavigationString` attribute value must match the route segment used in the game's page directive.

Each game controls its full user experience: the lobby layout, gameplay phases, transitions, and any game-specific sub-flows. The platform imposes no UI constraints on games.

**Security check on navigation:** When a user lands on a game page, the page validates in `OnInitializedAsync` that (1) the user has an active session in `IGameSessionService` and (2) the URI in the URL matches the session's registered lobby URI. If either check fails, the user is redirected home.

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
        │                                                                  │    host, state)      │
        │                                                        ┌─────────────────────────┐      │
        │                                                        │  Execute:                │      │
        │                                                        │    Acquire lock          │      │
        │                                                        │    UpdateJoinableStatus  │      │
        │                                                        │    (false)               │      │
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

1. Player selects a game type from the home page.
2. `LobbyService.CreateLobbyAsync` resolves the selected game's `AbstractGameEngine` from DI via `IServiceProvider`, calls `engine.CreateStateAsync(host)` to obtain the concrete game state, generates a unique 6-character lobby code via `ILobbyCodeService` (cryptographically random, profanity-filtered), constructs an obfuscated lobby URI (`room/{gameType}/{guidA}-{guidB}`), creates a `LobbyRegistration`, and stores it in the `ConcurrentDictionary`.
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
2. The game engine's `StartAsync(host, state)` is called. It verifies the caller is the host, then calls `state.Execute(...)` to initialize game data and close the lobby (`UpdateJoinableStatus(false)`).
3. The state notifies all subscribers, causing all circuits to re-render.

### Gameplay

1. A player performs an action (e.g., rolls dice, draws a card).
2. The game's Razor page calls a method on the injected game engine (e.g., `engine.RollDice(player, state, action)`).
3. The engine method calls `state.Execute(() => { ... })`, which acquires the lock, runs the mutation, releases the lock, and then notifies all subscribers.
4. All subscribed Razor components re-render with the updated state via `InvokeAsync(StateHasChanged)`.

### Player Disconnect

1. A player's browser tab closes or their circuit drops.
2. `IDBackedCircuitHandler.OnCircuitClosedAsync` calls `NotifyCircuitClosed`, which starts `IIDBackedServiceProvider`'s 5-minute grace period timer for the user's id.
3. If the user reconnects within 5 minutes (new circuit with the same session id), `NotifyCircuitActive` cancels the timer and the `GameSessionState` is retained — the player rejoins the game lobby seamlessly.
4. If the timer expires, `IDBackedServiceProvider` disposes all cached services for that id, including `GameSessionState`. `GameSessionState.Dispose()` calls `TakeCurrentSession()?.Dispose()`, which disposes the `UserRegistration`. The unregistration token disposes, removing the player from the game state and notifying subscribers. The `PlayerUnregistered` event is also fired, allowing game engines to react (e.g., `CardCounterGameEngine` uses this to advance the turn order).
5. The host is fixed — there is no host transfer on disconnect.

---

## Adding a New Game

Adding a game requires these steps:

1. **Subclass `AbstractGameState`** — define a concrete state class with the strongly-typed properties your game needs. The host, player list, lock, subscription, notification, and scheduled callback infrastructure is inherited.

2. **Subclass `AbstractGameEngine`** — implement `CreateStateAsync(User host)` to return the concrete state instance (wrapped in `ValueResult`), implement `StartAsync(User host, AbstractGameState state)` to begin gameplay, and add game-specific action methods. Each method calls `state.Execute`/`state.ExecuteAsync` — locking and notification are handled automatically.

3. **Create Razor page(s)** — add one or more pages inheriting `DisposableComponent` with `@page "/room/{navigation-string}/{ObfuscatedRoomCode}"`. Inject the concrete engine via DI, subscribe to `state.StateChangedEventManager`, validate the session in `OnInitializedAsync`, and dispose the subscription in `Dispose()`.

4. **Add the `GameType` enum value** — add an entry to the `GameType` enum with `[Description("...")]` and `[NavigationString("...")]` attributes.

5. **Register in DI** — add the engine to `LogicRegistrations.RegisterLogic()`:
   ```csharp
   services.AddSingleton<MyGameEngine>();
   ```

6. **Add the engine resolution** — add the case to the `GameType` switch in `LobbyService.CreateLobbyAsync`:
   ```csharp
   GameType.MyGame => serviceProvider.GetService<MyGameEngine>(),
   ```

No changes to the lobby service interface, join flow, navigation infrastructure, or any other game's code.

---

## DI Registration

DI is organized into registration extension methods called from `Program.cs`:

### RegisterLogic() — Singletons

| Interface | Implementation | Purpose |
|---|---|---|
| `IProfanityFilter` | `ProfanityFilter` | Aho-Corasick profanity detection |
| `ILobbyCodeService` | `LobbyCodeService` | Lobby code generation and release |
| `IRandomNumberService` | `RandomNumberService` | Fast and secure random number generation |
| *(concrete)* | `DiceSimulatorGameEngine` | Dice simulator game logic |
| *(concrete)* | `CardCounterGameEngine` | Card counter game logic |

### RegisterStateServices() — Mixed

| Interface | Implementation | Lifetime | Purpose |
|---|---|---|---|
| `ILobbyService` | `LobbyService` | Singleton | Lobby registry |
| `IIDBackedServiceProvider` | `IDBackedServiceProvider` | Singleton | ID-keyed persistent service cache |
| `IUserService` | `UserService` | Scoped | Per-circuit user identity |
| `IGameSessionService` | `GameSessionService` | Scoped | Per-circuit session proxy; delegates state to `GameSessionState` |
| *(concrete)* | `GameSessionState` | Transient | User-id-backed session state; cached by `IIDBackedServiceProvider` |
| `CircuitHandler` | `IDBackedCircuitHandler` | Scoped | Notifies `IIDBackedServiceProvider` of circuit lifecycle |

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
| `IDbContextFactory<ApplicationDbContext>` | EF Core + Npgsql | Factory | Database context creation |

**Lifetime rules:**
- Lobby state (`LobbyService`, `IDBackedServiceProvider`, game engines, lobby code service) is **Singleton** — all users share the same active lobby registrations, engine instances, and ID-backed service cache.
- Per-circuit concerns (`UserService`, `GameSessionService`, `IDBackedCircuitHandler`, `NavigationService`, client storage) are **Scoped** (one instance per Blazor circuit / browser connection).
- Per-user session state (`GameSessionState`) is **Transient** in the DI container but cached as a single instance per user id by `IIDBackedServiceProvider`, surviving circuit breaks.
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

## Constraints & Trade-offs

**Single server only.** All state is in-memory within one process. This is appropriate for a party game platform with moderate concurrent usage. Scaling to multiple servers would require replacing the in-memory `ConcurrentDictionary` with a distributed store and reintroducing external pub/sub.

**No persistence during gameplay.** If the server restarts, all active lobbies are lost. This is acceptable for short-lived party game sessions. The PostgreSQL database and repository layer exist as infrastructure scaffolding for future persistent features (game history, leaderboards, user accounts).

**Thread safety is per-state.** The `ConcurrentDictionary` protects lobby creation and lookup. Each `AbstractGameState` instance owns a `SemaphoreSlim` lock, and all mutations go through `Execute`/`ExecuteAsync`. The subscriber list is independently thread-safe via the copy-on-write array in `ThreadSafeEventManager`.

**Notification after lock release.** Subscriber notification happens after the lock is released. This means a listener that reads the state could theoretically see a subsequent mutation's result if another `Execute` call completes between lock release and notification. In practice this is acceptable for UI rendering — the component renders the latest state. The benefit is that listener callbacks cannot deadlock the state, and lock hold times are minimized.

**Fixed host.** The host is the player who created the lobby and is set at creation time. There is no host transfer if the host disconnects.

**5-minute disconnect grace period.** When a circuit drops, the player is not immediately removed. `IIDBackedServiceProvider` starts a 5-minute timer before disposing the user's cached services (including `GameSessionState`). If the user reconnects within that window their session is preserved seamlessly.

**JS interop for client storage and file export.** Browser `localStorage` is used for persisting the user's display name across sessions, and a JS module handles CSV file downloads for the Dice Simulator.

**Route convention is runtime-enforced.** The `NavigationString` attribute on the `GameType` enum must match the route segment used in the game's Razor page `@page` directive. A mismatch will result in a 404 at navigation time.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core / .NET 10 |
| Frontend | Blazor Server (Interactive Server render mode, prerender disabled) |
| Real-time updates | `IDisposable` event subscriptions via `ThreadSafeEventManager` |
| State storage | In-memory (`ConcurrentDictionary`, per-state `SemaphoreSlim` locking) |
| Scheduled transitions | `ScheduleCallback` on `AbstractGameState` (returns `CancellationTokenSource`) |
| Game plugin system | .NET dependency injection (`AbstractGameEngine` subclasses) |
| Game UI | Game-owned Razor pages at `/room/{game-type}/{obfuscated-code}` |
| Database | PostgreSQL via EF Core (Npgsql) |
| Logging | Serilog (structured, console sink) |
| Validation | FluentValidation |
| Deployment | Docker (docker-compose) |
| Language | C# 13 / .NET 10 |
