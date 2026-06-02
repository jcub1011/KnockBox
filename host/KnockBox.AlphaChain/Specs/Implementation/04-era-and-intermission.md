# Milestone 4 — Era & Intermission

## Goal

Wire up the 4-round Era loop. After every `Config.EraInterval` rounds, the game transitions from `RoundState` to a new `IntermissionState` that orchestrates four sub-phases — **Deal → Expansion → Optimization → Sniper Ban** — and then returns to `RoundState` for the next Era. After `Config.EraCount` eras complete, the game enters `GameOverState` and resolves win conditions.

## Demonstrable outcome

- A 4-player match plays Era 1 with baseline scoring (no cards).
- After round 4, an Intermission overlay appears for all players:
  1. **Deal:** each player is privately dealt 3 modifiers + 2 actions.
  2. **Expansion:** every player's modifier slot count increases by 1.
  3. **Optimization:** each player privately reorders their Engine Bay within a countdown; opponents' bays are hidden.
  4. **Sniper Ban:** the player currently in last place picks the banned letter for the next Era from the legal pool.
- Era 2+ rounds resume with cards active and the new banned letter.
- After the last era's final round, `GameOverState` shows the rankings, winner, and total words played.

## New / changed files

### Domain

- `Services/Logic/Games/Data/IntermissionSubPhase.cs` — `enum { Deal, Expansion, Optimization, SniperBan, Complete }`.
- `Services/Logic/Games/Data/OptimizationSubmission.cs` — record `{ string UserId; IReadOnlyList<string> ModifierBayIds; bool Submitted; }`.
- `Services/Logic/Games/Data/GameResults.cs` — record `{ IReadOnlyList<PlayerResult> Rankings; string WinnerUserId; int TotalWordsPlayed; TimeSpan Duration; }`. `PlayerResult { string UserId; string DisplayName; int Score; bool Eliminated; int WordsPlayed; }`.

### State

- `Services/State/Games/AlphaChainGameState.cs` — add:
  - `IntermissionSubPhase IntermissionPhase`
  - `Dictionary<string, OptimizationSubmission> OptimizationSubmissions` (key: userId).
  - `string? SniperBanUserId` (chosen ban-picker; resolved at start of SniperBan sub-phase).
  - `DateTimeOffset SubPhaseEndTime` (separate from `PhaseEndTime` so the round timer doesn't conflict with the intermission timer).

### Commands

- `Services/Logic/Games/FSM/SubmitOptimizationCommand.cs` — `record(string ActorUserId, IReadOnlyList<string> ModifierBayIds)`.
- `Services/Logic/Games/FSM/SelectSniperBanCommand.cs` — `record(string ActorUserId, char Letter)`.

### FSM behavior

- `Services/Logic/Games/FSM/States/RoundState`:
  - At the turn-order wrap point, apply the **canonical Era/round rule** defined in M1 (`01-foundation.md`):
    using `completedRound` (the pre-increment `CurrentRound`) and `LastScheduledRound = EraInterval × EraCount`,
    transition to `GameOverState` if `completedRound == LastScheduledRound`; else transition to
    `IntermissionState` if `completedRound % Config.EraInterval == 0`; otherwise increment `CurrentRound`
    and continue. This replaces the M1 round-only end check with the same overall bound plus Intermission
    insertion — do not introduce a second, differently-worded condition.
- `Services/Logic/Games/FSM/States/IntermissionState`:
  - `OnEnter`:
    - Set `Phase = Intermission` (via `SetPhase`), `IntermissionPhase = Deal`, `SubPhaseEndTime = now + 3s` (deal animation buffer).
    - Use injected `IRandomNumberService` to deal each non-eliminated player N modifiers + M actions. Weighted draws from `ModifierLibrary` and `ActionLibrary`. Counts come from the existing `AlphaChainSettings` fields `ModifiersDealtPerEra` (default 3) and `ActionsDealtPerEra` (default 2), defined in M1 — no new config is introduced here.
    - Append drawn modifiers to each player's `EngineBay` (append-to-right; player resequences in Optimization).
    - Append drawn actions to each player's `ActionHand`.
  - `Tick` drives the sub-phase progression deterministically:
    - **Deal → Expansion** when `now >= SubPhaseEndTime`. Expansion: `player.ModifierSlots += 1` for all active players. Then move to **Optimization** with `SubPhaseEndTime = now + Config.IntermissionCardSelectSeconds`. Initialize `OptimizationSubmissions` with `Submitted = false` and current bay order.
    - **Optimization → SniperBan** when either `now >= SubPhaseEndTime` OR `OptimizationSubmissions.Values.All(s => s.Submitted)`. For non-submitters: keep their current order (capped to `ModifierSlots`; excess discarded oldest-first or chosen by deterministic rule — document the call). Apply all submissions to the live bays. Then resolve `SniperBanUserId` = lowest-score active player (ties: earliest in `TurnManager.TurnOrder`). `SubPhaseEndTime = now + Config.SniperBanSeconds`.
    - **SniperBan → Complete** when either the eligible player issues `SelectSniperBanCommand` or `now >= SubPhaseEndTime`. On timeout, pick a random letter from the legal pool. Validate the chosen letter is legal under `Config.BanMode`; reject if not, no consumption of the timer; if rejected at timeout, fall back to random.
    - **Complete:** set `BannedLetter`, increment `CurrentEra`, then go back to `RoundState` with `RequiredStartLetter = null` and `PhaseEndTime` reset. (Per the canonical rule, `RoundState` ends the game on the final scheduled round, so an Intermission never runs after the last era; the `CurrentEra > Config.EraCount` → `GameOverState` check here is a defensive backstop only.)
  - `HandleCommand`:
    - `SubmitOptimizationCommand`: validate length ≤ `player.ModifierSlots`; all ids present in either current bay or just-dealt set; record submission. Do **not** mutate the live `EngineBay` yet (apply only when the sub-phase ends, so changes don't leak to opponents in case of UI snapshotting).
    - `SelectSniperBanCommand`: validate actor == `SniperBanUserId`, letter in the legal pool. Set `BannedLetter`, advance sub-phase to Complete.
- `Services/Logic/Games/FSM/States/GameOverState`:
  - Populate `state.Results` with rankings (highest score first; eliminated players ranked last in elimination order).
  - Survival mode: if game ended due to last-player-standing, winner is the survivor regardless of score.

### Engine

- `Services/Logic/Games/AlphaChainGameEngine`:
  - Subscribe `IRandomNumberService` to the FSM via `AlphaChainGameContext` so tests can deterministically inject a `Mock<IRandomNumberService>`.

### UI

- `Pages/AlphaChainGame.razor`:
  - Branch on `GameState.Phase`. For Intermission, render an overlay:
    - **Deal:** brief "Dealing cards…" animation with newly-drawn cards revealed at the bottom.
    - **Expansion:** "+1 Slot" badge animates onto each Engine Bay.
    - **Optimization:** show only the local player's Engine Bay (mutable, drag-or-click reorder) + a list of new cards waiting to be slotted; show waiting indicators for opponents (count of submitted vs. total). Countdown bar.
    - **SniperBan:** if `CurrentUser.Id == SniperBanUserId`, show a letter picker constrained to legal letters; otherwise show "Waiting for <name> to pick the next banned letter…". Countdown bar.
  - For `GameOver`, render `GameResults` (winner + leaderboard + words played).
- `Components/IntermissionDealAnimation.razor`, `Components/IntermissionOptimizationPanel.razor`, `Components/SniperBanPicker.razor`, `Components/GameOverPanel.razor` — split for clarity.

### Tests

- `Unit/Logic/Games/AlphaChain/States/IntermissionStateTests.cs`:
  - **Deal:** each player gets exactly `ModifiersDealtPerEra` modifiers + `ActionsDealtPerEra` actions; eliminated players skipped.
  - **Expansion:** each active player's `ModifierSlots` += 1.
  - **Optimization:**
    - Submitted ordering applied to live bay.
    - Non-submitter's default ordering preserves prior bay + appends new cards (or whatever the documented default).
    - Submission with invalid id rejected.
    - Submission with length > slots rejected.
  - **SniperBan:**
    - Last-place player picks the letter.
    - Tie-break: earliest turn-order index.
    - Timeout → random legal letter.
    - Illegal letter under `BanMode` rejected.
  - **Era progression:** exactly `EraCount − 1` Intermissions run (none after the final era); the game reaches `GameOverState` when the last scheduled round (`EraInterval × EraCount`) completes.
- `Unit/Logic/Games/AlphaChain/States/GameOverStateTests.cs`:
  - Rankings ordered by score desc.
  - Survival winner is the last survivor.
  - Ties broken deterministically (earliest in turn order).
- Integration-style test: 4-player, 2-era simulation with mocked clock and RNG runs end-to-end and produces sane results.

## Key types & contracts

- The Intermission state is the **only** writer of `BannedLetter` after `SetupState` (M2 sets the first one). Document the invariant.
- `OptimizationSubmission` is recorded but **not applied** to the live bay until sub-phase completion — this keeps opponents' visible state stable during the timer and supports the "fog-of-war" claim of the GDD.
- The Sniper Ban legal-letter pool is derived from `Config.BanMode`; ensure the picker UI hides illegal letters rather than relying on engine rejection.

## Step-by-step build order

1. Add the new enums and records (`IntermissionSubPhase`, `OptimizationSubmission`, `GameResults`).
2. Extend `AlphaChainGameState` with intermission fields.
3. Implement `IntermissionState.OnEnter` (Deal) and unit-test the deal counts.
4. Implement the sub-phase progression in `Tick` (Expansion → Optimization → SniperBan → Complete).
5. Implement the two new commands and their validation.
6. Update `RoundState` to transition into `IntermissionState` on era boundaries.
7. Build the Intermission UI components and wire them into `AlphaChainGame.razor`.
8. Implement `GameOverState` results population and the `GameOverPanel` UI.
9. Write tests; run the integration sim.

## Risks & notes

- **Optimization UX complexity:** Drag-reorder + countdown + fog-of-war is the densest UI in the milestone. Mitigation: ship a click-to-swap reorder in M4; defer real drag-reorder to M5 polish.
- **RNG determinism for tests:** Every random draw (deal, sniper-ban timeout fallback) must go through `IRandomNumberService`. Audit before merging.
- **Eliminated players in Sniper Ban:** GDD says "last place" picks. If the last-place player is eliminated, fall back to the next-lowest active player. Document the resolution rule in the milestone close-out.
- **Slot capacity vs. existing bay:** When `Expansion` increases slots, the player may now hold fewer cards than slots — they can leave the extra slot empty until they draw more. Optimization should accept under-filled bays. When dealt cards exceed remaining slots, the player must discard during Optimization; specify the discard UI affordance.
