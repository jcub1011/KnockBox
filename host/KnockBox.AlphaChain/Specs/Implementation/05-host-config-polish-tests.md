# Milestone 5 — Host Configuration UI, Polish & Tests

## Goal

Expose `AlphaChainSettings` to the host through a lobby configuration panel, replace placeholder visuals with the final look, and bring test coverage to release-grade. After this milestone the game ships.

## Demonstrable outcome

- The lobby shows a host-only configuration panel with controls for every GDD-specified knob (ban mode, shot clock, era interval, era count, survival mode, intermission timer, sniper-ban timer).
- Non-host players see the chosen configuration in read-only form so they know what they're signing up for.
- Validation prevents the host from starting with invalid settings (negative or zero values, era count < 1, etc.).
- The home-page tile no longer says "Coming soon" — it carries Alpha Chain's final art.
- The in-game header shows live Era / Round indicators.
- `dotnet test host/KnockBox.Host.slnx` passes with full coverage of the FSM, scoring, intermission, and player-left flows.
- Drag-reorder works for the Engine Bay (deferred from M3/M4).

## New / changed files

### Host configuration UI

- `Pages/AlphaChainLobby.razor` — add a host-only `<AlphaChainSettingsPanel>` section:
  - **Ban Mode** radio group: `Vowels Only` / `Consonants Only` / `All Bannable`.
  - **Shot Clock** slider, 5–20 s (default 12).
  - **Era Interval** numeric input, ≥1 (default 4).
  - **Era Count** numeric input, ≥1 (default 4).
  - **Survival Mode** toggle.
  - **Intermission Timer** numeric input, ≥5 s (default 30).
  - **Sniper Ban Timer** numeric input, ≥5 s (default 15).
  - Cards-per-era numeric inputs (`ModifiersDealtPerEra`, `ActionsDealtPerEra`) if those were left configurable in M4.
- `Components/AlphaChainSettingsPanel.razor` — the panel itself; two-way bound to a local `AlphaChainSettings` instance owned by the page.
- `Pages/AlphaChainLobby.razor.cs`:
  - On every change, validate via `config.Validate()`; gate "Start Session" until valid.
  - On Start, persist via `state.UpdateSettings(_ => config)` before calling `engine.StartAsync`.
  - Subscribe to `StateChangedEventManager` so non-host viewers re-render when the host edits the config.
- `Services/Logic/Games/Data/AlphaChainSettings.cs`:
  - Add `ConfigValidationResult Validate()` method enumerating violations.
  - All numeric ranges defined as `const` named constants (e.g., `MinShotClockSeconds`, `MaxShotClockSeconds`).

### Visual polish

- `Components/AlphaChainTile.razor` — replace "Coming soon" with the final game name + tagline + glyph. Add scoped CSS.
- `Components/AlphaChainHeader.razor` — extend with live `Era N / Round M` indicator when `GameState.Phase != Setup`.
- `Pages/AlphaChainGame.razor.css` — final layout, palette, responsive breakpoints.
- `wwwroot/img/` — SVG art for card categories (Anchor, Vowel Surge, etc.) and the tile glyph.
- `wwwroot/css/cards.css` — shared card visual styles.

### UX completion

- `Components/EngineBay.razor` — replace click-to-swap with HTML5 drag-and-drop (or a small JS-interop helper). Keep keyboard fallback (`Tab` + arrow keys to move) for accessibility.
- `Components/ActionHand.razor` — target picker becomes a proper modal for Time Thief; show countdown badge for queued Pivot/Amnesty.
- `Components/IntermissionOptimizationPanel.razor` — drag-reorder, discard affordance for over-capacity bays.
- Disable Engine Bay reorder during `Phase == Round` (locks in deterministic scoring).

### Tests

Add or expand under `host/KnockBox.AlphaChainTests/`:

- `Unit/Logic/Games/Data/AlphaChainSettingsTests.cs` — every validation rule has a positive and negative test.
- `Unit/Logic/Games/AlphaChain/States/RoundStateTests.cs` — expand from M2/M3 to cover:
  - Pivot + Banned Letter interactions.
  - Amnesty interacting with a word whose last char is banned (still clears `RequiredStartLetter`?).
  - Time Thief on a player who just submitted (clock already reset for next player).
- `Unit/Logic/Games/AlphaChain/States/IntermissionStateTests.cs` — full sub-phase coverage from M4.
- `Unit/Logic/Games/AlphaChain/States/GameOverStateTests.cs` — rankings, ties, survival winner.
- `Unit/Logic/Scoring/ScoreCalculatorTests.cs` — exhaustive coverage of every shipped modifier in `ModifierLibrary`.
- `Unit/Logic/Games/AlphaChain/AlphaChainGameEnginePlayerLeftTests.cs` — leaves during round / leaves during intermission optimization / leaves while holding Sniper Ban.
- `Integration/FullGameSimulationTests.cs` — drives a 4-player, 2-era, then a 6-player, 4-era game on a mocked clock + RNG. Asserts:
  - No exceptions.
  - Game ends in `GameOverState`.
  - Winner has the highest score (non-survival) or is the last survivor.
  - All words validated against the dictionary.
  - All cards consumed correctly.

### Documentation

- `Specs/alpha-chain-gdd.md` — append an "Implementation Deviations" section if the final mechanics differ from the GDD (e.g., starting hand, sniper-ban fallback rules, score cap).
- `Specs/Implementation/README.md` — short index pointing to the five milestone files and the GDD.

## Key types & contracts

- `AlphaChainSettings.Validate()` is the single source of truth for what's a legal config; both the UI and `StartAsyncCore` call it.
- No new public API on the SDK. All polish stays plugin-internal.

## Step-by-step build order

1. Add validation + constants to `AlphaChainSettings`.
2. Build `AlphaChainSettingsPanel` and integrate into the lobby; non-host read-only view first, then host editable.
3. Wire validation gating + persist on Start.
4. Replace tile + header art; finalize game-page CSS.
5. Implement drag-reorder for Engine Bay; verify keyboard accessibility.
6. Polish Intermission overlay (discard affordance, animations).
7. Expand test suite to cover everything noted above; add the integration simulation.
8. Run the analyzer (`KB1001–KB1004`); fix any flagged calls.
9. Manual matrix: 2 players survival on, 4 players survival off, 6 players default, 8 players non-default ban mode. Each through to GameOver in browser.
10. Update GDD with any deviations; add Implementation README.

## Risks & notes

- **Drag-and-drop fragility:** Blazor Server + HTML5 DnD can be flaky. Mitigation: ship the JS-interop helper behind a single small file; keep keyboard fallback as the primary code path.
- **Asset bloat:** SVG art and dictionary CSV inflate plugin staging. Mitigation: SVGs should be <5 KB each; dictionary stays at the M2 chosen size unless gameplay testing shows it's too restrictive.
- **Analyzer surprises:** `KB1001–KB1004` may flag any direct `System.IO` access introduced casually during development. Audit before release; route everything through `IPluginStorage`.
- **Non-host config visibility:** The lobby must re-render for non-hosts whenever the host edits a value. Test with two browsers open before declaring the panel done.

## Acceptance gate

- `dotnet build host/KnockBox.Host.slnx` → warning-free.
- `dotnet test host/KnockBox.Host.slnx` → all green.
- Roslyn analyzers KB1001–KB1004 → zero diagnostics on AlphaChain.
- Manual playthrough matrix above completed.
- `host/KnockBox.AlphaChain/Specs/alpha-chain-gdd.md` updated with any deviations.
