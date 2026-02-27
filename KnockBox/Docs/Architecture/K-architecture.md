# Multi-Game Room Platform — Architecture Overview

## Vision

A Blazor Server web application that hosts multiple browser-based party games under a single platform. Players create or join rooms using short room codes, similar to Jackbox-style party games. The architecture treats room management as shared infrastructure and individual games as pluggable modules, allowing new games to be added with zero changes to the core platform.

---

## System Context

All users connect to a single Blazor Server instance. Each browser tab maintains a persistent WebSocket circuit to the server. Because all circuits share the same process, game state lives entirely in memory with no need for SignalR, external message buses, or database-backed state during gameplay.

```
 ┌──────────┐  ┌──────────┐  ┌──────────┐
 │ Browser  │  │ Browser  │  │ Browser  │
 │ (Tab A)  │  │ (Tab B)  │  │ (Tab C)  │
 └────┬─────┘  └────┬─────┘  └────┬─────┘
      │ WebSocket    │ WebSocket   │ WebSocket
      │ (Circuit)    │ (Circuit)   │ (Circuit)
 ┌────┴──────────────┴─────────────┴──────┐
 │         Blazor Server Process          │
 │                                        │
 │  ┌──────────────────────────────────┐  │
 │  │     RoomManager (Singleton)      │  │
 │  │  ┌────────┐ ┌────────┐ ┌─────┐  │  │
 │  │  │ Room 1 │ │ Room 2 │ │ ... │  │  │
 │  │  └────────┘ └────────┘ └─────┘  │  │
 │  └──────────────────────────────────┘  │
 │                                        │
 │  ┌──────────────────────────────────┐  │
 │  │  Game Engine Registry (DI)       │  │
 │  │  Trivia │ DrawBattle │ WordChain │  │
 │  └──────────────────────────────────┘  │
 └────────────────────────────────────────┘
```

---

## Control Flow

The following diagram shows the lifecycle of a single player action during gameplay. The Razor page calls a method on the game engine (resolved via DI), the engine calls `state.ExecuteAsync` which acquires the lock, runs the mutation, and then notifies all subscribers to re-render after the lock is released.

```
  Player A (Razor Page)          Game Engine              Game State           Player B (Razor Page)
  ─────────────────────          ───────────              ──────────           ─────────────────────
          │                           │                        │                        │
          │  SubmitAnswerAsync(       │                        │                        │
          │    state, playerId,       │                        │                        │
          │    answer)                │                        │                        │
          │ ─────────────────────────>│                        │                        │
          │                           │                        │                        │
          │                           │  state.ExecuteAsync()  │                        │
          │                           │ ──────────────────────>│                        │
          │                           │                        │                        │
          │                           │               ┌────────────────────┐             │
          │                           │               │  Acquire lock      │             │
          │                           │               │  Run mutation      │             │
          │                           │               │  Release lock      │             │
          │                           │               │  NotifyChanged()   │             │
          │                           │               └────────────────────┘             │
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

Key points: the `RoomManager` is not involved during gameplay. The Razor page injects the game engine via DI and holds a reference to the game state from joining. `ExecuteAsync` acquires the lock, runs the mutation, releases the lock, and *then* notifies subscribers — keeping the lock held for the minimum duration and preventing reentrant deadlocks from listener callbacks. Listeners are invoked with error isolation so a failing subscriber cannot break notification for others.

---

## Core Components

### RoomManager

A singleton service that acts as a room registry. Responsibilities include creating rooms with unique 8-character alphanumeric case-insensitive codes, validating join requests, adding players to rooms, providing the game state reference on join, and exposing rooms for cleanup. The `RoomManager` is not involved in gameplay — once a player has joined and received the game state, all gameplay flows directly between the Razor page, the game engine, and the state. Thread safety for room creation and lookup is handled via `ConcurrentDictionary`.

Room code generation is outside the scope of this document and is covered in a separate architecture document.

### Room

A lightweight container representing a single game session. Holds the player list, the game ID (used for routing), a reference to the `AbstractGameState`, the host's user ID, and a last-activity timestamp for cleanup. The room does not track a status enum — the game's phase is owned by the state, and the cleanup service uses the last-activity timestamp to determine whether a room should be reaped. When a player joins, the room provides them with the game state reference. The game engine is not stored on the room; Razor pages resolve their concrete engine directly from DI.

The room tracks host identity. The host is the player who created the room. If the host disconnects, host status transfers to the next player in join order. Authorization checks (e.g., only the host can start the game) should be enforced at the engine level via the state, not solely in Razor pages.

### UserRegistration

Players are identified by a `UserRegistration` record containing `UserName` and `UserId`. The `UserId` is a unique identifier scoped to the Blazor circuit and is used for all authorization checks, action routing, and player tracking. `UserName` is the player's chosen display name, used for rendering in game UI.

```csharp
public record UserRegistration(string UserName, string UserId);
```

### AbstractGameEngine

An abstract base class that every game extends. Defines metadata (ID, display name, player limits), a factory method for creating the game's concrete state, and the `StartAsync` lifecycle method. Each concrete engine exposes **game-specific methods** (e.g., `SubmitAnswerAsync`, `RollDiceAsync`, `CastVoteAsync`) that the game's Razor pages call directly. These methods receive a reference to the game state, mutate it in place via `state.ExecuteAsync`, and any action-specific parameters.

Game engines are **singletons** registered in DI. They hold no per-room state — all mutable data lives on the `AbstractGameState`. Razor pages inject their concrete engine directly (e.g., `@inject TriviaGameEngine Engine`) rather than receiving it from the room, keeping the engine resolution simple and testable.

```csharp
public abstract class AbstractGameEngine
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract int MinPlayers { get; }
    public abstract int MaxPlayers { get; }

    /// <summary>
    /// Factory method that creates the concrete game state for this game.
    /// Called by RoomManager during room creation, keeping the RoomManager
    /// decoupled from concrete game state types.
    /// </summary>
    public abstract AbstractGameState CreateState();

    public abstract Task StartAsync(AbstractGameState state);
}

public class TriviaGameEngine : AbstractGameEngine
{
    public override string Id => "trivia";
    public override string DisplayName => "Trivia";
    public override int MinPlayers => 2;
    public override int MaxPlayers => 8;

    public override AbstractGameState CreateState() => new TriviaGameState();

    public override async Task StartAsync(AbstractGameState state)
    {
        // Mutate the existing state to set up the first round:
        // load questions, initialize scores for all tracked players,
        // set phase to gameplay, etc.
    }

    public async Task SubmitAnswerAsync(TriviaGameState state, string playerId, string answer)
    {
        await state.ExecuteAsync(async () =>
        {
            state.PlayerAnswers[playerId] = answer;

            if (state.PlayerAnswers.Count == state.Players.Count)
            {
                ScoreRound(state);
                await AdvanceQuestionAsync(state);
            }
        });
    }
}
```

`AbstractGameEngine` is purely server-side, concerned only with game logic and state transitions. It has no knowledge of UI components. All rendering decisions are owned by each game's Razor pages.

### AbstractGameState

An abstract base class that defines the minimal contract shared across all games — a phase identifier, the player list, common metadata, a concurrency lock, a built-in event dispatcher, and a scheduled callback mechanism. Each game implements its own concrete subclass with strongly-typed properties for that game's specific state.

The game state instance is created when the room is created via the engine's `CreateState()` factory method, and the same instance is used from lobby through gameplay. Players join and subscribe to this state immediately. When the host starts the game, `StartAsync` mutates the existing state in place — there is no state replacement or re-subscription required.

All state mutations go through `ExecuteAsync`, which acquires a per-state `SemaphoreSlim`, executes the mutation, releases the lock, and then notifies all subscribers. Notification happens *after* the lock is released to keep lock duration minimal and to prevent reentrant deadlocks if a listener callback triggers another `ExecuteAsync` call. Each listener is invoked with error isolation so that a failing subscriber does not prevent others from being notified. `NotifyStateChanged` is private; the only way to trigger it is through `ExecuteAsync`.

Subscriptions return an `IDisposable`. Razor components store the subscription and dispose it when the circuit disconnects, preventing dead callbacks from accumulating. This disposable subscription pattern is the standard for all event subscriptions across the application. The subscriber list is thread-safe to support concurrent subscribe/unsubscribe operations from different circuits.

#### Scheduled Callbacks

The state exposes a `ScheduleCallback` method that allows game engines to schedule delayed state transitions (e.g., a 30-second answer timer, countdown to next round, auto-advance when time expires). `ScheduleCallback` accepts a `TimeSpan` delay and a `Func<Task>` action, and returns a `CancellationTokenSource` that the caller can use to cancel the scheduled callback before it fires. Internally, the scheduled action is executed via `ExecuteAsync` when the delay elapses, ensuring it follows the same locking and notification semantics as any player-driven mutation. The callback checks the cancellation token before executing; if the token has been cancelled, the callback is silently discarded.

Game engines must cancel outstanding callbacks when the game phase changes or the scheduled event is no longer relevant. The returned `CancellationTokenSource` makes this explicit — engines store the reference and call `Cancel()` when the phase advances, a player action supersedes the timer, or any other condition invalidates the scheduled work. Scheduled callbacks are also automatically cancelled when the room is cleaned up.

As a defensive measure, scheduled callback actions should include a guard clause verifying the expected game phase or round is still current before performing mutations. This protects against edge cases where cancellation and execution race — the lock serializes access, but the callback may win the race and execute against a state that has already transitioned.

```csharp
public abstract class AbstractGameState
{
    public string Phase { get; set; } = "lobby";
    public List<UserRegistration> Players { get; } = new();

    // Lock, subscription, notification, and scheduled callback infrastructure.
    // ExecuteAsync acquires the lock, runs the mutation, releases the lock,
    // then notifies subscribers with per-listener error isolation.
    // ScheduleCallback schedules a delayed action that runs through ExecuteAsync.
    //   Returns a CancellationTokenSource for explicit cancellation by the caller.
    // Subscribe returns an IDisposable for lifecycle management.
}

public class TriviaGameState : AbstractGameState
{
    public int CurrentQuestionIndex { get; set; }
    public TriviaQuestion CurrentQuestion { get; set; } = default!;
    public Dictionary<string, string> PlayerAnswers { get; } = new();
    public Dictionary<string, int> Scores { get; } = new();
}
```

The `Room` holds state as `AbstractGameState`. Game pages hold references to the concrete subclass, keeping all internal logic type-safe. Because state is held in memory as live objects and mutated in place, there is no serialization or allocation overhead during gameplay.

Game engine methods that need to perform expensive async work (e.g., fetching questions from a file or external API) should do so **before** calling `ExecuteAsync`, and only place the actual state mutation inside the lambda. This keeps the lock held for the minimum duration.

### Player Identity

Each browser tab is associated with a `UserRegistration` scoped to the Blazor circuit. This can be managed via a circuit-scoped service or cascading parameter. The `UserId` is used to enforce authorization (e.g., only the host can start the game) and to route player actions. The game state's `Players` list is the authoritative record of who is in the game.

---

## Routing & Game-Owned Pages

Each game owns its own routable Razor pages under the pattern `/room/{game-id}/{code}`. The core platform does not render game UI directly — it only resolves the correct route and navigates the player there.

This means each game controls its full user experience: the lobby layout, gameplay phases, transitions, and any game-specific sub-flows (e.g., team selection, round recaps). The platform imposes no UI constraints on games.

### Convention

The route segment `{game-id}` must match the `Id` property returned by the game's `AbstractGameEngine` subclass. For example, if `TriviaGameEngine.Id` returns `"trivia"`, the game's page must declare `@page "/room/trivia/{Code}"`. This is a runtime convention — a mismatch between the engine ID and the route will result in a navigation failure, so game authors should verify this during development.

Games are free to use a single page that handles all phases internally, or split into multiple routable pages if the game flow warrants it.

---

## Join-to-Start Flow

The following diagram shows the complete lifecycle from a player joining an existing room through the host starting the game. The key takeaway is that the game state is a single long-lived instance — players subscribe to it on join and are already connected when the host starts the game. No state replacement or re-subscription occurs.

```
  Player (Browser)            Join Page             RoomManager              Room / State           Host (Browser)
  ────────────────            ─────────             ───────────              ────────────           ──────────────
        │                         │                      │                        │                       │
        │  Enter room code        │                      │                        │                       │
        │ ───────────────────────>│                      │                        │                       │
        │                         │                      │                        │                       │
        │                         │  JoinRoom(code,      │                        │                       │
        │                         │    registration)     │                        │                       │
        │                         │ ────────────────────>│                        │                       │
        │                         │                      │                        │                       │
        │                         │                      │  Validate code         │                       │
        │                         │                      │  Check capacity        │                       │
        │                         │                      │  Reject if game        │                       │
        │                         │                      │    already started     │                       │
        │                         │                      │  Add to Players list   │                       │
        │                         │                      │ ──────────────────────>│                       │
        │                         │                      │                        │                       │
        │                         │  Return: game ID,    │                        │                       │
        │                         │    code, state ref   │                        │                       │
        │                         │ <────────────────────│                        │                       │
        │                         │                      │                        │                       │
        │  Navigate to            │                      │                        │                       │
        │  /room/{game-id}/{code} │                      │                        │                       │
        │ <───────────────────────│                      │                        │                       │
        │                         │                      │                        │                       │
        │                                                                         │                       │
        │  Game Razor Page loads                                                  │                       │
        │  Inject engine from DI                                                  │                       │
        │  Subscribe to state ───────────────────────────────────────────────────>│                       │
        │  (store IDisposable)                                                    │                       │
        │                                                                         │                       │
        │                                                    NotifyChanged()      │                       │
        │  callback: re-render (show in lobby) <──────────────────────────────────│──────────────────────>│
        │                                                                         │   callback: re-render │
        │                                                                         │   (updated player list│
        │                                                                         │                       │
        │                                                                         │   Host clicks Start   │
        │                                                                         │ <─────────────────────│
        │                                                                         │                       │
        │                                                                         │   engine.StartAsync() │
        │                                                              ┌──────────────────────────┐       │
        │                                                              │  ExecuteAsync:            │       │
        │                                                              │    Acquire lock           │       │
        │                                                              │    Load game data         │       │
        │                                                              │    Init per-player state  │       │
        │                                                              │    Set phase = "playing"  │       │
        │                                                              │    Release lock           │       │
        │                                                              │    NotifyChanged()        │       │
        │                                                              └──────────────────────────┘       │
        │                                                                         │                       │
        │  callback: re-render (phase = "playing", show game UI) <────────────────│──────────────────────>│
        │                                                                         │   callback: re-render │
        │                                                                         │   (show game UI)      │
```

---

## Key Flows

### Create Room

1. Player selects a game from the game picker page.
2. `RoomManager.CreateRoom` resolves the selected game's `AbstractGameEngine` from DI, calls `engine.CreateState()` to obtain the concrete game state, generates a unique 8-character room code, instantiates a `Room` (recording the game ID and the created state), and registers the player as host.
3. The player's `UserRegistration` is added to the state's `Players` list.
4. Player is navigated to `/room/{game-id}/{code}`, where the game's own page renders its lobby view.

### Join Room

1. Player enters a room code on the join page.
2. `RoomManager.JoinRoom` validates the code, checks that the game's phase is still `"lobby"` (rejecting the join if the game has already started), checks capacity against the engine's `MaxPlayers`, and adds the player's `UserRegistration` to the state's `Players` list.
3. The room returns the game ID, room code, and a reference to the game state.
4. The join page navigates the player to `/room/{game-id}/{code}`.
5. The game's Razor page resolves the concrete engine from DI and subscribes to the state's event dispatcher, storing the returned `IDisposable`.
6. The state notifies all subscribers, updating all components to show the new player.

Players cannot join a room once the game has started. The `RoomManager` enforces this by checking the state's phase during `JoinRoom`. This is a platform-level invariant — individual games do not need to implement their own join-gating logic. If a game needs to support late joining (e.g., a spectator mode), this would require an explicit opt-in mechanism on the engine, but the default behavior is to reject joins after the lobby phase.

### Start Game

1. Host clicks start in the game's lobby view.
2. The game engine's `StartAsync` is called, which mutates the existing `AbstractGameState` in place — loading initial game data, initializing per-player state for all tracked players, and setting the phase to begin gameplay.
3. The state notifies all subscribers, causing all circuits to re-render. The game's page reads the state's `Phase` to determine what to display (e.g., swapping from a lobby section to a gameplay section).

### Gameplay

1. A player performs an action (e.g., submits an answer in a trivia game).
2. The game's Razor page calls a method on the injected game engine (e.g., `engine.SubmitAnswerAsync(state, playerId, answer)`).
3. The engine method calls `state.ExecuteAsync(async () => { ... })`, which acquires the lock, runs the mutation (records the answer, updates scores, advances the phase if needed), releases the lock, and then notifies all subscribers.
4. All subscribed Razor components re-render with the updated state.
5. If the game is over, the engine sets the state's `Phase` accordingly inside the `ExecuteAsync` lambda and the game page renders the results view.

### Player Disconnect

1. A player's browser tab closes or their circuit disconnects.
2. The Razor component disposes its state subscription (via the stored `IDisposable`).
3. The room detects the disconnection and removes the player's `UserRegistration` from the state's `Players` list.
4. If the disconnected player was the host, host status transfers to the next player in join order.
5. If no players remain, the room is eligible for immediate cleanup.
6. Game engines that need to handle mid-game disconnects gracefully (e.g., skipping a disconnected player's turn, redistributing cards, or ending the game early) should implement an `OnPlayerDisconnectedAsync` method. The platform invokes this when a player leaves during an active game, allowing each game to define its own disconnect behavior.

---

## Adding a New Game

Adding a game requires exactly four steps:

1. **Subclass `AbstractGameState`** — define a concrete state class with the strongly-typed properties your game needs (scores, current turn, question list, etc.). The player list, lock, subscription, notification, and scheduled callback infrastructure is inherited.
2. **Subclass `AbstractGameEngine`** — define game metadata, implement `CreateState()` to return the concrete state instance, implement `StartAsync` (which mutates the existing state to begin gameplay), and add game-specific action methods (e.g., `SubmitAnswerAsync`, `RollDiceAsync`). Each method calls `state.ExecuteAsync` with its mutation logic — locking and notification are handled automatically. Use `state.ScheduleCallback` for time-based transitions, storing the returned `CancellationTokenSource` to cancel when the phase advances.
3. **Create Razor page(s)** — add one or more pages with `@page "/room/{game-id}/{Code}"` that inject the concrete engine via DI, subscribe to the state (storing the `IDisposable`), and dispose the subscription on circuit disconnect.
4. **Register in DI** — add one line: `builder.Services.AddSingleton<TriviaGameEngine>();`

No changes to the room manager, join page, routing infrastructure, or any other game's code.

---

## Lifecycle & Cleanup

A `BackgroundService` runs on a timer (e.g., every 5 minutes) and removes rooms whose last-activity timestamp exceeds a threshold (e.g., 30 minutes). The last-activity timestamp is updated whenever a player joins or an `ExecuteAsync` call completes. Rooms also clean up when all players disconnect, detected via the circuit disconnect lifecycle — when a Razor component disposes its state subscription, the room can check whether any subscribers remain. Room cleanup cancels any outstanding scheduled callbacks on the state (via the `CancellationTokenSource` references held by the state's callback infrastructure).

---

## Constraints & Trade-offs

**Single server only.** All state is in-memory within one process. This is appropriate for a party game platform with moderate concurrent usage. Scaling to multiple servers would require replacing the in-memory `ConcurrentDictionary` with a distributed store (e.g., Redis) and reintroducing SignalR for cross-server pub/sub.

**No persistence during gameplay.** If the server restarts, all active rooms are lost. This is acceptable for short-lived party game sessions. Persistence (e.g., to a database) can be added later for features like game history, leaderboards, or session recovery.

**Thread safety is per-state.** The `ConcurrentDictionary` protects room creation and lookup. Each `AbstractGameState` instance owns a `SemaphoreSlim` lock, and all mutations go through `ExecuteAsync`, which acquires the lock, runs the mutation, releases the lock, and then notifies subscribers. Game engine methods cannot bypass this — `NotifyStateChanged` is private and only called within `ExecuteAsync`. The subscriber list is independently thread-safe for concurrent subscribe/unsubscribe operations. Because each room has its own state instance, contention is minimal; actions in one room never block another.

**Notification after lock release.** Subscriber notification happens after the lock is released. This means a listener that reads the state could theoretically see a subsequent mutation's result if another `ExecuteAsync` call completes between lock release and notification. In practice, this is acceptable for UI rendering — the component will simply render the latest state. The benefit is that listener callbacks cannot deadlock the state, and lock hold times are minimized.

**Join requires a redirect.** When a player enters a room code, the platform must look up the room to determine the game ID before navigating to the game's page. This is a lightweight lookup against the in-memory `ConcurrentDictionary` and adds negligible overhead compared to the previous approach of navigating directly to a shared room page.

**Route convention is runtime-enforced.** The game engine's `Id` must match the route segment used in the game's Razor page `@page` directive. A mismatch will result in a 404 at navigation time. This is a trade-off for keeping the plugin system simple — compile-time enforcement would require source generators or a registration-time validation step, which can be added later if needed.

**No late joining by default.** The platform rejects join attempts once the game has left the lobby phase. This simplifies game engine implementation — engines can assume the player list is fixed from `StartAsync` onward. Games that want to support late joining or spectators would need an explicit opt-in mechanism.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor Server (Razor components, no JS required) |
| Real-time updates | `IDisposable` event subscriptions on `AbstractGameState` |
| State storage | In-memory (`ConcurrentDictionary`) |
| Scheduled transitions | `ScheduleCallback` on `AbstractGameState` (returns `CancellationTokenSource`) |
| Game plugin system | .NET dependency injection (`AbstractGameEngine` subclasses) |
| Game UI | Game-owned Razor pages at `/room/{game-id}/{code}` |
| Background tasks | `IHostedService` for room cleanup |
| Language | C# / .NET 8+ |