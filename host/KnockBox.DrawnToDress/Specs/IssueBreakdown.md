# Craft Clothing Clash — Epic 1 Issue Breakdown

All issues below are self-contained. Each includes the exact specifications, default values, behavioral rules, and edge cases needed for implementation. No external documents are required.

---

## #49 — 1.1 Scaffold Drawn To Dress project structure and register it in KnockBox (DONE)

### Summary
Create the initial Drawn To Dress module and integrate it into the existing KnockBox solution using the same architectural conventions as Card Counter.

### Goals
- Add the baseline project structure for a new game module.
- Ensure a Drawn To Dress room can be created through the app.
- Establish the minimum engine/state/context shells needed for later issues.

### In scope
- Create the new game project/module for Drawn To Dress.
- Add all required solution and project references.
- Register required DI services.
- Add game-type registration so Drawn To Dress can be created from the app.
- Add an initial route/page shell in the main Blazor app.
- Add placeholder engine/state/context classes.

### Expected structure
- `KnockBox.DrawnToDress/`
- `Services/Logic/Games/DrawnToDress/`
- `Services/State/Games/DrawnToDress/`
- `KnockBox.DrawnToDressTests/`
- `KnockBox/Components/Pages/Games/DrawnToDress/`

### Deliverables
- `DrawnToDressGameEngine`
- `DrawnToDressGameState`
- `DrawnToDressGameContext`
- Placeholder page route/component
- App registration needed to create a Drawn To Dress game

### Out of scope
- Real gameplay logic
- Full FSM behavior
- Lobby/config UI
- Drawing, pool, voting, or scoring logic

### Implementation notes
- Mirror Card Counter's project organization and naming patterns where reasonable.
- Favor minimal placeholder implementations over speculative gameplay logic.
- The route should render a shell even if the game is not yet playable.

### Acceptance criteria
- Solution compiles successfully.
- Drawn To Dress appears as a selectable/creatable game type.
- A Drawn To Dress room can be created.
- The placeholder page route renders without runtime errors.
- Engine/state/context shells exist and are wired into the app.

### Test cases
- Build/compile verification.
- Smoke test that creating a Drawn To Dress room succeeds.
- Smoke test that the route/page renders.

### Dependencies / sequencing
- No hard dependency within Epic 1; this is a foundation issue for the rest of the epic.

---

## #50 — 1.2 Define Drawn To Dress domain models and configuration schema (DONE)

### Summary
Define the core domain and configuration models needed to support all game concepts. All default values, property names, types, and allowed options are specified below — implement them exactly as listed.

### Goals
- Create stable, testable models for session state, player state, clothing items, outfit submissions, themes, voting, and results.
- Encode default configuration values exactly as specified below.

### In scope — Models to create

#### `DrawnToDressConfig`
The central configuration model. All properties, their types, defaults, and allowed values:

**Drawing Phase Settings:**
| Property | Type | Default | Allowed Values | Description |
|----------|------|---------|----------------|-------------|
| `DrawingTimeSec` | int | 180 | 45, 60, 90, 120, 180 | Seconds per drawing round |
| `MaxItemsPerType` | int | 3 | 3, 5, 8, 10, 0 (unlimited) | Max items a player can draw per clothing type per round |
| `ClothingTypes` | List&lt;ClothingTypeDefinition&gt; | see below | Custom list | Ordered list of clothing types to draw |

Default clothing types (in order):
1. `{ Id: "hat", DisplayName: "Hat" }`
2. `{ Id: "top", DisplayName: "Top" }`
3. `{ Id: "bottom", DisplayName: "Bottom" }`
4. `{ Id: "shoes", DisplayName: "Shoes" }`

**Theme Settings:**
| Property | Type | Default | Allowed Values | Description |
|----------|------|---------|----------------|-------------|
| `ThemeSource` | enum ThemeSource | Random | Random, HostPick, PlayerWritten, RandomVoting | How theme is selected |
| `ThemeAnnouncement` | enum ThemeAnnouncement | BeforeDrawing | BeforeDrawing, AfterDrawing | When theme is revealed |
| `RandomVotingCandidateCount` | int | 3 | 2-5 | Number of theme candidates shown in RandomVoting mode |

Theme source behaviors:
- **Random**: Game randomly selects a theme from a curated built-in pool on entry. No player input needed.
- **HostPick**: Host selects a theme from a list before the game starts.
- **PlayerWritten**: Each player submits one theme at game start. All submitted themes are shown to all players as-is before outfit building begins. No moderation required (game is designed for family/friends).
- **RandomVoting**: Game presents `RandomVotingCandidateCount` random themes from the built-in pool. Players vote on which theme to use.

Both outfits in a session always use the same theme. There is no per-outfit theme variation.

**Pool Settings:**
| Property | Type | Default | Allowed Values | Description |
|----------|------|---------|----------------|-------------|
| `PoolRevealTimeSec` | int | 30 | 10-60 | Auto-advance countdown for pool reveal |

**Outfit Building Settings:**
| Property | Type | Default | Allowed Values | Description |
|----------|------|---------|----------------|-------------|
| `OutfitBuildingTimeSec` | int | 90 | 60, 90, 120, 180, 240, 300, 0 (unlimited) | Seconds for outfit building phase |
| `OutfitCustomizationTimeSec` | int | 60 | 30-300 | Seconds for customization/naming phase |
| `AllowSketching` | bool | true | true, false | Whether sketch overlay is available during customization |
| `SketchingRequired` | bool | false | true, false | If true, sketching becomes a timed required component |
| `CanReuseOutfit1Items` | bool | false | true, false | Whether Outfit 2 can reuse items from Outfit 1. This setting takes precedence over `OutfitDistinctnessThreshold` |
| `OutfitDistinctnessThreshold` | int | 2 | 1, 2, 3 | Minimum number of items that must differ between Outfit 2 and every Outfit 1. Only applies when `CanReuseOutfit1Items` is true. Value meaning: 1=lenient (reuse up to 3 of 4), 2=moderate (must differ by 2+), 3=strict (must differ by 3+) |
| `NumOutfitRounds` | int | 1 | 1, 2, 3, 4 | Number of outfit rounds per game |
| `Outfit2PoolType` | enum | ExactCopy | ExactCopy, FreshDrawingsOnly, Hybrid | How Outfit 2 pool is constructed. ExactCopy = identical to Outfit 1 pool minus used items. FreshDrawingsOnly = only new/unused drawings. Hybrid = mix of both |

**Voting & Scoring Settings:**
| Property | Type | Default | Allowed Values | Description |
|----------|------|---------|----------------|-------------|
| `VotingCriteria` | List&lt;VotingCriterionDefinition&gt; | see below | Custom list | Criteria voters judge on |
| `VotingTimeSec` | int | 60 | 30-300 | Seconds per voting matchup |
| `VotingVisibility` | enum | PercentagesOnly | Hidden, PercentagesOnly, IndividualVotes, LiveVoting | What vote information is shown |
| `VotingScope` | enum | CannotVoteOnOwn | CannotVoteOnOwn, CanVoteOnOwn, HostDecides | Whether players can vote on their own outfits |
| `ShowCreatorDuringVoting` | bool | false | true, false | Whether outfit creator identity is visible during voting |
| `TournamentFormat` | enum | Swiss | Swiss, SingleElimination, Custom | Tournament structure. Swiss=fair competitive. SingleElimination=bracket. Custom=host specifies round count with Swiss pairing |
| `TournamentRounds` | string/int | "auto" | 1-10 or "auto" | Number of voting rounds. "auto" = ceil(log2(numOutfits)). Example: 6 players × 1 outfit = 6 outfits → ceil(log2(6)) = 3 rounds. 6 players × 2 outfits = 12 outfits → ceil(log2(12)) = 4 rounds |
| `BonusPoints` | object | {RoundLeader: 3, TournamentWin: 10} | Host-defined (0 to disable) | RoundLeader: awarded to outfit with most points in a round (ties: all tied outfits get it). TournamentWin: awarded to player with highest cumulative points |

**Note:** Swiss-system is the only supported tournament format. SingleElimination and Custom are not implemented.

Default voting criteria:
1. `{ Id: "creativity", DisplayName: "Creativity", Weight: 1.0 }` — How unique/innovative the outfit is
2. `{ Id: "theme_match", DisplayName: "Theme Match", Weight: 1.0 }` — How well outfit fits the theme
3. `{ Id: "overall_look", DisplayName: "Overall Look", Weight: 1.0 }` — Overall aesthetic appeal and cohesion

Additional available criteria (not in defaults):
- `{ Id: "skillExecution", DisplayName: "Skill Execution", Weight: 1.0 }` — Quality of drawing/design

**Game Flow Settings:**
| Property | Type | Default | Allowed Values | Description |
|----------|------|---------|----------------|-------------|
| `MinPlayers` | int | 3 | 3-20+ | Minimum players to start. 6+ recommended for best voting experience |
| `HostRole` | enum | Active | Active, Passive | Active=host announces phases, picks themes, moderates. Passive=host just tracks, players self-manage |
| `HostDisconnectTimeoutSec` | int | 60 | 30, 60, 120, 300 | Seconds to wait for host reconnect before abandoning game |

**Note:** The host is always a non-participant observer (except theme picking in HostPick mode). The Active/Passive distinction is not implemented.

**Advanced Settings:**
| Property | Type | Default | Allowed Values | Description |
|----------|------|---------|----------------|-------------|
| `AllowPlayersToChooseThemes` | bool | false | true, false | If true, players vote on themes before drawing |
| `RandomizePairings` | bool | true | true, false | If false, matchups are deterministic (replayable) |

**Note:** Votes are always changeable until voting ends (all votes cast or timer expires). This is not configurable.

#### `DrawnToDressPlayerState`
| Property | Type | Description |
|----------|------|-------------|
| `PlayerId` | string | Unique player identifier |
| `DisplayName` | string | Player's display name |
| `IsReady` | bool | Ready state for current phase |
| `OwnedClothingItemIds` | List&lt;Guid&gt; | IDs of items this player drew |
| `SubmittedOutfits` | Dictionary&lt;int, OutfitSubmission&gt; | Outfits keyed by round number |
| `BonusPoints` | int | Accumulated bonus points |

#### `DrawnClothingItem`
| Property | Type | Description |
|----------|------|-------------|
| `Id` | Guid | Unique item identifier |
| `ClothingTypeId` | string | Which clothing type (hat, top, bottom, shoes) |
| `CreatorPlayerId` | string | Who drew this item |
| `SvgContent` | string | The SVG drawing data |
| `IsInPool` | bool | Whether currently available in the shared pool |
| `ClaimedByPlayerId` | string? | Who has claimed this item (null if unclaimed) |

#### `ClothingTypeDefinition`
| Property | Type | Description |
|----------|------|-------------|
| `Id` | string | Type identifier (e.g. "hat", "top") |
| `DisplayName` | string | Display name (e.g. "Hat", "Top") |
| `AllowMultiple` | bool | Whether players can draw multiple of this type |
| `MaxItemsPerRound` | int | Max items per player per round (default: 3) |
| `CanvasWidth` | int | Drawing canvas width |
| `CanvasHeight` | int | Drawing canvas height |

#### `OutfitSubmission`
| Property | Type | Description |
|----------|------|-------------|
| `PlayerId` | string | Who submitted this outfit |
| `SelectedItemsByType` | Dictionary&lt;string, Guid&gt; | One item ID per clothing type |
| `Customization` | OutfitCustomization | Name, sketch, position overrides |
| `SubmittedAt` | DateTime | When outfit was submitted |

#### `OutfitCustomization`
| Property | Type | Description |
|----------|------|-------------|
| `OutfitName` | string | Required outfit name (1-5 words recommended) |
| `SketchSvgContent` | string? | Optional sketch overlay SVG data |
| `ItemPositionOverrides` | List&lt;ItemPositionOverride&gt; | Optional position adjustments per item |

#### `ItemPositionOverride`
| Property | Type | Description |
|----------|------|-------------|
| `ClothingTypeId` | string | Which item slot |
| `X` | double | X position |
| `Y` | double | Y position |

#### `ThemeDefinition`
| Property | Type | Description |
|----------|------|-------------|
| `Id` | string | Theme identifier |
| `DisplayName` | string | Theme display name |
| `Description` | string? | Optional theme description |

#### `VotingCriterionDefinition`
| Property | Type | Description |
|----------|------|-------------|
| `Id` | string | Criterion identifier (e.g. "creativity") |
| `DisplayName` | string | Display name (e.g. "Creativity") |
| `Weight` | double | Point multiplier (default: 1.0) |

#### `SwissMatchup`
| Property | Type | Description |
|----------|------|-------------|
| `Id` | Guid | Matchup identifier |
| `EntrantAId` | string | First outfit's identifier |
| `EntrantBId` | string | Second outfit's identifier |
| `RoundNumber` | int | Which voting round |

#### `VotingRound`
| Property | Type | Description |
|----------|------|-------------|
| `RoundNumber` | int | Round number (1-based) |
| `Matchups` | List&lt;SwissMatchup&gt; | All matchups in this round |

#### `VoteSubmission`
| Property | Type | Description |
|----------|------|-------------|
| `VoterPlayerId` | string | Who voted |
| `MatchupId` | Guid | Which matchup |
| `CriterionId` | string | Which criterion |
| `ChosenEntrantId` | string | Which outfit they voted for |
| `SubmittedAt` | DateTime | When vote was cast |
| `IsLate` | bool | Whether vote was submitted after timer |

#### `LeaderboardEntry`
| Property | Type | Description |
|----------|------|-------------|
| `PlayerId` | string | Player identifier |
| `TotalPoints` | int | Sum of all outfit points + bonuses |
| `MatchupsWon` | int | Number of matchups won (tiebreaker 1) |
| `Rank` | int | Final ranking position |
| `TiebreakerUsed` | string? | "matchups", "coinFlip", or null |

#### `CoinFlipRequest`
| Property | Type | Description |
|----------|------|-------------|
| `CallerId` | string | Player selected to call |
| `TimerDurationSec` | int | Always 15 seconds |
| `Context` | string | "criterionTie" or "finalStandings" |

#### `CoinFlipResult`
| Property | Type | Description |
|----------|------|-------------|
| `CallerChoice` | string | "heads" or "tails" |
| `Result` | string | "heads" or "tails" (randomly generated) |
| `WinnerId` | string | Player who won the flip |
| `WasAutoSelected` | bool | Whether choice was auto-selected due to timeout |

### Out of scope
- FSM state classes
- UI implementation
- Scoring calculations
- Tournament generation algorithms

### Implementation notes
- Keep "drawn asset," "ownership," "claim state," and "outfit submission" as separate concerns.
- Prefer immutable or value-like models where practical.
- Avoid leaking UI-specific concepts into core domain models.

### Acceptance criteria
- All models listed above are created with the exact properties specified.
- Default config values match the tables above exactly.
- Models are suitable for deterministic unit tests.
- Enum types are defined for: ThemeSource (Random, HostPick, PlayerWritten, RandomVoting), ThemeAnnouncement (BeforeDrawing, AfterDrawing), VoteVisibilityMode (Hidden, PercentagesOnly, IndividualVotes, LiveVoting), VotingScope (CannotVoteOnOwn, CanVoteOnOwn, HostDecides), TournamentFormat (Swiss, SingleElimination, Custom), HostRole (Active, Passive), Outfit2PoolType (ExactCopy, FreshDrawingsOnly, Hybrid).
- VotingScope is defined but only CannotVoteOnOwn is currently implemented. Other modes are planned for a future release.

### Test cases
- Config default-value tests: verify every property matches the default specified above.
- Basic construction tests for key domain models.
- Serialization/state persistence tests if applicable.

### Dependencies / sequencing
- Should be completed before most feature issues, especially #51–#65.

---

## #51 — 1.3 Implement Drawn To Dress FSM skeleton and transition map (DONE)

### Summary
Create the FSM skeleton for Drawn To Dress using the shared FSM abstractions in `KnockBox.Core`.

### Goals
- Define the game's phase model as explicit FSM states.
- Establish transition responsibilities early so later feature issues can fill in behavior without redesigning flow.

### In scope
- Add `DrawnToDressCommand` base type and command records.
- Add `IDrawnToDressGameState` and timed-state aliases/interfaces as needed.
- Add initial placeholder state classes.
- Define transition flow between phases.
- Wire FSM creation into game start.

### State list (14 states)
1. `LobbyState` — Players join, host configures settings. Transitions to ThemeSelectionState on StartGameCommand.
2. `ThemeSelectionState` — Theme is selected per configured ThemeSource. Transitions to DrawingRoundState.
3. `DrawingRoundState` — Timed. Sequential drawing rounds by clothing type. Transitions to next DrawingRoundState (next clothing type) or PoolRevealState (after last type).
4. `PoolRevealState` — Timed. View-only reveal of shared pool. Transitions to OutfitBuildingState when all ready or timer expires. For round > 1: optionally reveals theme if AfterDrawing mode, resets pool.
5. `OutfitBuildingState` — Timed. Players claim items from shared pool. Transitions to OutfitCustomizationState when all locked in or timer expires.
6. `OutfitCustomizationState` — Timed. Players name outfit + optional sketch. Transitions to OutfitDistinctnessResolutionState (if round > 1 and conflicts found), PoolRevealState (if more outfit rounds), or VotingRoundSetupState (if last round).
7. `OutfitDistinctnessResolutionState` — Resolves outfit distinctness conflicts for round 2+. Transitions back to OutfitBuildingState or forward to VotingRoundSetupState.
8. `VotingRoundSetupState` — Generates Swiss tournament matchups. Immediately transitions to VotingMatchupState.
9. `VotingMatchupState` — Timed. Players vote on matchups. Transitions to CoinFlipState (if ties) or VotingRoundResultsState.
10. `CoinFlipState` — Handles coin flip tie-breaking. Transitions back to VotingMatchupState or VotingRoundResultsState.
11. `VotingRoundResultsState` — Shows round results. Transitions to VotingRoundSetupState (next round) or FinalResultsState (last round).
12. `FinalResultsState` — Terminal. Shows leaderboard and winner.
13. `PausedState` — Game paused (e.g. host disconnect). Can resume to previous state.
14. `AbandonedState` — Terminal. Game abandoned (host timeout or explicit abandon).

### Command types to define
- `StartGameCommand` — Host starts the game from lobby
- `UpdateConfigCommand` — Host updates configuration
- `SelectThemeCommand` — Host picks a theme (HostPick mode)
- `SubmitPlayerThemeCommand` — Player submits a theme (PlayerWritten mode)
- `VoteForThemeCommand` — Player votes for a theme (RandomVoting mode)
- `SubmitDrawingCommand` — Player submits a drawing
- `MarkReadyCommand` — Player marks ready (pool reveal, results, etc.)
- `ClaimPoolItemCommand` — Player claims an item from the pool
- `UnclaimPoolItemCommand` — Player releases a claimed item
- `SubmitOutfitCommand` — Player submits/locks their outfit
- `SubmitCustomizationCommand` — Player submits outfit name + sketch
- `ResolveDistinctnessCommand` — Player resolves distinctness conflict
- `CastVoteCommand` — Player casts a vote on a matchup criterion
- `RequestCoinFlipCommand` — Player makes a coin flip call
- `PauseGameCommand` — Pause the game
- `ResumeGameCommand` — Resume from pause
- `AbandonGameCommand` — Abandon the game

### Transition flow
```
LobbyState → ThemeSelectionState → DrawingRoundState (hat) → DrawingRoundState (top) →
DrawingRoundState (bottom) → DrawingRoundState (shoes) → PoolRevealState →
OutfitBuildingState → OutfitCustomizationState →
  [if more outfit rounds] → PoolRevealState → OutfitBuildingState → ...
  [if round > 1 and distinctness conflict] → OutfitDistinctnessResolutionState
  [if last round] → VotingRoundSetupState → VotingMatchupState →
    [if tied criterion] → CoinFlipState → VotingMatchupState
    → VotingRoundResultsState →
    [if more voting rounds] → VotingRoundSetupState → ...
    [if last voting round] → FinalResultsState

Any state → PausedState (on host disconnect)
PausedState → previous state (on host reconnect)
PausedState → AbandonedState (on host timeout: 60s default)
Any state → AbandonedState (on explicit abandon)
```

### Out of scope
- Full implementation of every rule in each state
- Final UI for each phase

### Implementation notes
- Keep transition rules explicit and documented in code comments or state summaries.
- Timed states should implement `ITimedDrawnToDressGameState` to enable timer-driven behavior.
- Use placeholder transitions where final logic depends on later issues.

### Acceptance criteria
- Starting a Drawn To Dress game creates a context and FSM in LobbyState.
- FSM can move through a simplified placeholder version of the intended flow.
- All 14 states exist as classes.
- All 17 command types exist as records.
- Unit tests cover basic entry and transition behavior.

### Test cases
- FSM creation test (starts in LobbyState).
- Basic transition tests for nominal flow.
- Pause/abandon transition tests.
- Timed state interface verification.

### Dependencies / sequencing
- Depends on #49 and should preferably follow #50.

---

## #52 — 1.4 Build Drawn To Dress lobby and host configuration UI (DONE)

### Summary
Implement the pre-game lobby and host configuration experience.

### Goals
- Allow players to gather in a room.
- Allow the host to configure the session before starting.
- Validate or normalize settings that would create invalid or confusing gameplay.

### In scope
- Lobby player list
- Start button (enabled when player count >= `MinPlayers`, default 3)
- Settings panel grouped by category
- Host-only settings editing
- Minimum-player warning and start gating
- Validation for conflicting or invalid config combinations
- Quick-select preset buttons (Quick Game, Standard, Full Experience, Creative Focus) that populate config with predefined values
- Individual settings remain editable after preset selection
- No save/load preset functionality (future feature)

### Settings to expose (grouped by category)

**Drawing Phase:**
- `DrawingTimeSec`: default 180, options [45, 60, 90, 120, 180]
- `MaxItemsPerType`: default 3, options [3, 5, 8, 10, unlimited]
- `ClothingTypes`: default [hat, top, bottom, shoes], custom list

**Theme System:**
- `ThemeSource`: default Random, options [Random, HostPick, PlayerWritten, RandomVoting]
- `ThemeAnnouncement`: default BeforeDrawing, options [BeforeDrawing, AfterDrawing]

**Outfit Building:**
- `OutfitBuildingTimeSec`: default 90, options [60, 90, 120, 180, 240, 300, unlimited]
- `OutfitCustomizationTimeSec`: default 60
- `AllowSketching`: default true
- `SketchingRequired`: default false
- `CanReuseOutfit1Items`: default false
- `OutfitDistinctnessThreshold`: default 2, options [1, 2, 3] — only relevant when CanReuseOutfit1Items is true
- `NumOutfitRounds`: default 1, options [1, 2, 3, 4]
- `Outfit2PoolType`: default ExactCopy, options [ExactCopy, FreshDrawingsOnly, Hybrid]

**Voting & Scoring:**
- `VotingCriteria`: default [creativity, theme_match, overall_look] with weights
- `VotingTimeSec`: default 60
- `VotingVisibility`: default PercentagesOnly, options [Hidden, PercentagesOnly, IndividualVotes, LiveVoting]
- `VotingScope`: default CannotVoteOnOwn, options [CannotVoteOnOwn, CanVoteOnOwn, HostDecides]
- `TournamentFormat`: default Swiss, options [Swiss, SingleElimination, Custom]
- `TournamentRounds`: default "auto", options [1-10, "auto"]
- `BonusPoints`: default {RoundLeader: 3, TournamentWin: 10} (0 to disable either)

**Game Flow:**
- `MinPlayers`: default 3
- `HostDisconnectTimeoutSec`: default 60, options [30, 60, 120, 300]

**Advanced:**
- `AllowPlayersToChooseThemes`: default false
- `RandomizePairings`: default true

### Required UX behavior
- Start button enabled when player count >= MinPlayers (default 3).
- If player count is below 6, show warning: "At least 6 players is recommended for the best experience. Currently [N]. Voting may be less meaningful with fewer players."
- Settings grouped by category with default/recommended values obvious.
- Invalid combinations blocked or normalized (e.g., `OutfitDistinctnessThreshold` greyed out when `CanReuseOutfit1Items` is false).
- Each setting should have a tooltip explaining its impact.

### Out of scope
- Theme selection flow itself
- Drawing/gameplay states
- Voting UI

### Implementation notes
- If some settings are not fully used yet, persist them anyway so later issues can rely on them.
- Keep host vs player views clearly separated.

### Acceptance criteria
- Host can configure all settings listed above before starting.
- Non-host players cannot edit host-only settings.
- Invalid combinations are blocked or normalized.
- Fewer-than-6 player counts show warning messaging.
- Starting the game transitions into the first configured gameplay state.
- All default values match the tables above.
- Host can select a preset to populate settings with predefined values.
- Settings remain individually editable after preset selection.
- Host is always a non-participant observer. No HostRole setting is exposed.

### Test cases
- Host permissions tests.
- Config validation tests (conflicting settings).
- Lobby start-gating tests (player count < MinPlayers).
- Default value verification.

### Dependencies / sequencing
- Depends on #49, #50, #51.

---

## #53 — 1.5 Implement theme selection and theme announcement flow (DONE)

### Summary
Implement the theme-selection workflows for all four theme source modes.

### Goals
- Support all configured theme sources.
- Persist the final selected theme for the session.
- Respect the `ThemeAnnouncement` timing rules.

### In scope
- All four theme source modes
- Configurable announcement timing
- Theme persistence in game state

### Theme source behaviors (implement all four)

**Random (default):**
- On entering ThemeSelectionState, system randomly selects one theme from the built-in curated pool.
- No player input needed.
- Immediately transitions to next state.

**HostPick:**
- Host is presented with a list of themes from the built-in pool.
- Host selects one theme via `SelectThemeCommand`.
- Other players wait.
- Transitions when host confirms selection.

**PlayerWritten:**
- Each player submits one theme via `SubmitPlayerThemeCommand`.
- All submitted themes are revealed to all players as-is before outfit building begins.
- No moderation required (game is for family/friends without matchmaking).
- Transitions when all players have submitted (or timeout).

**RandomVoting:**
- System presents `RandomVotingCandidateCount` (default: 3) random themes from the built-in pool.
- Players vote via `VoteForThemeCommand`.
- Theme with most votes wins. Tie: random selection among tied themes.
- Transitions when all players have voted (or timeout).

### Announcement timing
- **BeforeDrawing (default):** Theme is announced/visible before drawing phase starts. Players know the theme while drawing.
- **AfterDrawing:** Theme is selected/persisted during ThemeSelectionState but only revealed to players after all drawing rounds complete (during PoolRevealState before outfit building).

### Key rule
Both outfits in a session always use the same theme. There is no per-outfit theme variation.

### Out of scope
- Lobby settings UI itself
- Drawing implementation
- Outfit building

### Implementation notes
- Keep theme-source acquisition separate from theme-announcement timing.
- Make the theme-selection result durable in game state so later phases can rely on it.
- Include a built-in theme pool with at least 20+ curated themes.

### Acceptance criteria
- Random mode auto-selects a theme without player input.
- HostPick mode lets only the host select from the pool.
- PlayerWritten mode collects one theme per player and reveals all.
- RandomVoting mode presents candidates and tallies votes.
- BeforeDrawing and AfterDrawing announcement timing both work correctly.
- Final theme selection is persisted in game state.
- Both outfits use the same theme.

### Test cases
- One test per theme source mode.
- Announcement timing tests (theme visible/hidden at correct phases).
- Same-theme-for-both-outfits test.
- RandomVoting tie-breaking test.

### Dependencies / sequencing
- Depends on #50–#52.

---

## #54 — 1.6 Implement sequential drawing rounds using SvgDrawingCanvas (DONE)

### Summary
Build the drawing phase using the existing `SvgDrawingCanvas` component.

### Goals
- Support sequential timed drawing rounds by clothing type.
- Persist submitted drawings and creator attribution.
- Produce the raw input needed for the shared clothing pool.

### In scope
- Sequential drawing rounds by clothing type
- Timer per round
- Max-items-per-type enforcement
- Autosave/finalize flow per drawing
- Player-only drawing visibility during this phase
- Separate host/player views

### Required behavior

**Drawing round sequence:**
Default order: hat → top → bottom → shoes (configurable via `ClothingTypes` in config).

**Per round:**
- Display: "Draw [TYPE] — [time] seconds remaining" (e.g., "Draw HATS — 180 seconds remaining")
- Timer: `DrawingTimeSec` seconds per round (default: 180)
- Canvas/drawing area provided for each player
- Players draw freehand using the existing `SvgDrawingCanvas` component
- Players can submit multiple drawings per clothing type, up to `MaxItemsPerType` (default: 3)
- A player may draw zero items in a round (valid — pool may be smaller for that type)
- Timer expiration auto-advances to the next clothing type
- All players can mark ready to advance early (if all ready, skip to next round)

**Host view:**
- Clothing type displayed prominently
- Timer (visual + audio countdown)
- Player submission view (shows which players are finished and item counts)
- Progress indicator (Round X/Y)

**Player view:**
- Clothing type displayed prominently
- Timer (visual + audio countdown)
- Canvas for drawing (full screen or primary area)
- Clear canvas button
- Visual feedback: "Drawing saved automatically"
- Submit button
- Item count: "2/3 items drawn"

**Key constraints:**
- Players cannot see other players' drawings during this phase
- Each drawing is attributed to its creator (`CreatorPlayerId`) — used to block self-picks later
- Drawings are stored as SVG content
- Drawings are preserved on timeout/disconnect where practical

**Post-phase:**
- All drawings are aggregated into the shared clothing pool
- If `ThemeAnnouncement` is AfterDrawing, theme is announced now
- Game transitions to PoolRevealState

### Edge cases
- **Player draws nothing in a round:** Item count for that type = 0; they proceed to next round normally. Pool may be smaller for that clothing type.
- **Drawing canvas crashes mid-round:** Previous saved strokes are preserved. Player reconnects and continues drawing.
- **Multiple players draw identical items:** Allowed. Duplicates are stored separately (same appearance, different creators).

### Out of scope
- Shared pool reveal
- Outfit building
- Customization overlays

### Implementation notes
- Reuse `SvgDrawingCanvas` rather than inventing a new drawing system.
- Persist creator attribution on every submitted drawing.
- Keep drawings hidden from other players during this phase.

### Acceptance criteria
- Players can submit up to 3 drawings per clothing type (default).
- Drawings are persisted and attributed to their creator.
- Timer expiration advances rounds correctly.
- Drawing rounds follow the configured clothing type order.
- End of drawing phase yields a usable set of `DrawnClothingItem` records for pool generation.
- Host view shows per-player progress.
- Player view shows canvas with item count.

### Test cases
- Timer progression tests (advances to next type on expiry).
- Max-items-per-type tests (blocks 4th drawing when max is 3).
- "Player drew nothing" tests (0 items valid).
- Early advance when all players ready.
- Disconnect/autosave preservation tests.

### Dependencies / sequencing
- Depends on #50–#53.

---

## #55 — 1.7 Build shared clothing pool generation and reveal phase (DONE)

### Summary
Generate the shared clothing pool from submitted drawings and implement the reveal phase.

### Goals
- Aggregate all submitted drawings into a shared pool.
- Present a synchronized, view-only reveal before real-time claiming starts.

### In scope
- Pool generation from submitted drawings
- Grouping display by clothing type
- Reveal screen with pool items displayed grouped by clothing type
- Ready tracking
- Auto-advance countdown
- Claim prevention during reveal

### Required behavior

**Pool generation:**
- All drawings from all players are aggregated into a single shared pool.
- Each item preserves: Id, ClothingTypeId, CreatorPlayerId, SvgContent.
- All items start with `IsInPool = true`, `ClaimedByPlayerId = null`.
- All players see the same pool contents.

**Reveal screen:**
- All drawings displayed grouped by clothing type with a countdown timer.
- Large, clear display of each item in the pool.
- Players can browse the full pool at their leisure (view-only).

**Ready tracking:**
- Each player has a "Ready" button to confirm they have seen the pool.
- Counter visible to all: "4 / 6 Ready"
- Players who press Ready see a "Waiting for others..." state.

**Auto-advance:**
- If all players press Ready, outfit building begins immediately.
- If not all players are ready when the countdown timer expires (default: `PoolRevealTimeSec` = 30 seconds), the phase auto-advances.

**For outfit round > 1:**
- Pool is reset: exact copy of original pool minus items used in previous outfit rounds (respecting `Outfit2PoolType` and `CanReuseOutfit1Items` settings).
- If `ThemeAnnouncement` is AfterDrawing and this is the first reveal, theme is revealed now.

**Key constraint:**
- Players CANNOT claim items during the reveal — it is view-only. No claims or reservations may occur during this phase.

### Edge cases
- **A player never presses Ready:** The countdown timer (default: 30s) elapses and the phase auto-advances for all players.
- **Player disconnects during reveal:** Their Ready state is ignored. If they reconnect before the timer elapses, they see the pool and can press Ready. Phase advances normally when timer ends.

### Out of scope
- Real-time claiming logic
- Outfit assembly rules

### Implementation notes
- Preserve item identity from drawing submission into the pool.
- Ready tracking should be authoritative and visible to clients.

### Acceptance criteria
- All players see the same revealed pool.
- Items are grouped by clothing type.
- Ready count updates correctly and is visible.
- Reveal auto-advances on timeout if not all players ready.
- Reveal does not allow any claims.
- Pool reset works correctly for round > 1.

### Test cases
- Pool aggregation tests (all drawings from all players included).
- Ready tracking tests (count updates, all-ready advances immediately).
- Auto-advance timeout tests.
- View-only enforcement tests (claim commands rejected).
- Round 2+ pool reset tests.

### Dependencies / sequencing
- Depends on #54.

---

## #56 — 1.8 Implement real-time shared-pool claiming for outfit building (DONE)

### Summary
Implement the server-authoritative outfit-building rules where players race to claim items from a shared pool.

### Goals
- Support simultaneous real-time claiming.
- Enforce "first valid claim wins."
- Enforce self-drawn-item restrictions and outfit completion rules.

### In scope
- Outfit-building state behavior
- Server-authoritative claim/unclaim logic
- Slot-based outfit assembly
- Self-drawn-item restrictions
- Lock-in and timeout handling
- Claim conflict resolution
- Item removal broadcast to all players

### Required behavior

**Claiming mechanics:**
- Shared clothing pool is displayed with all available items.
- Players click/drag items into their outfit slots (one per clothing type: hat, top, bottom, shoes).
- **First player to claim an item owns it** — determined by server-side timestamp.
- If a player tries to pick an item another player just claimed, selection fails. They must pick something else.
- Players can swap items in/out freely until they lock in. When an item is released, it returns to the pool.
- Each outfit must contain exactly 1 hat, 1 top, 1 bottom, 1 shoes (complete set).
- Claimed items are removed from the pool display for all other players.

**Self-drawn-item restriction:**
- Players CANNOT pick items they personally drew. This rule is absolute — no exceptions.
- Self-drawn items should be visually disabled/grayed out in the UI.
- If clicked: no effect. Tooltip: "You drew this item and cannot use it."

**Timer and lock-in:**
- Time limit: `OutfitBuildingTimeSec` (default: 90 seconds).
- Once a player has all 4 slots filled, they can lock in their outfit (optional — auto-submits on time expiration).
- Lock-in is optional; auto-submits when time expires.

**Auto-fill on timeout:**
- If all 4 slots are filled when time expires: current outfit is auto-submitted.
- If slots are incomplete: random available items in the pool populate the empty slots.
- Auto-fill will NEVER assign an item the player drew themselves.
- Player is notified of what was auto-filled.

**Conflict resolution:**
- Simultaneous claim attempts resolved by server-side timestamp — earliest wins.
- Loser sees item disappear from pool and must select another.
- All claim/unclaim events broadcast to all players in real-time.

### Edge cases
- **Two players click the same item simultaneously:** Server-side timestamp determines winner. Loser sees item disappear.
- **Player doesn't lock in by time expiration:** If all 4 slots filled → auto-submit. If incomplete → auto-fill then submit.
- **Player tries to pick an item they drew:** Item is visually disabled/grayed out. Click has no effect.
- **Pool becomes empty (all items used):** Remaining players cannot complete outfits. Game should prevent this via item count validation.
- **Auto-fill cannot avoid self-drawn items (all remaining items were drawn by this player):** Extremely unlikely with 3+ players. If it occurs, system notifies player and host: "Not enough items remain for [Player] to complete their outfit without using their own drawings." Host must intervene or outfit is submitted incomplete and marked invalid for voting.

### Out of scope
- Final outfit-builder UI polish
- Customization flow
- Outfit 2 distinctness validation

### Implementation notes
- Keep claim resolution deterministic and testable.
- Separate claim ownership from UI state.
- Broadcast item removal/availability updates to all players via WebSocket.

### Acceptance criteria
- Simultaneous claim conflicts resolve deterministically by server timestamp.
- Players cannot claim items they created.
- Players can complete and lock an outfit with exactly 1 item per clothing type.
- Timeout auto-submit works for complete outfits.
- Timeout auto-fill works for incomplete outfits (never assigns self-drawn items).
- All claim/unclaim events broadcast to all connected players.

### Test cases
- Concurrent claim conflict tests (same item, two players).
- Self-drawn-item rejection tests.
- Lock/unlock/replace tests (swap items freely before lock-in).
- Auto-fill behavior tests (incomplete outfit on timeout).
- Auto-fill self-pick avoidance tests.
- Complete outfit auto-submit on timeout.

### Dependencies / sequencing
- Depends on #50, #51, #54, #55.

---

## #57 — 1.9 Build outfit builder UI and player/host progress displays (DONE)

### Summary
Create the Blazor UI for the outfit-building experience.

### Goals
- Give players a clear, responsive interface for selecting items into outfit slots.
- Give hosts visibility into completion progress.

### In scope
- Clothing pool grid
- Outfit slot display
- Drag/click selection UX
- Timer/status panel
- Lock-in button
- Claimed/conflict feedback states
- Host progress panel
- Mobile/touch-friendly interactions

### Required behavior

**Player view layout:**
- Left side: Clothing pool (grid of all available items, scrollable, grouped by type)
- Center: Outfit builder (4 slots: hat, top, bottom, shoes)
- Right side: Timer + status (e.g., "3/4 items selected")
- Drag-and-drop from pool to slots (click/tap also works — item placed in corresponding slot, replacing existing if applicable)
- Lock-in button (enabled when all 4 slots filled)
- Visual feedback: Red outline when item claimed by another player
- Self-drawn items: visually disabled/grayed out with tooltip "You drew this item and cannot use it"

**Host view:**
- Current round displayed prominently ("BUILD OUTFIT #1")
- Player submission view (shows which players have locked in / completed)
- Timer visible

**UI feedback requirements:**
- Unavailable items (claimed by others) show clear visual state
- Claimed-by-other failure states display error feedback
- Valid current selections highlighted
- Instant visual feedback on claim/unclaim (<100ms target)

### Out of scope
- Core claim engine logic (issue #56)
- Customization
- Voting

### Implementation notes
- Treat issue #56 as the source of truth for rules.
- This issue focuses on rendering and interaction, not redefining claim mechanics.
- Touch targets must be at least 44x44px for mobile/touch use.

### Acceptance criteria
- Players can clearly see available items and selected slots.
- Claimed/conflicted items display clear visual feedback.
- Self-drawn items are visually distinguished and non-interactive.
- Host can track completion progress.
- Layout works across desktop, tablet, and mobile form factors.
- Lock-in button works when all 4 slots filled.

### Test cases
- Basic interaction tests for selection and lock-in.
- Visual-state tests for claimed/unavailable/self-drawn items.
- Responsive layout checks across form factors.

### Dependencies / sequencing
- Depends on #56.

---

## #58 — 1.10 Implement outfit customization and naming flow (DONE)

### Summary
Implement post-selection customization, including optional sketch overlay and required outfit naming.

### Goals
- Allow players to finalize an outfit presentation before voting.
- Persist items, name, and optional sketch as a complete outfit submission.

### In scope
- Customization state behavior
- Outfit-name input
- Optional sketch overlay using reusable drawing-canvas patterns
- Item position overrides (drag to adjust item placement)
- Submission tracking
- Required-sketch behavior when enabled by config

### Required behavior

**What the player sees:**
- Their selected outfit items displayed
- Optional sketch canvas overlaid on top (enabled by default: `AllowSketching = true`)
- Text field: "Outfit Name" (required, 1-5 words recommended)
- Optional item position adjustment via drag-and-drop
- Button: "Submit Outfit"
- Progress: "Outfit 1 of [NumOutfitRounds]" (if applicable)

**Timer:** `OutfitCustomizationTimeSec` seconds (default: 60)

**Outfit name:** REQUIRED. Players cannot submit without providing a name.

**Sketch overlay:**
- By default, `AllowSketching = true`: sketch canvas is available but optional.
- If `SketchingRequired = true`: players must provide a sketch before submitting.
- Sketch data is stored as separate SVG content (`SketchSvgContent`), distinct from the original clothing-item drawings.
- Sketching time limit: the customization timer covers both naming and sketching.

**Item position overrides:**
- Players can optionally drag items to adjust their position within the outfit display.
- Position overrides stored as `ItemPositionOverride` (X, Y per clothing type).
- Default positions used if no overrides provided.

**Submission:**
- Persist final outfit submission as: selected items + outfit name + optional sketch + position overrides.
- All players must submit (or auto-submit on timer expiry) before advancing.

**Post-phase transitions:**
- After Outfit 1 customization: if more outfit rounds, transition to PoolRevealState (pool is reset). If last round, transition to VotingRoundSetupState.
- After Outfit 2+ customization: check distinctness first. If conflicts found, transition to OutfitDistinctnessResolutionState. Otherwise, proceed as above.

### Out of scope
- Outfit selection/claiming (issue #56)
- Outfit 2 distinctness rules (issue #59)
- Voting

### Implementation notes
- Reuse SvgDrawingCanvas for sketch overlay.
- Keep sketch overlay data separate from the original clothing-item drawings.
- Store position overrides only when they differ from defaults (keep data lean).

### Acceptance criteria
- Players can name each outfit (required — submission blocked without name).
- Players can optionally sketch over their assembled outfit when `AllowSketching = true`.
- Sketch is required when `SketchingRequired = true`.
- Item position overrides work via drag-and-drop.
- Submissions persist items + name + sketch + position overrides.
- Timer auto-submits on expiry.
- `AllowSketching` defaults to **true**.

### Test cases
- Name-required validation tests (submit blocked without name).
- Optional sketching tests (AllowSketching=true, sketch provided and not provided).
- Required sketching tests (SketchingRequired=true, submit blocked without sketch).
- Position override persistence tests.
- Timer auto-submit tests.
- Submission persistence tests (all fields stored correctly).

### Dependencies / sequencing
- Depends on #54 and #56.

---

## #59 — 1.11 Implement Outfit 2 pool reset and distinctness validation (DONE)

### Summary
Implement Outfit 2 setup, pool-reset behavior, reuse rules, and distinctness validation against all Outfit 1 submissions.

### Goals
- Correctly construct the Outfit 2 pool based on config.
- Enforce reuse and distinctness rules.
- Reject invalid Outfit 2 submissions with actionable feedback.

### In scope
- Generate Outfit 2 pool from config rules
- Enforce reuse rules
- Validate distinctness against all Outfit 1s
- Reject/rebuild invalid Outfit 2 submissions
- Support timeout/autofix behavior

### Required behavior

**Outfit 2 pool generation:**
- Default (`Outfit2PoolType = ExactCopy`): Pool is an exact copy of the original Outfit 1 pool, minus items used in Outfit 1 picks. Items used by different players across outfits are allowed.
- `FreshDrawingsOnly`: Pool contains only new/unused drawings.
- `Hybrid`: Mix of items from Outfit 1 pool and new drawings.

**Reuse rules:**
- `CanReuseOutfit1Items` (default: false): If false, items that were part of any player's Outfit 1 are removed from the Outfit 2 pool entirely. This setting takes precedence over `OutfitDistinctnessThreshold`.
- If true, reuse is allowed subject to the distinctness threshold.

**Distinctness validation:**
- `OutfitDistinctnessThreshold` (default: 2): Only applies when `CanReuseOutfit1Items = true`.
- Outfit 2 must differ from **every player's Outfit 1** (including the submitting player's own) by at least `OutfitDistinctnessThreshold` items.
- Comparison is item-by-item per clothing type slot. Sketches do NOT count toward distinctness — only items matter.
- If any comparison shows matching items >= (4 - OutfitDistinctnessThreshold), outfit is rejected.
  - With threshold=2: 3+ matching items → reject. Must differ by 2+ items.
  - With threshold=1: 4 matching items → reject. Must differ by 1+ items.
  - With threshold=3: 2+ matching items → reject. Must differ by 3+ items.

**Rejection behavior:**
- If Outfit 2 matches any player's Outfit 1 in too many items, it is rejected.
- Player is notified with actionable feedback: "Your second outfit is too similar to [Player Name]'s first outfit ([N] matching items). Please swap at least [threshold] items."
- Player returns to picking phase to make changes.
- Repeat until Outfit 2 passes distinctness check against ALL Outfit 1s.

**Timeout/autofix:**
- If timer expires and outfit still fails distinctness check: system auto-swaps conflicting items with random available items from the pool.
- The self-pick rule is respected during auto-swap (system will not assign items the player drew).
- Player is notified of the swaps made.

### Edge cases
- **Player's Outfit 2 uses 3+ items matching another player's Outfit 1 (with threshold=2):** System rejects. "Too similar to [Player Name]'s first outfit (3 matching items). Swap 2+ items."
- **Player's Outfit 2 uses 3+ items matching their OWN Outfit 1:** Same rejection. "Too similar to your first outfit (3 matching items). Swap 2+ items."
- **Outfit 2 matches multiple players' Outfit 1s:** Check against ALL players. If any comparison fails, reject.
- **Multiple players have identical Outfit 1s:** Outfit 2 must be distinct from all of them individually.
- **Timer expires with failing outfit:** Auto-swap conflicting items, respecting self-pick rule.

### Suggested supporting service
- `OutfitDistinctnessEvaluator` — evaluates shared item count between two outfits and returns pass/fail with details.

### Out of scope
- Voting
- Scoring
- Final results

### Acceptance criteria
- Outfit 2 pool generation is correct for all three pool types (ExactCopy, FreshDrawingsOnly, Hybrid).
- `CanReuseOutfit1Items = false` removes Outfit 1 items from pool entirely.
- `CanReuseOutfit1Items = true` allows reuse subject to distinctness threshold.
- `OutfitDistinctnessThreshold = 2` means Outfit 2 must differ from every Outfit 1 by at least 2 items.
- Invalid Outfit 2 submissions are rejected with clear, actionable feedback naming the conflicting player.
- Timeout auto-swap resolves conflicts while respecting self-pick rule.
- Validation checks against ALL players' Outfit 1s, not just the submitter's.

### Test cases
- Distinctness pass tests (2+ items different → pass with threshold=2).
- Distinctness fail tests (3+ matching items → fail with threshold=2).
- Self-comparison tests (player's own Outfit 1 vs Outfit 2).
- Other-player comparison tests.
- Config precedence tests: CanReuseOutfit1Items=false overrides distinctness threshold.
- Timeout/autofix tests (conflicting items swapped, self-pick rule respected).
- Pool generation tests for each Outfit2PoolType.

### Dependencies / sequencing
- Depends on #55, #56, #58.

---

## #60 — 1.12 Implement tournament pairing and voting-round generation (DONE)

### Summary
Create the tournament engine that turns submitted outfits into voting rounds and matchups.

### Goals
- Register outfit submissions as tournament entrants.
- Generate deterministic, testable round structures.
- Swiss-system is the only supported tournament format.

### In scope
- Register outfits as entrants
- Calculate round count
- Generate matchups
- Enforce creator-voting exclusions in eligibility inputs
- Swiss-system is the only supported tournament format (SingleElimination and Custom are not implemented)

### Required behavior

**Entrant registration:**
- Each submitted outfit becomes a tournament entrant.
- Total entrants = NumOutfitRounds × number of players (e.g., 6 players × 1 outfit = 6 entrants; 6 players × 2 outfits = 12 entrants).

**Round count calculation:**
- Default (`TournamentRounds = "auto"`): Number of rounds = ceil(log2(numOutfits)).
  - 3 players × 1 outfit = 3 outfits → ceil(log2(3)) = 2 rounds
  - 6 players × 1 outfit = 6 outfits → ceil(log2(6)) = 3 rounds
  - 6 players × 2 outfits = 12 outfits → ceil(log2(12)) = 4 rounds
  - 8 players × 2 outfits = 16 outfits → ceil(log2(16)) = 4 rounds
  - 10 players × 2 outfits = 20 outfits → ceil(log2(20)) = 5 rounds
- If `TournamentRounds` is a fixed number (1-10), use that instead.

**Swiss pairing algorithm:**
- **Round 1:** Outfits paired based on deterministic ordering (e.g., by ID). If odd number of outfits, one gets a bye.
- **Subsequent rounds:** Outfits grouped by cumulative points. Within each point group, pair outfits avoiding rematches where possible.
- Avoid self-matchups (a player's two outfits facing each other) where practical, but allow if unavoidable.
- Pairings must be deterministic and testable given the same inputs.

**Voting eligibility:**
- For each matchup, all players EXCEPT the two outfit creators are eligible to vote.
- If `VotingScope = CanVoteOnOwn`, creators can also vote.
- If `VotingScope = HostDecides`, host configures per-player.
- Default: `VotingScope = CannotVoteOnOwn`.

**Tournament state:**
- Store as `VotingRound` objects containing `SwissMatchup` lists.
- Tournament state must be consumable by the FSM (VotingRoundSetupState generates it, VotingMatchupState consumes it).

### Suggested supporting services
- `SwissTournamentService` — generates pairings, calculates round counts, tracks wins.
- `VotingEligibilityService` — determines eligible voters per matchup.

### Out of scope
- Voting UI
- Scoring logic
- Coin flip resolution UI

### Acceptance criteria
- Round count is auto-calculated as ceil(log2(numOutfits)) when set to "auto".
- Fixed round counts (1-10) are respected when configured.
- Swiss pairings are deterministic given the same inputs.
- Self-voting restrictions are reflected in eligibility logic.
- Self-matchups (same player's two outfits) are avoided where practical.
- Rematches are avoided in subsequent rounds where possible.
- Tournament state is persisted as VotingRound/SwissMatchup objects.

### Test cases
- Round-count calculation tests (verify ceil(log2(n)) for various outfit counts).
- Swiss pairing fixture tests (deterministic output for known inputs).
- Rematch avoidance tests.
- Self-matchup avoidance tests.
- Eligibility exclusion tests (creator excluded, VotingScope variations).
- Odd-outfit-count handling (bye).

### Dependencies / sequencing
- Depends on #58 and #59.

---

## #61 — 1.13 Implement matchup voting flow and vote capture (DONE)

### Summary
Build the player-facing voting experience for outfit matchups.

### Goals
- Allow eligible players to vote criterion-by-criterion on each matchup.
- Persist votes in a structure that scoring can consume.
- Enforce creator-voting restrictions and complete-submission validation.

### In scope
- Side-by-side outfit comparison UI
- Criterion-by-criterion voting
- Submit validation
- Vote persistence
- Missing/late vote handling
- Host/player progress feedback
- All four visibility modes

### Required behavior

**Voting UI (player view):**
- Progress: "Round X of Y - Matchup M of N"
- Both outfits displayed side-by-side (left vs. right) with: items, sketches (if present), names, theme
- Voting criteria displayed clearly (default: Creativity, Theme Match, Overall Look)
- For each criterion: "Choose A" or "Choose B" buttons
- Submit vote button (enabled only when all criteria have selections)
- Timer: `VotingTimeSec` seconds (default: 60) with urgency indicator at <= 10 seconds

**Voting UI (host view):**
- Same matchup display
- Matchup progress with vote counts
- Which players have submitted votes

**Creator exclusion:**
- Default (`VotingScope = CannotVoteOnOwn`): Players cannot vote on matchups involving their own outfits. Show: "You created one of these outfits and cannot vote."
- `CanVoteOnOwn`: Players can vote for themselves.
- `HostDecides`: Host configures per-player.

**Vote submission:**
- Voters MUST vote on ALL criteria before submitting (incomplete submissions blocked).
- Each vote is persisted as a `VoteSubmission` record: VoterPlayerId, MatchupId, CriterionId, ChosenEntrantId, SubmittedAt, IsLate.

**Missing/late votes:**
- Votes not submitted before timer expires are NOT counted.
- Missing votes reduce the vote pool but don't invalidate the matchup.
- Example: 4 eligible voters, 3 submit. Only 3 votes count.
- Late votes (submitted after timer but before state transition) are marked `IsLate = true` and not counted.

**Visibility modes:**
| Mode | Behavior |
|------|----------|
| Hidden | No vote information revealed until final results |
| PercentagesOnly (default) | Only vote percentages shown after voting (e.g., "60% vs 40%") |
| IndividualVotes | See who voted for what (full transparency) |
| LiveVoting | See votes come in real-time as they're cast |

**Vote changing:** Players may change their votes at any time before voting ends (all players submit or timer expires). Previously submitted votes are overwritten. There is no AllowVotingRollback config — this behavior is always enabled.

**Creator identity:** When `ShowCreatorDuringVoting = false` (default), outfit creator identity is hidden during voting. Only the outfit name is shown. When `true`, the creator's name is displayed below the outfit name.

### Edge cases
- **Voter is one of the two outfit creators:** Voter is disabled for that matchup. "You created one of these outfits and cannot vote."
- **Voter doesn't submit before round closes:** Vote not counted. Missing votes don't invalidate matchup.
- **All voters abstain / no votes submitted:** Matchup results in 0-0 tie. Coin flip determines winner, who receives 1 bonus point.

### Out of scope
- Score computation (issue #62)
- Coin flip logic (issue #63)
- Final leaderboard (issue #64)

### Implementation notes
- Keep vote capture independent from score calculation.
- Persist votes at the matchup + criterion + voter level.
- Votes are changeable until voting ends (all votes cast or timer expires). Previously submitted votes are overwritten.

### Acceptance criteria
- Non-creators can vote on all required criteria.
- Creators cannot vote on their own outfits (default VotingScope).
- Incomplete vote submissions are blocked.
- Votes are stored per matchup, criterion, and voter.
- Missing/late votes are not counted.
- All four visibility modes work correctly.
- Default `VotingVisibility` is **PercentagesOnly**.
- Timer shows urgency indicator at <= 10 seconds.
- Players can change their votes after initial submission until voting ends.
- When ShowCreatorDuringVoting is false, creator identity is not shown during voting.

### Test cases
- Voting eligibility tests (creator excluded in default mode).
- Submit validation tests (incomplete submission blocked).
- Missing/late vote handling tests.
- Visibility-mode behavior tests (each of the 4 modes).
- All-abstain edge case (0-0 tie triggers coin flip).
- VotingScope variation tests.
- Vote-changing tests (player submits, changes, resubmits).
- ShowCreatorDuringVoting tests (identity hidden when false, shown when true).

### Dependencies / sequencing
- Depends on #60.

---

## #62 — 1.14 Implement scoring engine, weighted criteria, and bonuses

### Summary
Implement the scoring rules that convert persisted votes into criterion scores, matchup totals, round bonuses, and final player totals.

### Goals
- Convert persisted votes into scores exactly as specified below.
- Preserve enough metadata for leaderboard tiebreakers.

### In scope
- Points per vote
- Weighted criteria
- Tied-criterion bonus via coin flip integration
- Round leader bonus
- Tournament winner bonus
- Player score rollups across all outfits
- Matchup-win tracking for tiebreakers

### Scoring model

**Base scoring:**
- Each vote cast for an outfit earns that outfit **1 point** per criterion.
- If criterion weights are configured, each vote is worth `weight` points instead of 1.
- **Both outfits in a matchup earn points** — scoring is proportional, not winner-takes-all.

**Per-criterion calculation:**
- For each criterion in a matchup: count votes for Outfit A and votes for Outfit B.
- Outfit A criterion points = (votes for A) × criterion weight.
- Outfit B criterion points = (votes for B) × criterion weight.
- If votes are exactly tied on a criterion → coin flip is triggered (see issue #63). The coin flip winner receives **1 bonus point** for that criterion.

**Matchup total:**
- Sum all criterion points for each outfit in the matchup.

**Scoring example (3 criteria, default weights of 1, 4 eligible voters):**

| Criterion | Votes for A | Votes for B | A Points | B Points |
|-----------|-------------|-------------|----------|----------|
| Creativity | 3 | 1 | 3 | 1 |
| Theme Match | 2 | 2 (tie → coin flip → A wins) | 2 + 1 bonus = 3 | 2 |
| Overall Look | 3 | 1 | 3 | 1 |
| **Matchup Total** | | | **9** | **4** |

**With weighted criteria example (creativity weight=2, others weight=1):**

| Criterion | Votes for A | Votes for B | A Points | B Points |
|-----------|-------------|-------------|----------|----------|
| Creativity (×2) | 3 | 1 | 6 | 2 |
| Theme Match (×1) | 2 | 2 (tie → coin flip → B wins) | 2 | 2 + 1 bonus = 3 |
| Overall Look (×1) | 3 | 1 | 3 | 1 |
| **Matchup Total** | | | **11** | **6** |

### Bonus points

**Round leader bonus:** +3 points (default, configurable via `BonusPoints.RoundLeader`, 0 to disable)
- Awarded to the outfit that accumulated the most points in a given voting round (across all its matchups in that round).
- If tied for round leader: ALL tied outfits receive the bonus.

**Tournament winner bonus:** +10 points (default, configurable via `BonusPoints.TournamentWin`, 0 to disable)
- Awarded to the player with the highest cumulative points across all rounds at the end of the tournament.

### Player totals
- A player's total score = sum of all their outfits' accumulated points + bonus points.
- If a player has 2 outfits, both outfits' points contribute to the player's total.

### Matchup-win tracking
- Track how many matchups each outfit/player won (needed for tiebreaking).
- An outfit "wins" a matchup if its total points in that matchup exceed the opponent's.
- Tied matchups: both outfits get 0.5 matchup wins (or track separately).

### Tiebreaker chain (for final standings)
1. **Total points** (descending) — primary ranking.
2. **Matchups won** — if total points are tied, the player who won more individual matchups wins.
3. **Coin flip** — if matchups won are also tied, a coin flip determines the winner (see issue #63).

### Scoring edge cases
- **Outfit receives all votes in a matchup:** Outfit gets all available points for each criterion. Other outfit gets 0 for those criteria.
- **Both outfits tie on all criteria:** One coin flip per tied criterion. Each coin flip awards 1 bonus point to its winner. Outfits may split tied criteria (e.g., A wins creativity flip, B wins theme flip).
- **Player's two outfits face each other in voting:** Both outfits are valid. Player cannot vote on this matchup. Voting continues with all other eligible players. Both outfits earn points normally.
- **Final standings result in an exact tie between two players:** Apply tiebreaker chain: total points → matchups won → coin flip.
- **All voters abstain / no votes for a matchup:** 0-0 tie on all criteria. Coin flip per criterion → winner gets 1 bonus point per flip.

### Suggested supporting service
- `DrawnToDressScoringService` — calculates criterion scores, matchup totals, round bonuses, player totals, and tracks matchup wins.

### Out of scope
- Voting UI (issue #61)
- Final leaderboard UI (issue #64)
- Coin flip UI/workflow (issue #63) — but scoring must integrate with coin flip results

### Acceptance criteria
- Each vote for an outfit earns 1 × criterion weight points.
- Both outfits in a matchup earn points proportionally.
- Tied criteria trigger coin flip and award 1 bonus point to winner.
- Round leader bonus (+3 default) awarded correctly, including ties.
- Tournament winner bonus (+10 default) awarded to highest-scoring player.
- Player totals roll up all their outfits' points correctly.
- Matchup-win counts are persisted for tiebreaking.
- Bonus point values are configurable (0 disables).
- Scoring outputs match the example fixtures above.

### Test cases
- Basic scoring: votes × weight = points for each criterion.
- Proportional scoring: both outfits earn points in a matchup.
- Tied-criterion coin flip bonus test.
- Round leader bonus test (single leader and tied leaders).
- Tournament winner bonus test.
- Player rollup test (2 outfits' points summed).
- Weighted criteria test (different weights produce correct totals).
- Matchup-win tracking test.
- All-abstain edge case (0-0 triggers coin flips).
- Exact scoring example fixture verification (see examples in this issue).

### Dependencies / sequencing
- Depends on #60, #61. Depends on #63 if coin flip integration is hard-linked (can stub coin flip results for testing).

---

## #63 — 1.15 Implement coin flip tie-break workflow and UI

### Summary
Implement the coin-flip workflow used for tied criteria in matchups and tied final standings.

### Goals
- Provide a reusable, server-authoritative tie-break mechanism.
- Support both matchup-level criterion ties and final-standings tie resolution.

### In scope
- CoinFlipState FSM behavior
- Caller selection
- 15-second server-authoritative timer
- Heads/tails choice UI
- Timeout auto-selection
- Random result generation (server-side)
- All-player result display
- Reuse for both criterion ties and final standings ties

### Coin flip procedure (6 steps)

1. **Caller selection:** The system randomly selects one of the two affected players to call.
2. **Prompt:** That player is prompted: "Call heads or tails!" with a **15-second timer** clearly visible.
3. **Timeout handling:** If the player does not respond before the timer expires, the system randomly selects heads or tails on their behalf. Display: "Time's up! [Heads/Tails] was chosen for you."
4. **Result generation:** The system randomly generates a result (heads or tails). This MUST be server-side to prevent client manipulation.
5. **Resolution:** If the call matches the result, the calling player wins the flip; otherwise the other player wins.
6. **Broadcast:** The winner receives the coin flip bonus point (for tied criteria) or is declared the tiebreaker winner (for tied final standings). Result is broadcast to all players simultaneously.

### UI requirements

**Coin Flip Screen:**
- Displayed when a tie must be broken.
- Shows which player has been selected to call.
- Large "HEADS" and "TAILS" buttons (for the caller only).
- 15-second countdown timer (clearly visible to all players).
- Non-callers see a waiting screen: "[Player Name] is calling it..."
- On timeout: "Time's up! [Heads/Tails] was chosen for you."
- On resolution: all players see the result and outcome simultaneously.

### Two usage contexts

**1. Criterion tie (during VotingMatchupState):**
- When votes are exactly tied on a criterion, CoinFlipState is entered.
- Winner receives 1 bonus point for that criterion.
- After resolution, return to VotingMatchupState to continue processing remaining criteria/matchups.
- Multiple coin flips may occur in a single voting round (one per tied criterion per matchup).

**2. Final standings tie (during FinalResultsState):**
- When two or more players are tied on total points AND matchups won.
- Same procedure: random player from the tied group is selected to call.
- Winner is declared the overall tiebreaker winner.

### Edge cases
- **Selected caller disconnects before calling:** If reconnects before 15s timer expires, they can still call. If timer expires (disconnect or inaction), system auto-selects on their behalf and proceeds.
- **Coin flip needed for final standings tiebreaker:** Same procedure as matchup coin flips. Random player from tied group selected to call. System resolves and declares winner.
- **Multiple tied criteria in one matchup:** Handle sequentially — one coin flip per tied criterion.

### Suggested supporting service
- `CoinFlipService` — handles caller selection, timer management, random generation, result broadcasting.

### Out of scope
- Score computation beyond exposing result data (issue #62)
- Final leaderboard UI itself (issue #64)

### Implementation notes
- Timer MUST be server-authoritative (server-side 15-second timer to prevent client-side manipulation).
- Random result generation MUST be server-side.
- The CoinFlipState should be reusable — it doesn't need to know whether it was invoked for a criterion tie or a final standings tie. It receives a `CoinFlipRequest` and produces a `CoinFlipResult`.

### Acceptance criteria
- Criterion ties trigger a coin flip that awards 1 bonus point to the winner.
- Final-standings ties use the same coin flip flow.
- Caller is randomly selected from the two affected players.
- 15-second timer is server-authoritative.
- Timeout auto-selects a call on behalf of the inactive player.
- Result and winner are broadcast to all players simultaneously.
- UI clearly shows caller, timer countdown, and result.
- Non-callers see a waiting state.

### Test cases
- Caller selection test (random, one of two affected players).
- Timer expiry auto-selection test.
- Result generation test (random heads/tails).
- Win/loss resolution test (call matches result → caller wins).
- Criterion tie integration test (bonus point awarded correctly).
- Final standings tie integration test.
- Caller disconnect + timeout test.
- Multiple sequential coin flips test (multiple tied criteria).

### Dependencies / sequencing
- Can proceed in parallel with #61/#62 if contracts (CoinFlipRequest/CoinFlipResult) are agreed.

---

## #64 — 1.16 Build leaderboard, winner declaration, and final results screen

### Summary
Create the end-of-game results flow.

### Goals
- Present final player standings clearly.
- Explain winner determination and any applied tiebreakers.
- Offer end-of-game actions.

### In scope
- Player ranking by total points
- Tiebreaker resolution display
- Winner highlight
- Detailed breakdown (optional expandable view)
- Play-again / return-to-menu actions

### Required behavior

**Ranking logic:**
1. Rank players by total points descending.
2. If tied on total points: player who won more individual matchups wins.
3. If still tied on matchups won: coin flip determines the winner (uses CoinFlipState from issue #63).
4. Display the tiebreaker path used (if any): "Tie broken by matchups won" or "Tie broken by coin flip."

**Results screen UI:**
- Leaderboard ranked by total points.
- Winner highlighted (e.g., crown emoji, color highlight, celebratory message).
- Each player row shows: rank, player name, total points, matchups won.
- Tiebreaker result shown if applicable.
- Optional detailed breakdown (expandable view): per-outfit points, per-round points, bonus points earned.
- "Play Again" button → returns to settings/lobby for a new game.
- "Return to Menu" button → exits to main menu.

**FinalResultsState behavior:**
- Compute final leaderboard from accumulated scoring data.
- If ties exist that require coin flips, transition to CoinFlipState first, then return to display final results.
- Terminal state — no further gameplay transitions.

### Edge cases
- **Final standings exact tie between two players:** Apply tiebreaker chain (total points → matchups won → coin flip). Display which tiebreaker was used.
- **Multiple players tied:** Resolve pairwise. Each tie that reaches coin flip stage uses the standard coin flip procedure.

### Suggested supporting service
- `LeaderboardService` — computes rankings, applies tiebreakers, generates LeaderboardEntry records.

### Out of scope
- Scoring engine logic (issue #62)
- Coin flip implementation (issue #63)
- Earlier gameplay phases

### Acceptance criteria
- Leaderboard reflects final scoring accurately.
- Rankings follow: total points → matchups won → coin flip.
- Winner is clearly highlighted with celebratory treatment.
- Tiebreaker path is displayed when used.
- Detailed breakdown shows per-outfit and per-round points.
- "Play Again" and "Return to Menu" actions work correctly.
- FinalResultsState is a terminal state.

### Test cases
- Ranking tests (correct ordering by total points).
- Tiebreak-display tests (matchups won tiebreaker shown).
- Coin-flip tiebreak-display tests.
- Detailed breakdown accuracy tests.
- End-of-game action tests (play again, return to menu).
- Multiple-tie resolution tests.

### Dependencies / sequencing
- Depends on #62 and #63.

---

## #65 — 1.17 Implement reconnect, pause, and host disconnect handling

### Summary
Implement resilience behavior for disconnects and recovery across the game lifecycle.

### Goals
- Preserve sensible behavior when players or the host disconnect.
- Allow resume where expected.
- Abandon the game when host timeout rules are exceeded.

### In scope
- Reconnect support for all major phases
- Host disconnect pause behavior
- Host reconnect resume behavior
- Host timeout abandon behavior
- Inactive-player handling mid-game
- Persistence/restoration of in-progress phase state

### Required behavior — per-phase reconnect

**Drawing phase:**
- Player's drawings up to disconnect are saved.
- If reconnect before phase ends: resume drawing.
- If reconnect after phase: saved drawings are included in pool.

**Pool reveal:**
- If reconnect before timer elapses: player sees pool and can press Ready.
- Phase advances normally when timer ends regardless of disconnected players.

**Outfit building:**
- Player's current outfit (if any) is frozen on disconnect.
- If reconnect before phase ends: resume picking.
- If reconnect after phase: last outfit is submitted (if complete) or rejected (if incomplete).

**Customization:**
- In-progress name/sketch preserved.
- If reconnect before phase ends: resume customization.
- If reconnect after phase: current state auto-submitted.

**Voting:**
- Votes already cast are saved.
- If reconnect: can continue voting on remaining matchups.
- Votes not cast before timer are not counted.

### Host disconnect handling
- **Host disconnects:** Game is immediately paused (transition to PausedState). All players see "Game paused — waiting for host to reconnect."
- **Host reconnects:** Game resumes from the paused state. Players notified: "Host has reconnected. Game resuming."
- **Host timeout:** If host does not reconnect within `HostDisconnectTimeoutSec` (default: **60 seconds**, options: 30, 60, 120, 300), the game is abandoned (transition to AbandonedState). Players notified: "Host did not reconnect. Game has been abandoned."

### Mid-game player leave/quit
- Player is marked inactive.
- Their outfits remain in the pool for voting (outfits can still earn votes/points).
- They cannot vote but see results if they reconnect.
- Scoring continues normally (their outfits can still earn votes).

### Edge cases
- **Player disconnects during drawing:** Saved strokes preserved. Reconnect resumes. Drawings included in pool regardless.
- **Player disconnects during outfit picking:** Current outfit frozen. Reconnect before phase ends → resume. After phase → submit if complete, reject if not.
- **Player disconnects during voting:** Cast votes saved. Reconnect → continue on remaining matchups. Uncast votes not counted.
- **Host disconnects:** Game paused. Host reconnect → resume. Host timeout (60s default) → abandoned.
- **Very large player count (20+):** Swiss system scales. No functional issues expected.

### Out of scope
- Infrastructure beyond what Drawn To Dress needs
- Non-game-wide generic reconnection framework redesign unless necessary

### Acceptance criteria
- Player reconnect restores relevant phase context for each major phase.
- Host disconnect pauses the game immediately.
- Host reconnect resumes play from the paused state.
- Host timeout (default 60s) transitions to AbandonedState.
- Inactive players' outfits remain in voting pool.
- `HostDisconnectTimeoutSec` defaults to **60** (not 120).
- Critical disconnect/reconnect flows are covered by tests.

### Test cases
- Per-phase reconnect tests (drawing, reveal, building, customization, voting).
- Host disconnect → pause tests.
- Host reconnect → resume tests.
- Host timeout → abandoned tests (verify 60s default).
- Mid-game player leave tests (outfits remain, scoring continues).
- Inactive player voting exclusion tests.

### Dependencies / sequencing
- Best after major gameplay phases exist (#54–#61).

---

## #66 — 1.18 Add unit tests for Drawn To Dress FSM states and services

### Summary
Add focused, deterministic tests for FSM states and supporting services.

### Goals
- Ensure each major state and core rules engine is covered.
- Capture key edge cases as regression tests.

### In scope

**Per-state tests (for each of the 14 FSM states):**
- `OnEnter` tests — verify initial state setup, timer initialization, data preparation.
- `HandleCommand` tests — verify correct command handling and state transitions.
- `Tick` tests (for timed states) — verify timer countdown, auto-advance, and timeout behavior.

**Service-specific tests:**

**OutfitDistinctnessEvaluator:**
- Pass: 2+ items different (threshold=2) → valid.
- Fail: 3+ matching items (threshold=2) → invalid.
- Self-comparison (player's own Outfit 1 vs Outfit 2).
- Cross-player comparison.
- Config precedence: CanReuseOutfit1Items=false overrides threshold.

**Claim conflict resolution:**
- Simultaneous claims (same item, two players) → first-by-timestamp wins.
- Self-drawn item rejection.
- Auto-fill never assigns self-drawn items.
- Pool exhaustion handling.

**SwissTournamentService:**
- Round count = ceil(log2(numOutfits)) for various outfit counts.
- Deterministic pairing for Round 1.
- Points-based pairing for subsequent rounds.
- Rematch avoidance.
- Self-matchup avoidance (same player's outfits).

**DrawnToDressScoringService:**
- Votes × weight = correct points per criterion.
- Both outfits earn points proportionally.
- Tied criterion → coin flip bonus (1 point).
- Round leader bonus (+3, including ties).
- Tournament winner bonus (+10).
- Player rollup (sum of all outfits' points).
- Matchup-win tracking.

**CoinFlipService:**
- Random caller selection.
- Timeout auto-selection (15s).
- Result generation (random).
- Win/loss resolution.

**Disconnect/reconnect:**
- Per-phase reconnect state restoration.
- Host disconnect → pause.
- Host timeout → abandon.

### Testing checklist (from design document)
- [ ] Drawing phase time tracking (ensure rounds end on time)
- [ ] Pool reveal animation (verify all items displayed, ready-gate works, auto-advance fires)
- [ ] Simultaneous picking conflict resolution (verify server timestamps are accurate)
- [ ] Auto-fill self-pick rule compliance (verify auto-fill never assigns player's own drawings)
- [ ] Outfit distinctness validation (ensure threshold-based difference is checked correctly)
- [ ] `CanReuseOutfit1Items` taking precedence over `OutfitDistinctnessThreshold`
- [ ] Swiss system pairing (verify correct matchups based on cumulative points)
- [ ] Vote tallying (ensure vote counts are accurate; 1 pt per vote × criterion weight)
- [ ] Coin flip procedure (random player selection, 15s timer, auto-select on timeout)
- [ ] Tiebreaker logic (points → matchups won → coin flip)
- [ ] Bonus point calculation (round leader and tournament winner awarded correctly)
- [ ] Edge case handling (disconnects, timeouts, empty pools, etc.)
- [ ] Accessibility (keyboard nav, screen reader, high contrast)
- [ ] Mobile responsiveness (drawing on mobile, touch interactions)
- [ ] ShowCreatorDuringVoting display tests (anonymous vs. named)
- [ ] Vote-changing tests (overwrite previous vote, final tally uses latest vote)

### Acceptance criteria
- Each of the 14 FSM states has at least one OnEnter, HandleCommand, and (if timed) Tick test.
- Core rule engines (distinctness, claiming, pairing, scoring, coin flip) are covered with deterministic fixtures.
- Important edge cases are represented as regression tests.
- All tests are deterministic (no flaky tests from timing or randomness — use seeded random or dependency injection).

### Dependencies / sequencing
- Depends on feature issues being implemented (#49–#65).

---

## #67 — 1.19 Accessibility, responsiveness, and performance polish for Drawn To Dress

### Summary
Perform a final polish pass on the Drawn To Dress UX and runtime responsiveness.

### Goals
- Make core flows usable across desktop, tablet, and mobile.
- Improve accessibility and reduce obvious latency/regressions.

### In scope
- Touch target sizing
- Keyboard accessibility
- Screen reader improvements
- Timer visibility
- Responsive layout refinement
- Pool rendering optimization
- Drawing responsiveness checks
- Real-time interaction responsiveness

### Required behavior

**Touch-friendly sizing:**
- All interactive elements (buttons, pool items, outfit slots, voting buttons) must be at least **44x44 pixels**.
- This applies to all phases: drawing, pool reveal, outfit building, customization, voting, coin flip, results.

**Keyboard accessibility:**
- All interactive elements must be reachable and operable via keyboard.
- Focus indicators must be visible.
- Tab order must be logical.

**Screen reader improvements:**
- Key state changes announced (timer warnings, claim failures, vote confirmations).
- Outfit descriptions available as alt text or aria labels.
- Phase transitions announced.

**Timer visibility:**
- Large, visible countdown timer in all timed phases.
- Color changes as time runs low (e.g., red at <= 10 seconds).
- Timer urgency indicator at <= 10 seconds remaining.

**Responsive layout:**
- All phases must work on:
  - **Mobile** (portrait + landscape): Drawing canvas scales. Pool items stack vertically. Voting outfits stack vertically on narrow screens.
  - **Tablet**: Hybrid layout. Side-by-side where space permits.
  - **Desktop**: Full layout with side-by-side comparisons, pool grid, and outfit builder.
- Drawing canvas must scale appropriately per device.

**Performance targets:**
- Drawing phase: Smooth **60fps** canvas rendering on all target devices.
- Outfit picking: Instant visual feedback, **<100ms latency** for claim/unclaim operations.
- Voting: No perceptible lag when clicking vote options.
- Network: Real-time sync via WebSocket for simultaneous picking and live voting.

### Out of scope
- New gameplay features
- Major architectural changes

### Acceptance criteria
- All interactive elements are at least 44x44px.
- Core game flows are keyboard-operable.
- Screen reader announces key state changes.
- Timers are large, visible, and change color at <= 10 seconds.
- Primary game flows are usable on mobile, tablet, and desktop.
- Drawing canvas renders at 60fps.
- Outfit claiming feedback is < 100ms.
- No obvious lag/regressions in drawing, claiming, or voting.

### Test cases
- Touch target size audit (all interactive elements >= 44x44px).
- Keyboard navigation test (tab through all phases).
- Screen reader announcement test (key state changes).
- Responsive layout test (mobile portrait, mobile landscape, tablet, desktop).
- Drawing performance test (60fps).
- Claiming latency test (<100ms).
- Cross-browser basic functionality check.

### Notes
- Sound effects are planned for a future release.

### Dependencies / sequencing
- Final pass issue; should come after all major feature work (#49–#66).
