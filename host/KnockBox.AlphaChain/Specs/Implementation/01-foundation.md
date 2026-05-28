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
- `Services/Logic/Games/Data/AlphaChainSettings.cs` — record with defaults:
  - `BanLetterMode BanMode = BanLetterMode.All` (enum: `Vowels`, `Consonants`, `All`)
  - `int ShotClockSeconds = 12`
  - `int IntermissionCardSelectSeconds = 30`
  - `int SniperBanSeconds = 15`
  - `int EraInterval = 4` (rounds per era)
  - `int EraCount = 4` (total eras before game over)
  - `bool SurvivalMode = false`
- `Services/State/Games/Data/AlphaChainPlayerState.cs` — plain mutable class (not a record yet — it grows):
  - `string UserId`
  - `string DisplayName`
  - `int Score = 0`
  - `bool IsEliminated = false`
  - `bool HasLeft = false`
  - Reserved hand collections (added in M3): keep public surface minimal here, but document intent in XML doc.

### FSM scaffolding

- `Services/Logic/Games/FSM/AlphaChainGameContext.cs` — context object passed between FSM states (mirror `CodewordGameContext`):
  - `AlphaChainGameState State`
  - `AlphaChainGameEngine Engine`
  - `ILogger Logger`
- `Services/Logic/Games/FSM/AlphaChainCommand.cs` — abstract base record.
  - Initial concrete command: `AdvanceTurnCommand(string ActorUserId)`.
- `Services/Logic/Games/FSM/IAlphaChainFsmState.cs` — `Task EnterAsync(AlphaChainGameContext ctx)`, `Task<bool> HandleCommandAsync(AlphaChainGameContext ctx, AlphaChainCommand cmd)`, `Task TickAsync(AlphaChainGameContext ctx, DateTimeOffset now)`.
- `Services/Logic/Games/FSM/States/SetupState.cs` — initialize `GamePlayers` dictionary from `state.Players`, set `CurrentEra = 1`, `CurrentRound = 1`, transition to `RoundState`.
- `Services/Logic/Games/FSM/States/RoundState.cs` —
  - `EnterAsync`: set `Phase = Round`, set `PhaseEndTime = now + ShotClockSeconds` (timer is real but no consequence until M2).
  - `HandleCommandAsync(AdvanceTurnCommand)`: validate `ActorUserId == CurrentPlayer`, advance `TurnManager.CurrentPlayerIndex`, increment `CurrentRound` once we wrap the turn order. If `CurrentRound > EraInterval × EraCount`, transition to `GameOverState`.
- `Services/Logic/Games/FSM/States/GameOverState.cs` — terminal; populate a `GameResults` record on the state for the UI.

### Engine and state

- `Services/State/Games/AlphaChainGameState.cs` — flesh out (currently empty stub). Implement:
  - `IPhasedGameState<AlphaChainGamePhase>` → `Phase` property + change notification through `Execute`.
  - `IPlayerTrackedGameState<AlphaChainPlayerState>` → `ConcurrentDictionary<string, AlphaChainPlayerState> GamePlayers`.
  - `IFsmContextGameState<AlphaChainGameContext>` → `Context` (settable once at start).
  - Plus: `TurnManager TurnManager`, `int CurrentRound`, `int CurrentEra`, `DateTimeOffset PhaseEndTime`, `GameResults? Results`.
  - All mutators go through `Execute`/`ExecuteAsync`. Read helpers use `WithExclusiveRead`.
- `Services/Logic/Games/AlphaChainGameEngine.cs` — flesh out:
  - Constructor takes `ILogger<AlphaChainGameEngine>`, `ILogger<AlphaChainGameState>` (for state logging once it's logger-aware).
  - `MinPlayerCount => 2`, `MaxPlayerCount => 8`.
  - `CreateStateAsync(User host, CancellationToken ct)` → returns `ValueResult<AbstractGameState>` containing a new `AlphaChainGameState` with default `AlphaChainSettings`.
  - `StartAsyncCore(AbstractGameState state, CancellationToken ct)`:
    - Cast to `AlphaChainGameState`.
    - Inside `state.Execute(...)`: build `AlphaChainGameContext`, set `state.Context`, init `TurnManager` from `state.Players`, set `state.SetJoinable(false)`.
    - Outside the lock: invoke `SetupState.EnterAsync` (which immediately transitions to `RoundState`).
  - `ProcessCommandAsync(AlphaChainCommand cmd)` — gateway used by Razor pages; serializes via `state.ExecuteAsync` and delegates to the current FSM state.
  - `Tick(AlphaChainGameContext ctx, DateTimeOffset now)` — delegates to current FSM state's `TickAsync` inside `state.Execute`. (No-op in M1 except recording `PhaseEndTime`.)
  - `HandlePlayerLeft(User user, AlphaChainGameState state)` — subscribed in `StartAsyncCore` to `state.PlayerUnregistered` (fires outside the lock). Marks `HasLeft = true`. If it was their turn, advance.

### UI

- `Pages/AlphaChainGame.razor` — new file `@page "/room/alpha-chain/{ObfuscatedRoomCode}/play"` (inherit `LobbyPageBase<AlphaChainGameState>`). Shows current player name, round, era, debug "advance" button (visible to all in M1 for testing).
- `Pages/AlphaChainGame.razor.cs` — inject `AlphaChainGameEngine`, subscribe to `GameState.StateChangedEventManager` in `OnInitializedAsync`, dispose in `Dispose`.
- `Pages/AlphaChainGame.razor.css` — minimal scaffolding (centered layout).
- `Pages/AlphaChainLobby.razor` — when `!GameState.IsJoinable`, render an `<AlphaChainGame />` component view rather than the placeholder text. (Alternative: redirect to the `/play` sub-route; pick one approach and document it.)

### Tests

- `host/KnockBox.AlphaChainTests/Unit/Logic/Games/AlphaChain/AlphaChainGameEngineTests.cs`:
  - `MinPlayerCount_IsTwo`
  - `MaxPlayerCount_IsEight`
  - `CreateStateAsync_WithNullHost_ReturnsError`
  - `CreateStateAsync_ReturnsDefaultConfig`
  - `StartAsync_ClosesLobbyAndEntersRoundPhase`
  - `AdvanceTurn_RotatesPlayerInTurnOrder`
  - `AdvanceTurn_WrapsAndIncrementsRound`
  - `Game_TransitionsToGameOver_AfterEraCountTimesEraIntervalRounds`
  - `PlayerLeaves_DuringTheirTurn_AdvancesAutomatically`

## Key types & contracts

- `AbstractGameState` mutation contract: every write goes through `Execute`/`ExecuteAsync`; notifications fire **outside** the lock.
- `AbstractGameEngine` is a singleton; per-room data lives only on `AlphaChainGameState`.
- `RouteIdentifier "alpha-chain"` (plugin.json) must keep matching the `@page` routes — both `/room/alpha-chain/{ObfuscatedRoomCode}` (lobby) and the new `/room/alpha-chain/{ObfuscatedRoomCode}/play` if that path is chosen.

## Step-by-step build order

1. Add the enums and config record.
2. Flesh out `AlphaChainPlayerState`.
3. Implement `AlphaChainGameState` fields and interface members.
4. Add FSM context, command base, state interface, and the three states (`SetupState`, `RoundState`, `GameOverState`).
5. Implement `AlphaChainGameEngine` (constructor, lifecycle, command dispatch, player-left handler).
6. Build `AlphaChainGame.razor` and integrate from the lobby.
7. Write engine tests; iterate until green.
8. Manual verify: `dotnet run --project host/KnockBox/KnockBox.csproj`, open two browser windows, start a 2-player match, rotate turns through to GameOver.

## Risks & notes

- **Risk:** Picking the wrong shape for `AlphaChainPlayerState` will force rewrites in M3/M4. **Mitigation:** keep it as a plain class (not a record), and write a brief XML doc on each field noting "extended in M-N for cards / elimination".
- **Risk:** Splitting the lobby vs. in-game view between separate Razor pages duplicates session-validation boilerplate. **Mitigation:** prefer a single page that swaps content on `GameState.IsJoinable`; only break it out if the routes diverge meaningfully.
- **Note:** Do not add `IPluginStorage` usage yet — first persistent state lands in M2 with the dictionary file.
