# Milestone 01 — Foundation: Settings, State & Phases

*Implements GDD §3 (core loop skeleton) and §8 (settings). Depends on: nothing. Unblocks: 02.*

---

## Goal

Stand up a clickable end-to-end skeleton — **Lobby → RoundIntro → Playing → Reveal → RoundOver → FinalStandings** — with placeholder content in the play/reveal phases. Later milestones fill real logic into a flow that already transitions, persists settings, and tracks players. After this milestone a host can create a lobby, adjust settings (which survive a refresh), start the game, watch it auto-advance through the phases on timers, and reach a final-standings screen.

## Scope

**In:** phase enum; `TracerySettings` record with all GDD §8 defaults plus tunable scoring/generation constants; `TraceryGameState` fleshed out (phase, expiry, round counter, settings, player dictionary, frozen roster, round results); engine start + placeholder phase transitions; phase-switching root page; host-only settings UI with localStorage persistence.

**Out:** real grid, solver, generation, tracing input, scoring math, reveal content (all placeholders here).

## Files to create or modify

- **Create** `Models/GamePhase.cs` — `enum GamePhase { Lobby, RoundIntro, Playing, Reveal, RoundOver, FinalStandings }`.
- **Create** `Models/TracerySettings.cs` — `sealed record` (see below).
- **Create** `Models/TraceryPlayerState.cs` — `RoundScore`, `CumulativeScore`, `LastRoundPoints`, banked-words collection (placeholder this milestone), `ResetRound()`.
- **Create** `Models/RoundResult.cs` — immutable per-round outcome record (round number, per-player breakdown — minimal fields now, extended in 06).
- **Modify** `Services/State/Games/TraceryGameState.cs` — add phase/settings/player/roster members (see below).
- **Modify** `Services/Logic/Games/TraceryGameEngine.cs` — real `StartAsyncCore`; placeholder phase-transition helpers via `ScheduleCallback`.
- **Create** `Pages/TraceryRoom.razor` (+ `.razor.cs`) at `@page "/room/tracery/{ObfuscatedRoomCode}"`, switching rendered view on `Phase`. **Delete/replace** the placeholder `Pages/TraceryLobby.razor(.cs)` (fold its lobby card into a `LobbyView`).
- **Create** lobby sub-views/components as needed (`Components/TracerySettingsPanel.razor` host-only).
- **Modify** `Unit/Logic/Games/TraceryGameEngineTests.cs` — extend.

## Key types & methods

**`TracerySettings`** (defaults from GDD §8 + tunable constants from §5/§6):

```
int GridWidth = 4, GridHeight = 4;
TimeSpan RoundTimer = TimeSpan.FromSeconds(90);   // TimeSpan.Zero = unlimited
int TotalRounds = 3;
int MinWordLength = 4;
bool UniqueFindBonusEnabled = true;
double UniqueFindMultiplier = 1.5;
bool RareLetterBonusEnabled = true;
TimeSpan TransitionDuration = TimeSpan.FromSeconds(5); // intro/results pacing
// Generation quality bar (Milestone 03) — kept here so it is tunable:
int MinFindableWords; int MinLongWordLength = 7; bool RequireRareLetterWord = true; int MaxGenerationAttempts;
// Scoring tables (Milestone 06) — exposed for playtest tuning per GDD §10.
```
Enums serialized by name (`[JsonStringEnumConverter]`) so persisted snapshots survive reordering (HiddenAgenda precedent).

**`TraceryGameState`** additions (mirror `SpardleState`): `GamePhase Phase`, `DateTimeOffset? PhaseExpiresAtUtc`, `int CurrentRound`, `TracerySettings Settings { get; private set; }` + `Result UpdateSettings(Func<…,…>)`, player dictionary (`ConcurrentDictionary<string, TraceryPlayerState>`) with `CreatePlayerState`/`TryGetPlayerState`, frozen `Participants` (ImmutableArray, set at start), `RoundResults` (ImmutableList). All mutators written only inside `Execute`.

**`TraceryGameEngine.StartAsyncCore`** — mirror `SpardleEngine.StartAsyncCore`: `SetJoinable(false)`, decide host-participates, freeze `Participants`, reset `CurrentRound`/`RoundResults`, init per-player states, call `EnterRoundIntro`. Placeholder `EnterRoundIntro → EnterPlaying → CompleteRound → (Reveal/RoundOver) → AdvanceAfterResults → next round or FinalStandings`, each setting `Phase`/`PhaseExpiresAtUtc` and using `ScheduleCallback` for auto-advance (no real grid/scoring yet).

## Reuse references

- `SpardleSettings.cs` (record shape), `SpardleState.cs` (state members + `UpdateSettings`), `SpardleEngine.cs` lines ~42–78, ~254–386 (start + phase helpers).
- `HiddenAgenda/Pages/LobbyPhase.razor.cs` — `LoadSettingsAsync`/`SaveSettingsAsync`/`_userHasEdited` localStorage pattern.
- `SpardleRoom.razor` — `Phase` switch + host/player view split.

## Acceptance criteria

- Host can change every setting; values persist across a page refresh (localStorage) and never clobber an in-flight host edit.
- Starting the game closes the lobby, freezes the roster, and auto-advances through all phases on timers to `FinalStandings`.
- Player and host see role-appropriate placeholder views per phase.
- Build green; plugin still stages into `games/Tracery/`.

## Tests

- `TracerySettings` defaults match GDD §8; `UpdateSettings` returns a new record and routes through `Execute` (state unchanged on failure).
- `StartAsync` rejects non-host; flips `IsJoinable` off; sets `Phase` past `Lobby`.
- Placeholder transition: advancing past the last round lands on `FinalStandings`.
