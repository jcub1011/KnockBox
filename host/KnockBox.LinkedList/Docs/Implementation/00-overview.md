# Linked List — Implementation Overview & Shared Context

> Read this first. Every milestone file (`01`–`05`) assumes the conventions, APIs, and file map below. Each milestone also restates the pieces it needs, so you can execute a single milestone without re-deriving everything — but this file is the canonical reference.

## What we're building

A Jackbox-style party word game (`host/KnockBox.LinkedList/Docs/linked-list-gdd.md`). Players build a chain of word pairs from a **start word** to a **destination word**; each new pair must begin with the last word of the previous pair (the **carried word**). A rotating **human Auditor** approves or rejects each submission and supplies a short reason on reject. Two scoring modes (Fewest Guesses, Fastest Time) and two player structures (Collective co-op, Groups competitive).

**v1 scope is human-Auditor only.** No automated judge, no word graph, no generated puzzles, no daily puzzle, no persistent profiles — those are GDD §11 future work and stay out.

## Current state of the plugin (scaffold only)

```
host/KnockBox.LinkedList/
  KnockBox.LinkedList.csproj        # Razor Class Library, refs KnockBox.Core, imports Directory.Plugin.targets
  LinkedListModule.cs               # IGameModule → AddGameEngine<LinkedListGameEngine>()
  plugin.json                       # routeIdentifier "linked-list"; description is a TODO placeholder
  _Imports.razor
  Pages/
    LinkedListLobby.razor           # placeholder: players list + Start button, "Game UI TODO"
    LinkedListLobby.razor.cs        # StartGame() → GameEngine.StartAsync(...)
    LinkedListLobby.razor.css
  Services/
    Logic/Games/LinkedListGameEngine.cs   # CreateStateAsync + StartAsyncCore stubs (just toggles joinable)
    State/Games/LinkedListGameState.cs     # empty subclass of AbstractGameState
  wwwroot/tile.svg
host/KnockBox.LinkedListTests/
  Unit/Logic/Games/LinkedList/LinkedListGameEngineTests.cs   # one happy-path test
  Unit/State/Games/LinkedList/LinkedListGameStateTests.cs
```

The project is already referenced transitively from `host/KnockBox/KnockBox.csproj` (`ReferenceOutputAssembly="false" Private="false"`) and listed in `host/KnockBox.Host.slnx`. No host-wiring work is needed.

## Plugin architecture invariants (do not break)

- The plugin loads into its own `PluginLoadContext` (ALC) from `games/KnockBox.LinkedList/`. The host has **no compile-time reference** to plugin types. Never add a `using` of a host type, and never reference a game-project type from the host.
- `plugin.json` `routeIdentifier` (`linked-list`) **must equal** the route segment in every `@page` directive (`/room/linked-list/{ObfuscatedRoomCode}`). Mismatch = 404 at navigation.
- `wwwroot/` is mounted at `/_content/KnockBox.LinkedList/...`. The scoped-CSS bundle is referenced as `_content/KnockBox.LinkedList/KnockBox.LinkedList.styles.css` (already in `LinkedListLobby.razor`).
- The plugin references **only** `KnockBox.Core` (`..\..\sdk\KnockBox.Core\KnockBox.Core.csproj`). Do not call `System.IO`, `HttpClient`, `Process`, or `Environment` — the KB1001–KB1004 analyzers flag these. Embedded resources (for the curated word list) are fine.
- **All per-room state mutation must flow through `AbstractGameState.Execute` / `ExecuteAsync`.** State-changed notification fires exactly once **after** the lock releases. Never raise notifications inside a mutation, and never call another state's `Execute` from within an `Execute`.

## SDK API cheat-sheet (verified file paths)

### `AbstractGameState` — `sdk/KnockBox.Core/Services/State/Games/Shared/AbstractGameState.cs`
- Mutate: `Result Execute(Action)`, `ValueResult<T> Execute<T>(Func<T>)`, `ValueTask<Result> ExecuteAsync(Func<ValueTask>, CancellationToken)`.
- Read (no notify): `Result WithExclusiveRead(Action)`, `WithExclusiveReadAsync`.
- Roster: `ImmutableArray<PlayerEntry> Players`, `Participants` (respects `HostIsParticipant`), `RosterIncludingHost`. `PlayerEntry` is `readonly record struct PlayerEntry(User User, string DisplayName, IDisposable? Token)`.
- Membership: `ValueResult<IDisposable> RegisterPlayer(User)`, `Result KickPlayer(User caller, User player)`, `User Host`, `bool IsJoinable`.
- Lobby gates (call inside `Execute`): `SetJoinable(bool)`, `SetHostIsParticipant(bool)`, `ThrowIfNotExecuting()`.
- Events: `StateChangedEventManager.Subscribe(Func<ValueTask>)` → `IDisposable`; `IDisposable SubscribePlayerUnregistered(Action<User>)` (fires **outside** the lock, so handlers may call `Execute`); `SubscribeStateDisposed(Action)`.
- Timers: `ValueResult<IScheduledCallbackHandle> ScheduleCallback(TimeSpan delay, Func<Task> action)` — runs `action` via `ExecuteAsync` after `delay`; auto-cancelled on `Dispose`. Handle has `Cancel()`/`IsCancelled`.

### `AbstractGameEngine` — `sdk/KnockBox.Core/Services/Logic/Games/Engines/Shared/AbstractGameEngine.cs`
- Ctor `AbstractGameEngine(int minPlayerCount, int maxPlayerCount)` (default `(0,0)`); exposes `MinPlayerCount`/`MaxPlayerCount`.
- Override `Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken)` and `Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken)`.
- `StartAsync(User caller, AbstractGameState, …)` (base) validates `caller == host` then calls `StartAsyncCore`. Don't override.
- `virtual Task<bool> CanStartAsync(...)` defaults to `HasValidPlayerCount(state) && IsLobbyOpen(state)`; override to add rules. Helpers `HasValidPlayerCount`, `IsLobbyOpen`.

### `TurnManager` — `sdk/KnockBox.Core/Services/State/Games/Shared/Components/TurnManager.cs`
`List<string> TurnOrder`, `int CurrentPlayerIndex`, `string? CurrentPlayer`, `void SetTurnOrder(IEnumerable<string>)`, `bool NextTurn()` (returns true when it wraps to index 0), `void SetCurrentPlayerIndex(int)`. Not thread-safe — call only inside `Execute`. Use for **player** rotation; drive **Auditor** rotation with a separate stored id/index (see Milestone 4).

### Result types — `sdk/KnockBox.Core/Primitives/Returns/`
`Result` (`Success`, `FromError(string)`, `FromError(public, internal)`, `FromCancellation`, `IsSuccess`/`IsFailure`/`IsCanceled`, `TryGetFailure(out ResultError)`). `ValueResult<T>` adds `FromValue`, `TryGetSuccess(out T)`, implicit conversions. `ValueResult<T,TError>` for custom error payloads. `ResultError` has `PublicMessage` (UI) + `InternalMessage` (logs).

### Components — `sdk/KnockBox.Core/Components/Shared/`
- `LobbyPageBase<TGameState>` (`LobbyPageBase.cs`): injects `IGameSessionService`, `INavigationService`, `IUserService`, `ITickService`, `IWakeLockService`, `ILoggerFactory`. Provides `[Parameter] ObfuscatedRoomCode`, `TGameState GameState`, `RoomCode`, `Logger`, `IsHost()`, `ReturnToHome()`. Validates session/URL and wires state subscriptions automatically. Override hooks: `OnLobbyInitializedAsync()`, `OnLobbyDisposing()`, `OnStateChangedAsync()` (default calls `StateHasChanged`), `bool TryGetHostTick(out Action, out int tickInterval)` (host-only per-tick callback).
- `DisposableComponent` (`DisposableComponent.cs`): `CancellationToken ComponentDetached`, `ScheduleClear(TimeSpan, Action)` for animated dismissals.
- `CountdownClock` (shared component): give it `EndTimeUtc` + `Duration`, render via `Context="countdown"` exposing `countdown.RemainingSeconds`/`Fraction`. See `host/KnockBox.Codeword/Pages/CodewordLobby.razor` lines 34–57 for the canonical timer-bar usage.
- `ITickService`: `RegisterTickCallback(Action, int tickInterval=1)`, `TicksPerSecond`, `TickInterval`.

### `IUserService` / `User` — `sdk/KnockBox.Core/Services/State/Users/IUserService.cs`
`User { string Id; string Name; }` (name ≤ 12 chars). `UserService.CurrentUser` (scoped per circuit). Tests build users with `UserFactory.Create(name, id)`.

### Plugin registration — `LinkedListModule.cs` (already correct)
`registration.AddGameEngine<LinkedListGameEngine>()` registers the engine as a singleton **and** as a keyed `AbstractGameEngine` under the manifest `routeIdentifier`. Takes no arguments. Optional `RenderFragment? GetCustomHeader()` override exists (Codeword/Operator use it) — out of scope unless a milestone calls for it.

## Reference games — read these while implementing

| Game | Path | What to copy |
|---|---|---|
| **Codeword** (primary) | `host/KnockBox.Codeword/` | Phase enum + `SetPhase` (no inline notify) in `Services/State/Games/CodewordGameState.cs`; immutable `CodewordSettings` record + atomic `UpdateSettings(Func<…>)`; per-phase `.razor` sub-components dispatched by `@switch (GameState.Phase)` in `Pages/CodewordLobby.razor`; settings-drawer property setters in `Pages/LobbyPhase.razor.cs`; `CountdownClock` timer bar; scoreboard in `Pages/GameOverPhase.razor`; keystroke input without `Execute` in `Pages/CluePhase.razor.cs`. |
| **Operator** | `host/KnockBox.Operator/` | Command-style player actions (model for Approve/Reject); host spectator view (`Pages/SpectatorView.razor`); action-log/chain display; per-player struct. |
| **CardCounter** | `host/KnockBox.CardCounter/` | Per-player state dictionary; overlay/pending-action UI that blocks other input; `OnParametersSet()` clearing transient component state when phase/turn changes. |

## Cross-cutting data model (introduced in M1, extended later)

```csharp
// LinkedListGameState.cs
public enum LinkedListGamePhase { Setup, Playing, RoundOver, GameOver }

public sealed record ChainLink(string FromWord, string ToWord, string PlayerId, string PlayerName, bool IsLoop);
public sealed record RejectionInfo(string PlayerId, string AttemptedWord, string Reason);
public sealed record Submission(string PlayerId, string ProposedWord);  // first word is the carried word

public sealed class LinkedListPlayerState
{
    public required string PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public int AcceptedPairs { get; set; }     // for "fewest guesses" + superlatives
    public int RejectionsReceived { get; set; }
    // group id / time accrual added in later milestones
}
```

`LinkedListSettings` (own file `LinkedListSettings.cs`) is an immutable `record` with `init` props, replaced atomically via `state.UpdateSettings(s => s with { … })` (mirrors `CodewordGameState.UpdateSettings`). Full shape is in Milestone 1.

## Testing conventions

- MSTest + Moq (`host/KnockBox.LinkedListTests/`). `[TestClass]`/`[TestInitialize]`/`[TestMethod]`. `MSTestSettings.cs` sets method-level parallelism.
- Build users with `UserFactory.Create("Host", "host1")`. Mock loggers with `new Mock<ILogger<T>>().Object`.
- Construct state with `using var state = new LinkedListGameState(host, loggerMock.Object);`. Mutate through `state.Execute(() => …)`. Register players with `state.RegisterPlayer(user)`.
- Assert on `Result`/`ValueResult<T>` via `.IsSuccess`/`.IsFailure`/`.Value`/`TryGetFailure`.
- Test files live under `Unit/Logic/Games/LinkedList/` (engine) and `Unit/State/Games/LinkedList/` (state). Each milestone adds tests for its new rules.

## Verification (applies to every milestone)

- `dotnet build host/KnockBox.Host.slnx` (transitively builds + stages the plugin into `games/`).
- `dotnet test host/KnockBox.LinkedListTests/KnockBox.LinkedListTests.csproj`.
- Manual: `dotnet run --project host/KnockBox/KnockBox.csproj`; the **Linked List** tile should appear on the home page; create a room, open extra tabs as players against `room/linked-list/{code}`, and exercise the milestone's loop. No 404 confirms manifest/route alignment.

## Milestone roadmap

1. **Foundation** — data model, settings, curated word source, engine start wiring, configurable lobby setup screen.
2. **Core loop** — playable §4 loop: Collective + Fewest Guesses + human Auditor (submit → audit → chain → destination), rejection cap, loop detection, role-conditioned Playing UI with chain-as-links visual.
3. **Scoring & timers** — Fewest Guesses + Fastest Time clock (pauses during audit, rejections cost time), host par, round/scoreboard screen.
4. **Auditor & match flow** — Auditor rotation, persona dial, reason presets, emoji reactions, full match lifecycle + results superlatives.
5. **Groups (competitive)** — independent chains per group, staggered/batch auditing, standings, cross-metric tie-breaking.
