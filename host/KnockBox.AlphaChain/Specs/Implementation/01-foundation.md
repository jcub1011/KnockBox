# Milestone 1 — Foundation

## Goal

Stand up the FSM skeleton, state shape, config record, and turn loop for Alpha Chain. No word logic, no cards, no scoring — just rounds rotating through players and the game ending after the configured era count. This milestone defines the data shapes that every later milestone extends, so the focus is forward-compatibility, not feature completeness.

## Demonstrable outcome

- A host starts the game from the lobby.
- The in-game page shows the current player, current round number (1..N), and current era (1..N).
- A debug "next turn" button issues an `AdvanceTurnCommand` that rotates the active player and increments the round counter.
- After `EraCount × EraInterval` rounds elapse, the game transitions to `GameOver` and shows a placeholder results screen.
- Disconnecting a player skips their turn (mirroring `KnockBox.Codeword`'s `HandlePlayerLeft`).

## New / changed files

### Domain types

- `Services/Logic/Games/Data/AlphaChainGamePhase.cs` — `enum AlphaChainGamePhase { Setup, Round, Intermission, GameOver }`.
- `Services/Logic/Games/Data/AlphaChainSettings.cs` — a **`public sealed record`** with init-only
  properties, following the established settings pattern (`KnockBox.Spardle/SpardleSettings.cs`,
  `KnockBox.Operator/Models/OperatorSettings.cs`). Enum properties are decorated with
  `[JsonConverter(typeof(JsonStringEnumConverter))]` so they persist by name (the record is serialized
  to host `localStorage` in M5). Defaults:
  - `[JsonConverter(typeof(JsonStringEnumConverter))] BanLetterMode BanMode = BanLetterMode.All` (enum: `Vowels`, `Consonants`, `All`)
  - `int ShotClockSeconds = 12`
  - `int IntermissionCardSelectSeconds = 30`
  - `int SniperBanSeconds = 15`
  - `int EraInterval = 4` (rounds per era)
  - `int EraCount = 4` (total eras before game over)
  - `bool SurvivalMode = false`
  - `int ModifiersDealtPerEra = 3` (cards dealt to each player at Intermission — consumed in M4)
  - `int ActionsDealtPerEra = 2` (action cards dealt at Intermission — consumed in M4)
  - `bool HostPlays = false` — **start-time-only** choice set by the lobby's two start buttons (host as
    shared display vs. host as player). Lives on the record but is **never persisted** to `localStorage`;
    mirrors `OperatorSettings.HostPlays`. Drives `AbstractGameState.SetHostIsParticipant` at start.
  - These last three fields are defined here (the single source of truth) even though they are first
    *used* in M4 (`ModifiersDealtPerEra`/`ActionsDealtPerEra`) and at lobby start (`HostPlays`).
- `Services/State/Games/Data/AlphaChainPlayerState.cs` — plain mutable class (not a record yet — it grows):
  - `string UserId`
  - `string DisplayName`
  - `int Score = 0`
  - `bool IsEliminated = false`
  - `bool HasLeft = false`
  - Reserved hand collections (added in M3): keep public surface minimal here, but document intent in XML doc.

### FSM scaffolding

**Reuse the existing FSM abstraction** — do not invent a bespoke state interface. Mirror Codeword
exactly: states implement `IGameState<AlphaChainGameContext, AlphaChainCommand>`
(`OnEnter`/`OnExit`/`HandleCommand`/`Tick`, each returning `ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?>`
to signal a transition), and the context holds an
`IFiniteStateMachine<AlphaChainGameContext, AlphaChainCommand>` (see `KnockBox.Codeword`'s
`CodewordGameContext.Fsm` + its `SetupState : ITimedCodewordGameState` for the timed variant).

- `Services/Logic/Games/FSM/AlphaChainGameContext.cs` — context object shared across FSM states (mirror `CodewordGameContext`):
  - `AlphaChainGameState State`
  - `AlphaChainGameEngine Engine`
  - `ILogger Logger`
  - `IFiniteStateMachine<AlphaChainGameContext, AlphaChainCommand> Fsm` (settable; the FSM drives current-state dispatch).
- `Services/Logic/Games/FSM/AlphaChainCommand.cs` — abstract base record `AlphaChainCommand(string ActorUserId)`.
  - Initial concrete command: `AdvanceTurnCommand(string ActorUserId) : AlphaChainCommand`.
- `Services/Logic/Games/FSM/States/SetupState.cs` — `IGameState<AlphaChainGameContext, AlphaChainCommand>`. `OnEnter`: initialize the `GamePlayers` dictionary from `state.Participants` (so the host is included when `HostPlays`), set `CurrentEra = 1`, `CurrentRound = 1`, return a transition to `RoundState`.
- `Services/Logic/Games/FSM/States/RoundState.cs` —
  - `OnEnter`: `state.SetPhase(AlphaChainGamePhase.Round)`, set `PhaseEndTime = now + ShotClockSeconds` (timer is real but no consequence until M2).
  - `HandleCommand(AdvanceTurnCommand)`: validate `ActorUserId == TurnManager.CurrentPlayer`, advance via `TurnManager.NextTurn()`, increment `CurrentRound` once the turn order wraps. Apply the single canonical end condition (see "Era/round rule" below) to decide whether to return a transition to `GameOverState`.
- `Services/Logic/Games/FSM/States/GameOverState.cs` — terminal; populate a `GameResults` record on the state for the UI.

**Era/round rule (canonical — referenced by M2 and M4; evaluate it at the turn-order wrap point):**
`CurrentRound` and `CurrentEra` are 1-based and set to `1` in `SetupState`. A round "completes" when the
turn order wraps. At that wrap point, let `completedRound` be the value of `CurrentRound` *before* this
wrap's increment, and define `LastScheduledRound = Config.EraInterval × Config.EraCount`. Decide in this
order:
1. **Game over** iff `completedRound == LastScheduledRound` → transition to `GameOverState` (no
   Intermission ever runs after the final era).
2. **Era boundary** (M4 only) iff `completedRound % Config.EraInterval == 0` → transition to
   `IntermissionState`.
3. **Otherwise** increment `CurrentRound` and continue in `RoundState`.

In M1 there is no Intermission, so only rule 1 applies (rule 2 is a no-op until M4). `CurrentEra` is
advanced only at Intermission completion (M4), so the M1 era indicator stays at `1` for the whole game —
acceptable for the skeleton. Because rule 1 fires on the final scheduled round, M4's separate
`CurrentEra > Config.EraCount` guard inside `IntermissionState` is defensive and unreachable under the
default flow — keep it only as a belt-and-suspenders check.

### Engine and state

- `Services/State/Games/AlphaChainGameState.cs` — flesh out (currently empty stub). Implement:
  - `IPhasedGameState<AlphaChainGamePhase>` → `Phase` getter; mutate via `SetPhase(AlphaChainGamePhase)` (the interface's setter) inside `Execute`.
  - `IPlayerTrackedGameState<AlphaChainPlayerState>` → `ConcurrentDictionary<string, AlphaChainPlayerState> GamePlayers`.
  - `IFsmContextGameState<AlphaChainGameContext>` → `Context` (settable once at start).
  - Settings: `public AlphaChainSettings Settings { get; private set; } = new();` plus
    `public Result UpdateSettings(Func<AlphaChainSettings, AlphaChainSettings> mutate) => Execute(() => { Settings = mutate(Settings); SetHostIsParticipant(Settings.HostPlays); });`
    (mirrors `OperatorGameState.UpdateSettings` / `CodewordGameState.UpdateSettings`).
  - Plus: `TurnManager TurnManager`, `int CurrentRound`, `int CurrentEra`, `DateTimeOffset PhaseEndTime`, `GameResults? Results`.
  - All mutators go through `Execute`/`ExecuteAsync`. Read helpers use `WithExclusiveRead`.
- `Services/Logic/Games/AlphaChainGameEngine.cs` — flesh out:
  - Constructor takes `ILogger<AlphaChainGameEngine>`, `ILogger<AlphaChainGameState>` (for state logging once it's logger-aware).
  - `MinPlayerCount => 2`, `MaxPlayerCount => 8`.
  - `CreateStateAsync(User host, CancellationToken ct)` → returns `ValueResult<AbstractGameState>` containing a new `AlphaChainGameState` (its `Settings` default to `new()`). Subscribe the player-left handler here, mirroring Codeword: `state.SubscribePlayerUnregistered(u => HandlePlayerLeft(u, state));`.
  - `StartAsyncCore(AbstractGameState state, CancellationToken ct)`:
    - Cast to `AlphaChainGameState`.
    - Inside `state.Execute(...)`: call `state.SetHostIsParticipant(state.Settings.HostPlays)`, build `AlphaChainGameContext` (wire its `Fsm`), set `state.Context`, init `TurnManager` turn order from `state.Participants` (so the host is included when `HostPlays` is true — **not** `state.Players`), set `state.SetJoinable(false)`.
    - Start the FSM in `SetupState` (its `OnEnter` immediately transitions to `RoundState`).
  - `ProcessCommandAsync(AlphaChainCommand cmd)` — gateway used by Razor pages; serializes via `state.ExecuteAsync` and delegates to the FSM's current state (`Fsm.HandleCommand`).
  - `Tick(AlphaChainGameContext ctx, DateTimeOffset now)` — delegates to the FSM's current state `Tick` inside `state.Execute`. (No-op in M1 except recording `PhaseEndTime`.)
  - `HandlePlayerLeft(User user, AlphaChainGameState state)` — subscribed via `state.SubscribePlayerUnregistered(...)` in `CreateStateAsync` (fires outside the execute lock). Marks `HasLeft = true`. If it was their turn, advance.

### UI

- `Pages/AlphaChainGame.razor` — new file `@page "/room/alpha-chain/{ObfuscatedRoomCode}/play"` (inherit `LobbyPageBase<AlphaChainGameState>`). Shows current player name, round, era, debug "advance" button (visible to all in M1 for testing).
- `Pages/AlphaChainGame.razor.cs` — inject `AlphaChainGameEngine`, subscribe to `GameState.StateChangedEventManager` in `OnInitializedAsync`, dispose in `Dispose`.
- `Pages/AlphaChainGame.razor.css` — minimal scaffolding (centered layout).
- `Pages/AlphaChainLobby.razor` —
  - **Two start buttons** (mirror `KnockBox.Operator`/`KnockBox.LinkedList`): `StartGame(false)` = host
    runs the **shared display** (host not a player); `StartGame(true)` = **host as player**. Gate the
    first on `GameState.Players.Length >= GameEngine.MinPlayerCount`, the second on
    `GameState.Players.Length + 1 >= GameEngine.MinPlayerCount`. Each handler does
    `GameState.UpdateSettings(s => s with { HostPlays = <bool> })` then `await GameEngine.StartAsync(...)`.
  - When `!GameState.IsJoinable`, render the in-game view. For the host, branch on
    `!GameState.HostIsParticipant` to show a shared-display/spectator view rather than the player UI
    (mirror `OperatorLobby.razor`). Render `<AlphaChainGame />` as a component view rather than the
    placeholder text. (Alternative: redirect to the `/play` sub-route; pick one approach and document it.)

### Tests

- `host/KnockBox.AlphaChainTests/Unit/Logic/Games/AlphaChain/AlphaChainGameEngineTests.cs`:
  - `MinPlayerCount_IsTwo`
  - `MaxPlayerCount_IsEight`
  - `CreateStateAsync_WithNullHost_ReturnsError`
  - `CreateStateAsync_ReturnsDefaultConfig`
  - `StartAsync_ClosesLobbyAndEntersRoundPhase`
  - `StartAsPlayer_IncludesHostInTurnOrder` (HostPlays = true → host appears in `TurnManager.TurnOrder`)
  - `StartAsDisplay_ExcludesHostFromTurnOrder` (HostPlays = false → turn order == `Players`)
  - `AdvanceTurn_RotatesPlayerInTurnOrder`
  - `AdvanceTurn_WrapsAndIncrementsRound`
  - `Game_TransitionsToGameOver_AfterEraCountTimesEraIntervalRounds`
  - `PlayerLeaves_DuringTheirTurn_AdvancesAutomatically`

## Key types & contracts

- `AbstractGameState` mutation contract: every write goes through `Execute`/`ExecuteAsync`; notifications fire **outside** the lock.
- `AbstractGameEngine` is a singleton; per-room data lives only on `AlphaChainGameState`.
- **Host participation:** the turn roster is `state.Participants`, which equals `state.Players` when
  `HostIsParticipant` is false and includes the host when true. `StartAsyncCore` is the single place
  that calls `SetHostIsParticipant(Settings.HostPlays)`; everything downstream (turn order, `GamePlayers`,
  results) is built from `Participants`, never `Players`.
- `RouteIdentifier "alpha-chain"` (plugin.json) must keep matching the `@page` routes — both `/room/alpha-chain/{ObfuscatedRoomCode}` (lobby) and the new `/room/alpha-chain/{ObfuscatedRoomCode}/play` if that path is chosen.

## Step-by-step build order

1. Add the enums and config record.
2. Flesh out `AlphaChainPlayerState`.
3. Implement `AlphaChainGameState` fields and interface members.
4. Add FSM context (with `Fsm`) and command base, then the three states (`SetupState`, `RoundState`, `GameOverState`) implementing the **existing** `IGameState<AlphaChainGameContext, AlphaChainCommand>` — no new state interface is introduced.
5. Implement `AlphaChainGameEngine` (constructor, lifecycle, command dispatch, player-left handler).
6. Build `AlphaChainGame.razor` and integrate from the lobby.
7. Write engine tests; iterate until green.
8. Manual verify: `dotnet run --project host/KnockBox/KnockBox.csproj`, open two browser windows, start a 2-player match, rotate turns through to GameOver.

## Risks & notes

- **Risk:** Picking the wrong shape for `AlphaChainPlayerState` will force rewrites in M3/M4. **Mitigation:** keep it as a plain class (not a record), and write a brief XML doc on each field noting "extended in M-N for cards / elimination".
- **Risk:** Splitting the lobby vs. in-game view between separate Razor pages duplicates session-validation boilerplate. **Mitigation:** prefer a single page that swaps content on `GameState.IsJoinable`; only break it out if the routes diverge meaningfully.
- **Note:** Do not add `IPluginStorage` usage yet — first persistent state lands in M2 with the dictionary file.
