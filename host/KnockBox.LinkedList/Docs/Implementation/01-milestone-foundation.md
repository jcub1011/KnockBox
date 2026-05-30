# Milestone 1 — Foundation

## Goal

Establish the full data model, host-configurable settings, a curated start/destination word source, engine start wiring, and a configurable **lobby setup screen**. After this milestone the project builds, the **Linked List** tile shows on the home page, and the host can configure a match and press Start to reach the `Playing` phase. No gameplay actions exist yet (that is Milestone 2).

## Prerequisites

- None beyond the existing scaffold. Read `00-overview.md` first.

## Architecture context (restated)

- All state mutation goes through `state.Execute(...)`; notification fires once after the lock releases. Never notify inside a mutation.
- Settings live in an immutable `record` replaced atomically via an `UpdateSettings(Func<…>)` wrapper that runs inside `Execute` — copy the pattern from `host/KnockBox.Codeword/Services/State/Games/CodewordGameState.cs` (`UpdateSettings`, lines ~111–116) and `host/KnockBox.Codeword/CodewordSettings.cs`.
- The lobby page already inherits `LobbyPageBase<LinkedListGameState>`. Sub-views render as child `.razor` components dispatched by a `@switch (GameState.Phase)` in `LinkedListLobby.razor` (mirror `host/KnockBox.Codeword/Pages/CodewordLobby.razor`, lines 68–98).
- Engine player bounds come from the `AbstractGameEngine(min, max)` ctor; gate Start with `CanStartAsync`.
- The plugin may use **embedded resources** for the curated word list — do not touch `System.IO` for app data (KB100x analyzers).

## Files to create / modify

**Modify**
- `Services/State/Games/LinkedListGameState.cs` — full state model (below).
- `Services/Logic/Games/LinkedListGameEngine.cs` — player bounds, start wiring.
- `Pages/LinkedListLobby.razor` — `@switch` dispatch + settings/lobby host UI (or delegate to `LobbyPhase`).
- `Pages/LinkedListLobby.razor.cs` — keep `StartGame()`; add settings setter helpers if not split into `LobbyPhase`.
- `plugin.json` — real `description`.

**Create**
- `LinkedListSettings.cs` (project root) — settings record + enums.
- `Services/Logic/WordPairSource.cs` — loads curated start/destination pairs from the embedded resource; supplies a random pick.
- `wwwroot` is not needed for the word list; embed instead: `Data/start-destination-pairs.json` marked `<EmbeddedResource>` in the csproj.
- `Pages/LobbyPhase.razor` (+ `.razor.cs`) — host settings drawer + players list + Start button (optional split; may instead live inline in `LinkedListLobby.razor`). Splitting is recommended to match Codeword.

## Implementation detail

### 1. Settings (`LinkedListSettings.cs`)

```csharp
namespace KnockBox.LinkedList;

public enum ScoringMode { FewestGuesses, FastestTime }
public enum PlayerStructure { Collective, Groups }

public sealed record LinkedListSettings
{
    public ScoringMode ScoringMode { get; init; } = ScoringMode.FewestGuesses;
    public PlayerStructure PlayerStructure { get; init; } = PlayerStructure.Collective;

    /// <summary>Rejected attempts allowed per turn before forfeit. 0 = off (unlimited).</summary>
    public int RejectionCap { get; init; } = 3;

    /// <summary>Optional §7.4 rigor: block a pair identical to the immediately previous pair.</summary>
    public bool NoImmediateRepeat { get; init; } = false;

    public bool HostPlaysGame { get; init; } = false;

    /// <summary>Collective co-op target the host sets by hand (§8.1). Null = no par.</summary>
    public int? Par { get; init; } = null;

    // Timer durations used in Milestone 3 (defined now so the record is stable).
    public TimeSpan PerTurnClock { get; init; } = TimeSpan.FromSeconds(60);
    public bool EnableTimers { get; init; } = true;
}
```

### 2. State (`LinkedListGameState.cs`)

Add to the existing subclass. Keep the constructor signature `(User host, ILogger<LinkedListGameState> logger)`.

```csharp
public LinkedListGamePhase Phase { get; private set; } = LinkedListGamePhase.Setup;
public void SetPhase(LinkedListGamePhase phase) => Phase = phase;   // notify happens via Execute, not here

public TurnManager TurnManager { get; } = new();
public ConcurrentDictionary<string, LinkedListPlayerState> GamePlayers { get; } = new();

// Round data (single shared chain for Collective; Groups extends this in M5)
public string StartWord { get; set; } = "";
public string DestinationWord { get; set; } = "";
public string CarriedWord { get; set; } = "";
public readonly List<ChainLink> Chain = [];
public readonly List<RejectionInfo> RejectionLog = [];
public int RejectionsThisTurn { get; set; }
public bool DestinationReached { get; set; }

// Auditor (rotation logic lands in M4; M1 just assigns the first one)
public string AuditorPlayerId { get; set; } = "";

public LinkedListSettings Settings { get; private set; } = new();
public Result UpdateSettings(Func<LinkedListSettings, LinkedListSettings> mutate) =>
    Execute(() =>
    {
        Settings = mutate(Settings);
        SetHostIsParticipant(Settings.HostPlaysGame);
    });
```

Enums/records (`LinkedListGamePhase`, `ChainLink`, `RejectionInfo`, `Submission`, `LinkedListPlayerState`) per `00-overview.md`. Add `using System.Collections.Concurrent;` and the `TurnManager` namespace `KnockBox.Core.Services.State.Games.Shared.Components`.

### 3. Curated word source (§8.4)

- Add `Data/start-destination-pairs.json` (a few dozen entries), e.g. `[{ "start": "DOG", "destination": "WORK" }, ...]`. Pick pairs with a hand-tuned distance so rounds aren't trivial (GDD §12 Q3).
- Mark it embedded in `KnockBox.LinkedList.csproj`:
  ```xml
  <ItemGroup>
    <EmbeddedResource Include="Data\start-destination-pairs.json" />
  </ItemGroup>
  ```
- `Services/Logic/WordPairSource.cs`: a small class that reads the embedded resource via `Assembly.GetManifestResourceStream`, deserializes with `System.Text.Json`, and exposes `(string start, string dest) Random(IRandomNumberService rng)` plus the raw list (for a lobby picker). Inject `IRandomNumberService` (used elsewhere in the codebase — see HiddenAgenda engine tests) rather than `Random` directly, so it's testable and deterministic. Register as a plugin singleton in `LinkedListModule.RegisterServices` via `registration.AddSingleton<WordPairSource>(ctx => new WordPairSource())` if it needs no deps, or inject into the engine directly.

### 4. Engine (`LinkedListGameEngine.cs`)

- Add a player-bounds ctor: `: base(minPlayerCount: 3, maxPlayerCount: 10)` (GDD §1: 3–10). Keep the injected loggers; add `WordPairSource` (and `IRandomNumberService` if used) as ctor params.
- `CreateStateAsync`: unchanged behavior — create state, `Execute(() => SetJoinable(true))`, return it. (Auditor/turn-order assignment happens at start, not create.)
- `StartAsyncCore`: cast to `LinkedListGameState`; in a single `Execute`:
  - Build the participant id list from `state.Participants` and populate `GamePlayers` (PlayerId + DisplayName).
  - `TurnManager.SetTurnOrder(participantIds)`.
  - If `StartWord`/`DestinationWord` weren't set by the host in the lobby, pick from `WordPairSource`. Set `CarriedWord = StartWord`, `DestinationReached = false`, clear `Chain`/`RejectionLog`, `RejectionsThisTurn = 0`.
  - Assign the first Auditor: `AuditorPlayerId` = the host-chosen id, or default to the first participant who is **not** the current submitter (M2 enforces the active-player-≠-Auditor rule; M1 just records the choice).
  - `SetJoinable(false)`, `SetPhase(LinkedListGamePhase.Playing)`.
  - Return `Result.Success`; surface failures with `Result.FromError(public, internal)`.
- Optionally override `CanStartAsync` to also require that a start/destination is chosen and an Auditor is assigned.

### 5. Lobby UI

- `LinkedListLobby.razor`: keep the joinable branch (players list + Start). Replace the "Game UI TODO" branch with `@switch (GameState.Phase)` dispatching to phase components (only `Setup`/lobby relevant in M1; later milestones add `Playing`/`RoundOver`/`GameOver`).
- Host settings drawer (in `LobbyPhase.razor` or inline): bind each setting through `UpdateSettings`. Mirror `host/KnockBox.Codeword/Pages/LobbyPhase.razor.cs` property-setter style:
  ```csharp
  protected ScoringMode Mode
  {
      get => GameState.Settings.ScoringMode;
      set => GameState.UpdateSettings(s => s with { ScoringMode = value });
  }
  ```
  Controls: scoring mode, player structure, rejection cap (incl. "off"), no-immediate-repeat toggle, host-plays toggle, optional par (Collective), and start/destination — either pick a curated pair or type custom values (write them onto `state` via `Execute`).
- Assign-first-Auditor control: a dropdown of participants; store the chosen id (write to a field the engine reads at start, or directly set `AuditorPlayerId` via `Execute`).
- Gate the Start button on `IsHost()` and `await GameEngine.CanStartAsync(GameState)`.

### 6. `plugin.json`

Replace the placeholder description, e.g. `"Build a chain of word pairs from a start word to a destination — a rotating human Auditor decides what counts."`

## Tests (`host/KnockBox.LinkedListTests/`)

Extend the existing files:
- **State** (`Unit/State/Games/LinkedList/LinkedListGameStateTests.cs`): `UpdateSettings` replaces settings atomically and reflects `HostPlaysGame` into `HostIsParticipant`; `SetPhase` changes `Phase`; default settings values.
- **Engine** (`Unit/Logic/Games/LinkedList/LinkedListGameEngineTests.cs`): `CreateStateAsync` returns a joinable `Setup`-phase state; after registering 3 participants and starting, `Phase == Playing`, `IsJoinable == false`, `TurnManager.TurnOrder` is populated, `CarriedWord == StartWord`, and `AuditorPlayerId` is set; starting with < 3 players fails `CanStartAsync`/start.
- **WordPairSource**: returns a non-empty start/destination pair; deterministic with a stubbed `IRandomNumberService`.

## Verification

- `dotnet build host/KnockBox.Host.slnx` succeeds; `dotnet test host/KnockBox.LinkedListTests/KnockBox.LinkedListTests.csproj` green.
- `dotnet run --project host/KnockBox/KnockBox.csproj`: **Linked List** tile appears; create a room → configure settings → assign Auditor → Start. The page advances out of the joinable lobby into the `Playing` phase placeholder (gameplay UI arrives in M2).

## Done-when checklist

- [ ] `LinkedListSettings` record + `ScoringMode`/`PlayerStructure` enums exist; `UpdateSettings` is atomic.
- [ ] `LinkedListGameState` holds phase, turn manager, player dict, round/chain/rejection fields, auditor id.
- [ ] Curated word list embedded + `WordPairSource` loads and randomly picks a pair.
- [ ] Engine enforces 3–10 players and, on start, sets turn order, words, carried word, first Auditor, phase = `Playing`.
- [ ] Lobby host UI configures every setting and start/destination; Start gated by `CanStartAsync`.
- [ ] `plugin.json` description is real.
- [ ] Tests pass; manual lobby→Playing transition works.
