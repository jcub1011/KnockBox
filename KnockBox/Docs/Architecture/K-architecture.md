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

## Core Components

### RoomManager

A singleton service that owns all active rooms. Responsibilities include creating rooms with unique 4-character codes, validating join requests, delegating game start and player actions to the appropriate game engine, and exposing rooms for cleanup. Thread safety is handled via `ConcurrentDictionary` for room lookup and per-room locking for mutations to player lists and game state.

### Room

Represents a single game session. Holds the player list, current game state, and room status (Lobby, InProgress, Finished). Each room also stores the game ID identifying which game engine governs the session. The room exposes a subscribe/notify mechanism: connected Blazor components subscribe to a room and receive a callback when state changes, which they use to trigger `InvokeAsync(StateHasChanged)`. This replaces the role SignalR groups would play in a distributed architecture.

### IGameEngine

The plugin interface every game implements. Defines metadata (ID, display name, player limits) and the core game loop methods: `InitializeAsync`, `HandleActionAsync`, and `IsGameOver`. Game engines are stateless services — all mutable state lives in the `GameState` object on the room.

`IGameEngine` is a purely server-side interface concerned only with game logic and state transitions. It has no knowledge of UI components. All rendering decisions are owned by each game's Razor pages.

### GameState

A game-agnostic container holding a phase identifier and a data dictionary. Each game engine defines its own internal structure within this container. Because state is held in memory as live objects, there is no serialization overhead during gameplay.

### Player Identity

Each browser tab is associated with a player identity scoped to the Blazor circuit. This can be managed via a circuit-scoped service or cascading parameter. The player ID is used to enforce authorization (e.g., only the host can start the game) and to route player actions.

---

## Routing & Game-Owned Pages

Each game owns its own routable Razor pages under the pattern `/room/{game-id}/{code}`. The core platform does not render game UI directly — it only resolves the correct route and navigates the player there.

This means each game controls its full user experience: the lobby layout, gameplay phases, transitions, and any game-specific sub-flows (e.g., team selection, round recaps). The platform imposes no UI constraints on games.

### Convention

The route segment `{game-id}` must match the `Id` property returned by the game's `IGameEngine` implementation. For example, if `TriviaGameEngine.Id` returns `"trivia"`, the game's page must declare `@page "/room/trivia/{Code}"`. This is a runtime convention — a mismatch between the engine ID and the route will result in a navigation failure, so game authors should verify this during development.

Games are free to use a single page that handles all phases internally, or split into multiple routable pages if the game flow warrants it.

---

## Key Flows

### Create Room

1. Player selects a game from the game picker page.
2. `RoomManager.CreateRoom` generates a unique room code, instantiates a `Room` (recording the game ID), and registers the player as host.
3. Player is navigated to `/room/{game-id}/{code}`, where the game's own page renders its lobby view.

### Join Room

1. Player enters a room code on the join page.
2. `RoomManager.JoinRoom` validates the code, checks room status and capacity, adds the player, and returns the game ID and room code.
3. The join page navigates the player to `/room/{game-id}/{code}`.
4. `Room.NotifyStateChanged` fires, updating all connected components to show the new player.

### Start Game

1. Host clicks start in the game's lobby view.
2. `RoomManager.StartGameAsync` delegates to the game engine's `InitializeAsync`, which returns the initial `GameState`.
3. Room status transitions to `InProgress`.
4. `Room.NotifyStateChanged` fires, causing all circuits to re-render. The game's page handles the phase transition internally (e.g., swapping from a lobby section to a gameplay section).

### Gameplay

1. A player performs an action (e.g., submits an answer).
2. The game's page calls `RoomManager.HandleActionAsync` with a `PlayerAction`.
3. The room manager delegates to the game engine's `HandleActionAsync`, which returns an updated `GameState`.
4. `Room.NotifyStateChanged` fires, pushing the new state to all players.
5. If `IsGameOver` returns true, room status transitions to `Finished`.

---

## Adding a New Game

Adding a game requires exactly three steps:

1. **Implement `IGameEngine`** — define game metadata and implement the initialize/action/game-over logic. The engine is purely a state machine with no UI references.
2. **Create Razor page(s)** — add one or more pages with `@page "/room/{game-id}/{Code}"` that handle the lobby, gameplay, and results phases.
3. **Register in DI** — add one line: `builder.Services.AddSingleton<IGameEngine, MyNewGameEngine>();`

No changes to the room manager, join page, routing infrastructure, or any other game's code.

---

## Lifecycle & Cleanup

A `BackgroundService` runs on a timer (e.g., every 5 minutes) and removes rooms that have been inactive beyond a threshold (e.g., 30 minutes since last activity). Rooms also clean up when all players disconnect, detected via the circuit disconnect lifecycle.

---

## Constraints & Trade-offs

**Single server only.** All state is in-memory within one process. This is appropriate for a party game platform with moderate concurrent usage. Scaling to multiple servers would require replacing the in-memory `ConcurrentDictionary` with a distributed store (e.g., Redis) and reintroducing SignalR for cross-server pub/sub.

**No persistence during gameplay.** If the server restarts, all active rooms are lost. This is acceptable for short-lived party game sessions. Persistence (e.g., to a database) can be added later for features like game history, leaderboards, or session recovery.

**Thread safety is per-room.** The `ConcurrentDictionary` protects room creation and lookup. Mutations within a room (player joins, game actions) are protected by a per-room lock. This keeps contention minimal since actions in one room never block another.

**Join requires a redirect.** When a player enters a room code, the platform must look up the room to determine the game ID before navigating to the game's page. This is a lightweight lookup against the in-memory `ConcurrentDictionary` and adds negligible overhead compared to the previous approach of navigating directly to a shared room page.

**Route convention is runtime-enforced.** The game engine's `Id` must match the route segment used in the game's Razor page `@page` directive. A mismatch will result in a 404 at navigation time. This is a trade-off for keeping the plugin system simple — compile-time enforcement would require source generators or a registration-time validation step, which can be added later if needed.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor Server (Razor components, no JS required) |
| Real-time updates | In-process subscribe/notify pattern on Room objects |
| State storage | In-memory (`ConcurrentDictionary`) |
| Game plugin system | .NET dependency injection (`IGameEngine` implementations) |
| Game UI | Game-owned Razor pages at `/room/{game-id}/{code}` |
| Background tasks | `IHostedService` for room cleanup |
| Language | C# / .NET 8+ |
