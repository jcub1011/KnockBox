

## #49 — 1.1 Scaffold Drawn To Dress project structure and register it in KnockBox (DONE)

**Suggested rewritten body:**

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
- Mirror Card Counter’s project organization and naming patterns where reasonable.
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
Define the core domain and configuration models needed to support all game concepts in the GDD.

### Goals
- Create stable, testable models for session state, player state, clothing items, outfit submissions, themes, voting, and results.
- Encode default configuration values from the GDD.

### In scope
Add or define models for:
- `DrawnToDressConfig`
- `DrawnToDressPlayerState`
- `DrawnClothingItem`
- `ClothingTypeDefinition`
- `OutfitSubmission`
- `OutfitCustomization`
- `ThemeDefinition`
- `VotingCriterionDefinition`
- `SwissMatchup`
- `VotingRound`
- `VoteSubmission`
- `LeaderboardEntry`
- `CoinFlipRequest`
- `CoinFlipResult`

### Required modeling details
- A clothing item must preserve:
  - clothing type
  - creator/player identity
  - drawing asset/reference
  - pool/claim usage metadata
- Outfit data must keep selected items separate from customization overlay/name.
- Voting data must distinguish:
  - matchup
  - voter
  - criterion
  - chosen outfit
  - submission timing/status
- Config must include defaults matching the GDD, including:
  - drawing timing
  - clothing types
  - theme source / announcement timing
  - outfit building time
  - sketching flags
  - reuse / distinctness settings
  - voting criteria / weights
  - visibility
  - tournament format / rounds
  - bonus points
  - host disconnect timeout

### Out of scope
- FSM state classes
- UI implementation
- Scoring calculations
- Tournament generation algorithms

### Implementation notes
- Keep “drawn asset,” “ownership,” “claim state,” and “outfit submission” as separate concerns.
- Prefer immutable or value-like models where practical.
- Avoid leaking UI-specific concepts into core domain models.

### Acceptance criteria
- Models cover all major GDD concepts needed by later issues.
- Default config values match the design doc.
- Naming is consistent and implementation-friendly.
- Models are suitable for deterministic unit tests.

### Test cases
- Config default-value tests.
- Serialization/state persistence tests if applicable.
- Basic construction tests for key domain models.

### Dependencies / sequencing
- Should be completed before most feature issues, especially #51–#65.

---

## #51 — 1.3 Implement Drawn To Dress FSM skeleton and transition map (DONE)

### Summary
Create the FSM skeleton for Drawn To Dress using the shared FSM abstractions in `KnockBox.Core`.

### Goals
- Define the game’s phase model as explicit FSM states.
- Establish transition responsibilities early so later feature issues can fill in behavior without redesigning flow.

### In scope
- Add `DrawnToDressCommand` base type and command records.
- Add `IDrawnToDressGameState` and timed-state aliases/interfaces as needed.
- Add initial placeholder state classes.
- Define transition flow between phases.
- Wire FSM creation into game start.

### Initial state list
- `LobbyState`
- `ThemeSelectionState`
- `DrawingRoundState`
- `PoolRevealState`
- `OutfitBuildingState`
- `OutfitCustomizationState`
- `OutfitDistinctnessResolutionState`
- `VotingRoundSetupState`
- `VotingMatchupState`
- `CoinFlipState`
- `VotingRoundResultsState`
- `FinalResultsState`
- `PausedState`
- `AbandonedState`

### Required flow coverage
The skeleton should represent, at minimum:
- lobby → theme selection / first configured gameplay state
- drawing rounds → reveal
- reveal → outfit building
- outfit building → customization
- outfit 1 completion → outfit 2 flow
- outfit 2 completion → voting setup
- voting matchup / results / next round progression
- coin flip as a tie-resolution branch
- final results
- pause / abandon transitions

### Out of scope
- Full implementation of every rule in each state
- Final UI for each phase

### Implementation notes
- Keep transition rules explicit and documented in code comments or state summaries.
- Timed states should make later timer-driven behavior easy to add.
- Use placeholder transitions where final logic depends on later issues.

### Acceptance criteria
- Starting a Drawn To Dress game creates a context and FSM.
- FSM can move through a simplified placeholder version of the intended flow.
- State responsibilities and transition ownership are documented.
- Unit tests cover basic entry and transition behavior.

### Test cases
- FSM creation test.
- Basic transition tests for nominal flow.
- Pause/abandon transition tests.

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
- Start button
- Settings panel grouped by category
- Host-only settings editing
- Minimum-player warning and start gating
- Validation for conflicting or invalid config combinations

### Settings to expose
- drawing timing
- max items per type
- clothing types
- theme source
- theme announcement timing
- outfit building limits
- sketch settings
- reuse/distinctness rules
- voting criteria and weights
- voting visibility
- tournament format/rounds
- bonus points
- host disconnect timeout

### Required UX behavior
- Start button should be enabled/disabled according to host permissions and room validity.
- If player count is below recommended minimum, show warning messaging consistent with the GDD.
- Settings should be grouped by category and default/recommended values should be obvious.
- Invalid combinations should be blocked or normalized.

### Out of scope
- Theme selection flow itself
- Drawing/gameplay states
- Voting UI

### Implementation notes
- Match the GDD’s configuration semantics exactly where possible.
- If some settings are not fully used yet, persist them anyway so later issues can rely on them.
- Keep host vs player views clearly separated.

### Acceptance criteria
- Host can configure a room before starting.
- Non-host players cannot edit host-only settings.
- Invalid combinations are blocked or normalized.
- Fewer-than-recommended player counts show warning messaging.
- Starting the game transitions into the first configured gameplay state.

### Test cases
- Host permissions tests.
- Config validation tests.
- Lobby start-gating tests.

### Dependencies / sequencing
- Depends on #49, #50, #51.

---

## #53 — 1.5 Implement theme selection and theme announcement flow (DONE)

### Summary
Implement the theme-selection workflows defined in the GDD.

### Goals
- Support all configured theme sources.
- Persist the final selected theme for the session.
- Respect the `themeAnnouncement` timing rules.

### In scope
- Built-in theme pool
- Host-picked themes
- Player-written themes
- Random-voting scaffolding
- Configurable announcement timing

### Required behavior
- Both outfits in a session use the same theme.
- `beforeDrawing` mode: theme is known before drawing starts.
- `afterDrawing` mode: theme is selected/persisted but only revealed after drawing completes.
- `playerWritten` mode:
  - each player submits a theme at game start
  - submitted themes are revealed to players as described in the GDD
- `randomVoting` mode:
  - game presents a random subset of candidate themes
  - players vote on which theme(s) to use, per configured flow

### Out of scope
- Lobby settings UI itself
- Drawing implementation
- Outfit building

### Implementation notes
- Keep theme-source acquisition separate from theme-announcement timing.
- Make the theme-selection result durable in game state so later phases can rely on it.

### Acceptance criteria
- The selected theme source drives the correct pre-game or pre-drawing behavior.
- Player-written themes are captured and revealed correctly.
- Before-drawing and after-drawing announcement timing both work.
- Final theme selection is persisted in game state.

### Test cases
- One test per theme source.
- Announcement timing tests.
- Same-theme-for-both-outfits test.

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
- Default clothing order: hat, shirt, pants, shoes
- Support custom clothing-type lists from config
- Players may draw multiple items per clothing type up to the configured max
- A player may draw zero items in a round
- Timer expiration advances the game to the next clothing type
- Drawings should be preserved on timeout/disconnect where practical

### Out of scope
- Shared pool reveal
- Outfit building
- Customization overlays

### Implementation notes
- Reuse `SvgDrawingCanvas` rather than inventing a new drawing system.
- Persist creator attribution on every submitted drawing.
- Keep drawing hidden from other players during this phase.

### Acceptance criteria
- Players can submit multiple drawings per clothing type.
- Drawings are persisted and attributed to their creator.
- Timer expiration advances rounds correctly.
- End of drawing phase yields a usable set of drawings for pool generation.

### Test cases
- Timer progression tests.
- Max-items-per-type tests.
- “Player drew nothing” tests.
- Disconnect/autosave preservation tests where practical.

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
- Reveal screen
- Ready tracking
- Auto-advance countdown
- Claim prevention during reveal

### Required behavior
- All players see the same pool contents.
- Reveal is view-only.
- Players may browse the pool and press Ready.
- If all players are ready, advance immediately.
- If not all players are ready, auto-advance when the countdown expires.

### Out of scope
- Real-time claiming logic
- Outfit assembly rules

### Implementation notes
- Preserve item identity from drawing submission into the pool.
- Ready tracking should be authoritative and visible to clients.
- No claims or reservations may occur during this phase.

### Acceptance criteria
- All players see the same revealed pool.
- Ready count updates correctly.
- Reveal auto-advances on timeout if necessary.
- Reveal does not allow claims.

### Test cases
- Pool aggregation tests.
- Ready tracking tests.
- Auto-advance timeout tests.
- View-only enforcement tests.

### Dependencies / sequencing
- Depends on #54.

---

## #56 — 1.8 Implement real-time shared-pool claiming for outfit building (DONE)

### Summary
Implement the server-authoritative outfit-building rules where players race to claim items from a shared pool.

### Goals
- Support simultaneous real-time claiming.
- Enforce “first valid claim wins.”
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
- First valid claim wins based on server-authoritative ordering.
- Conflicting claim attempts fail cleanly.
- Players may replace selections before lock-in, subject to pool rules.
- Each outfit must contain exactly one item for each required slot/type.
- Timeout auto-fill must avoid self-drawn items when possible.
- If auto-fill cannot avoid self-drawn items, follow the GDD’s failure/intervention behavior.

### Out of scope
- Final outfit-builder UI polish
- Customization flow
- Outfit 2 distinctness validation

### Implementation notes
- Keep claim resolution deterministic and testable.
- Separate claim ownership from UI state.
- Broadcast item removal/availability updates to all players.

### Acceptance criteria
- Simultaneous claim conflicts resolve deterministically.
- Players cannot claim items they created.
- Players can complete and lock an outfit.
- Timeout auto-submit works for complete and incomplete outfits.

### Test cases
- Concurrent claim conflict tests.
- Self-drawn-item rejection tests.
- Lock/unlock/replace tests.
- Auto-fill behavior tests.

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
- Player view must show:
  - pool grid
  - outfit slots
  - timer
  - selection status
  - lock-in action
- Host view must show:
  - which players have locked in / completed
- UI must clearly communicate:
  - unavailable items
  - claimed-by-other failure states
  - valid current selections

### Out of scope
- Core claim engine logic
- Customization
- Voting

### Implementation notes
- Treat issue #56 as the source of truth for rules.
- This issue should focus on rendering and interaction, not redefining claim mechanics.

### Acceptance criteria
- Players can clearly see available items and selected slots.
- Claimed/conflicted items display clear visual feedback.
- Host can track completion progress.
- Layout works across desktop, tablet, and mobile form factors.

### Test cases
- Basic interaction tests for selection and lock-in.
- Visual-state tests for claimed/unavailable items.
- Responsive layout checks.

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
- Submission tracking
- Required-sketch behavior when enabled by config

### Required behavior
- Outfit name is required.
- Sketch overlay is optional by default.
- If `sketchingRequired` is enabled, enforce timing and completion rules.
- Persist final outfit submission as:
  - selected items
  - outfit name
  - optional sketch/customization layer

### Out of scope
- Outfit selection
- Outfit 2 distinctness rules
- Voting

### Implementation notes
- Reuse established drawing patterns/components where possible.
- Keep sketch overlay data separate from the original clothing-item drawings.

### Acceptance criteria
- Players can name each outfit.
- Players can optionally sketch over their assembled outfit.
- Submissions persist items + name + sketch.
- Timing and required-sketch rules are enforced when enabled.

### Test cases
- Name-required validation tests.
- Optional vs required sketching tests.
- Submission persistence tests.

### Dependencies / sequencing
- Depends on #54 and #56.

---

## #59 — 1.11 Implement Outfit 2 pool reset and distinctness validation (DONE)

### Summary
Implement Outfit 2 setup, pool-reset behavior, reuse rules, and distinctness validation against all Outfit 1 submissions.

### Goals
- Correctly construct the Outfit 2 pool.
- Enforce the GDD’s reuse and distinctness rules.
- Reject invalid Outfit 2 submissions with actionable feedback.

### In scope
- Generate Outfit 2 pool from config rules
- Enforce reuse rules
- Validate distinctness against all Outfit 1s
- Reject/rebuild invalid Outfit 2 submissions
- Support timeout/autofix behavior where defined

### Required behavior
- Outfit 2 pool defaults to an exact copy of the Outfit 1 pool minus Outfit 1 picks, per GDD.
- Respect `canReuseOutfit1Items`.
- Respect `outfitDistinctnessRule`, but only when applicable per the GDD.
- Validate Outfit 2 against every player’s Outfit 1, including the submitting player’s own Outfit 1.
- If Outfit 2 matches any Outfit 1 in 3+ items under the default rule, reject it and return actionable feedback.
- If timeout handling requires automatic repair/swaps, do so according to the GDD.

### Suggested supporting service
- `OutfitDistinctnessEvaluator`

### Out of scope
- Voting
- Scoring
- Final results

### Acceptance criteria
- Outfit 2 pool generation is correct.
- Reuse settings are respected.
- Invalid Outfit 2 submissions are rejected with clear feedback.
- Validation behavior is covered by unit tests, including edge cases.

### Test cases
- Distinctness pass/fail tests.
- Self-comparison and other-player comparison tests.
- Config precedence tests for reuse vs distinctness.
- Timeout/autofix tests.

### Dependencies / sequencing
- Depends on #55, #56, #58.

---

## #60 — 1.12 Implement tournament pairing and voting-round generation (DONE)

### Summary
Create the tournament engine that turns submitted outfits into voting rounds and matchups.

### Goals
- Register outfit submissions as tournament entrants.
- Generate deterministic, testable round structures.
- Support Swiss pairing as the primary format.

### In scope
- Register outfits as entrants
- Calculate round count
- Generate matchups
- Enforce creator-voting exclusions in eligibility inputs
- Support Swiss pairing as primary format
- Leave extension points for alternative formats

### Suggested supporting services
- `SwissTournamentService`
- `VotingEligibilityService`

### Required behavior
- Default round count should follow the GDD’s auto-calculated logic.
- Pairings should be based on cumulative points/records after round 1.
- Tournament state must be stored in a form the FSM can consume.
- Avoid self-matchups and, where practical, avoid a player’s two outfits facing each other unless unavoidable.

### Out of scope
- Voting UI
- Scoring logic
- Coin flip resolution UI

### Acceptance criteria
- Round count is generated from outfit count/config.
- Pairings are deterministic and testable.
- Self-voting restrictions are reflected in eligibility logic.
- Tournament state is persisted in a form consumable by the FSM.

### Test cases
- Round-count calculation tests.
- Swiss pairing fixture tests.
- Eligibility exclusion tests.

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
- Visibility modes:
  - fully hidden
  - percentages shown
  - individual votes shown
  - live voting scaffold if practical

### Required behavior
- Non-creators can vote on all required criteria.
- Creators cannot vote on their own outfits unless config later explicitly allows it.
- Users cannot submit until all required criterion choices are made.
- Missing/late votes follow the GDD’s “not counted” behavior.

### Out of scope
- Score computation
- Coin flip logic itself
- Final leaderboard

### Implementation notes
- Keep vote capture independent from score calculation.
- Persist votes at the matchup + criterion + voter level.

### Acceptance criteria
- Non-creators can vote on all required criteria.
- Creators cannot vote on their own outfits.
- Votes are stored per matchup and criterion.
- Incomplete submissions are blocked.

### Test cases
- Voting eligibility tests.
- Submit validation tests.
- Missing/late vote handling tests.
- Visibility-mode behavior tests where practical.

### Dependencies / sequencing
- Depends on #60.

---

## #62 — 1.14 Implement scoring engine, weighted criteria, and bonuses

### Summary
Implement the scoring rules from the GDD exactly.

### Goals
- Convert persisted votes into criterion scores, matchup totals, round bonuses, and final player totals.
- Preserve enough metadata for leaderboard tiebreakers.

### In scope
- Points per vote
- Weighted criteria
- Tied-criterion bonus via coin flip integration
- Round leader bonus
- Tournament winner bonus
- Player score rollups across both outfits
- Matchup-win tracking for tiebreakers

### Suggested supporting service
- `DrawnToDressScoringService`

### Required behavior
- Each vote for an outfit earns 1 point times criterion weight.
- Both outfits may score in a matchup.
- Tied criteria trigger coin-flip bonus handling.
- Round-leader and tournament-winner bonuses must be configurable.
- Player totals must roll up both outfits’ accumulated points.
- Persist matchup-win counts for final tiebreaking.

### Out of scope
- Voting UI
- Final leaderboard UI

### Acceptance criteria
- Scoring outputs match fixture scenarios from the GDD.
- Both outfits accrue points independently.
- Player totals roll up correctly.
- Matchup-win tiebreaker data is persisted for final results.

### Test cases
- Exact GDD example fixtures.
- Weighted-criteria tests.
- Bonus-point tests.
- Player rollup tests.

### Dependencies / sequencing
- Depends on #60, #61, #63 if coin flip integration is hard-linked.

---

## #63 — 1.15 Implement coin flip tie-break workflow and UI

### Summary
Implement the coin-flip workflow used for tied criteria and final winner resolution.

### Goals
- Provide a reusable, server-authoritative tie-break mechanism.
- Support both matchup-level and final-standings tie resolution.

### In scope
- Select caller
- 15-second timer
- Heads/tails choice
- Timeout auto-selection
- Random result generation
- All-player result display
- Reuse workflow for final standings tie-break

### Suggested supporting service
- `CoinFlipService`

### Required behavior
- Caller selection must be deterministic/randomized per the intended rules.
- Timer must be server-authoritative.
- If the caller does not respond in time, the system auto-selects on their behalf.
- Result and winner must be broadcast to all players.
- Same workflow must support criterion ties and final leaderboard ties.

### Out of scope
- Score computation beyond exposing result data
- Final leaderboard UI itself

### Acceptance criteria
- Criterion ties can resolve through a coin flip.
- Final-standings ties can also use the same flow.
- Timeout handling is server-authoritative.
- UI clearly communicates caller, timer, result, and winner.

### Test cases
- Timeout auto-selection tests.
- Result-generation tests.
- Caller-selection tests.
- Integration tests for criterion tie vs final tie usage.

### Dependencies / sequencing
- Can proceed in parallel with #61/#62 if contracts are agreed.

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
- Optional detailed breakdown
- Play-again / return-to-menu actions

### Suggested supporting service
- `LeaderboardService`

### Required behavior
- Rank players by total points descending.
- If tied, apply matchup-wins tiebreaker.
- If still tied, use coin-flip winner.
- Show the winner and, if applicable, the tiebreaker path used.

### Out of scope
- Scoring engine logic itself
- Earlier gameplay phases

### Acceptance criteria
- Leaderboard reflects final scoring accurately.
- Winner and tiebreaker path are clearly displayed.
- Detailed breakdown can explain score accumulation.
- End-of-game actions are wired correctly.

### Test cases
- Ranking tests.
- Tiebreak-display tests.
- End-of-game action tests.

### Dependencies / sequencing
- Depends on #62 and #63.

---

## #65 — 1.17 Implement reconnect, pause, and host disconnect handling

### Summary
Implement resilience behavior for disconnects and recovery across the game lifecycle.

### Goals
- Preserve sensible behavior when players or the host disconnect.
- Allow resume where the GDD expects it.
- Abandon the game when host timeout rules are exceeded.

### In scope
- Reconnect support for drawing, reveal, building, customization, and voting
- Host disconnect pause behavior
- Host reconnect resume behavior
- Host timeout abandon behavior
- Inactive-player handling mid-game
- Persistence/restoration of in-progress phase state

### Required behavior
- Player reconnects should restore the relevant phase context where practical.
- Host disconnect pauses the game.
- Host reconnect resumes the game.
- Host timeout transitions to abandoned state after configured timeout.
- Mid-game inactive players should be handled according to the GDD.

### Out of scope
- Infrastructure beyond what Drawn To Dress needs
- Non-game-wide generic reconnection framework redesign unless necessary

### Acceptance criteria
- Player reconnect behavior works sensibly for each major phase.
- Host disconnect pauses the game.
- Host reconnect resumes play.
- Host timeout abandons the game.
- Critical disconnect/reconnect flows are covered by tests.

### Test cases
- Per-phase reconnect tests.
- Pause/resume tests.
- Host-timeout-to-abandoned tests.

### Dependencies / sequencing
- Best after major gameplay phases exist.

---

## #66 — 1.18 Add unit tests for Drawn To Dress FSM states and services

### Summary
Add focused, deterministic tests for FSM states and supporting services.

### Goals
- Ensure each major state and core rules engine is covered.
- Capture key GDD edge cases as regression tests.

### In scope
- State `OnEnter` tests
- State `HandleCommand` tests
- Timed `Tick` tests
- Distinctness validation tests
- Claim conflict tests
- Pairing tests
- Scoring tests
- Coin flip tests
- Disconnect/reconnect tests where practical

### Acceptance criteria
- Each major FSM state has direct test coverage.
- Core rule engines are covered with deterministic fixtures.
- Important GDD edge cases are represented as regression tests.

### Dependencies / sequencing
- Depends on feature issues being implemented.

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
- Core flows should be usable on mobile, tablet, and desktop.
- Controls should meet touch-friendly sizing guidance.
- Timers and critical state indicators should remain visible.
- Drawing, claiming, and voting interactions should feel responsive.

### Acceptance criteria
- Primary game flows are usable across target form factors.
- Controls meet touch-friendly minimum sizing.
- Keyboard/screen-reader accessibility is improved where feasible.
- No obvious lag/regressions in drawing, claiming, or voting.

### Dependencies / sequencing
- Final pass issue; should come after major feature work.

---

## Best next step

If you want, I can do one of these next:

1. **Rewrite all 19 sub-issues into final GitHub-ready markdown** in one consolidated response, or  
2. **Prioritize only the most important issues** (#49–#59 first), or  
3. **Convert these into exact issue-body replacements** with copy/paste-ready markdown for each issue number.

If you want option 3, I’ll format each one as a clean GitHub issue body block so you can paste them directly.