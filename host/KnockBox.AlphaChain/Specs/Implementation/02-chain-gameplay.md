# Milestone 2 — Chain Gameplay

## Goal

Wire up real word play. This milestone adds dictionary-backed validation, the Shiritori succession rule, uniqueness enforcement, a working shot-clock with turn-timeout consequences, banned-letter selection at game start, the Zero-Point Tax, baseline (length-only) scoring, and Survival-mode elimination.

## Demonstrable outcome

- A 2–4 player match plays end to end with real words. Players type a word, the chain rule (last letter of prior word → first letter of next) is enforced, duplicates rejected, and dictionary misses rejected.
- The shot clock counts down visibly; running it out either zeros the player's turn (default) or eliminates them (Survival mode).
- A banned letter is shown at the top of the game; using it as any letter inside the word yields 0 points but keeps the chain alive; using it as the **last** letter clears the required start letter for the next player.
- A live leaderboard reflects baseline length-only scores.

## New / changed files

### Word list

- `Services/Logic/WordList/IAlphaChainWordList.cs`:
  - `bool IsValidWord(ReadOnlySpan<char> word);`
  - This is the swappable boundary. The M2 implementation is a small local CSV; the end-goal implementation is a Spardle-style in-process word list (likely a larger dataset and/or shared lookup structure). Keeping the contract sync + span-based matches Spardle's existing `WordListService` shape and avoids forcing callers into `await` for what is always an in-memory check.
- `Services/Logic/WordList/AlphaChainWordList.cs`:
  - Port of `host/KnockBox.Spardle/Services/WordListService.cs` (cannot reference Spardle directly — re-implement inside this plugin).
  - Loads a CSV at construction. Source path resolves via `IPluginStorage` (preferred) or embedded resource fallback.
  - Builds a `HashSet<string>` (or per-length `byte[][]` pools if memory becomes a concern) for O(1) lookup.
- `wwwroot/dictionary.csv` — dictionary asset.
  - Decide between NY-Times-style (~5k common) vs. full (~250k); milestone defaults to NY-style to keep plugin staging small. Document the chosen source + license.
- `AlphaChainModule.cs` — add `registration.AddSingleton<IAlphaChainWordList, AlphaChainWordList>();`.

### Commands and results

- `Services/Logic/Games/FSM/SubmitWordCommand.cs` — `record SubmitWordCommand(string ActorUserId, string WordRaw) : AlphaChainCommand`.
- `Services/Logic/Games/FSM/SubmitWordResult.cs` — discriminated result for the UI:
  - `Accepted(int Score)`
  - `AcceptedZeroPointTax` (banned letter present)
  - `RejectedNotYourTurn`
  - `RejectedChainBroken(char Required)`
  - `RejectedNotInDictionary`
  - `RejectedDuplicate`
  - `RejectedEmpty`

### State changes

- `Services/State/Games/AlphaChainGameState.cs` — add:
  - `HashSet<string> PlayedWords` (case-insensitive comparer).
  - `string? LastWord`.
  - `char? RequiredStartLetter` (null at game start, after a banned-letter-as-last-letter, and after a Pivot — Pivot lands in M3).
  - `char? BannedLetter`.
  - Method `ResetTurnTimer(DateTimeOffset now)` (sets `PhaseEndTime = now + Config.ShotClockSeconds`).
- `Services/State/Games/Data/AlphaChainPlayerState.cs` — add `int TurnTimeouts`. `IsEliminated` already exists from M1.

### FSM behavior

- `Services/Logic/Games/FSM/States/SetupState.cs` — pick `BannedLetter`:
  - If `Config.BanMode == Vowels`: random from `{ A, E, I, O, U }`.
  - If `Config.BanMode == Consonants`: random from the 21 consonants.
  - If `Config.BanMode == All`: random from all 26 letters.
  - Uses an injected `IRandomNumberService` (existing in core — see Codeword's DI).
  - Then transitions to `RoundState` with `RequiredStartLetter = null` (first player has free choice).
- `Services/Logic/Games/FSM/States/RoundState.cs` — handle `SubmitWordCommand`:
  1. Validate actor is `TurnManager.CurrentPlayerId`; else return `RejectedNotYourTurn`.
  2. Normalize: trim, lower-case.
  3. Empty → `RejectedEmpty`.
  4. If `RequiredStartLetter != null` and `word[0] != RequiredStartLetter` → `RejectedChainBroken`.
  5. If `PlayedWords.Contains(word)` → `RejectedDuplicate`.
  6. If `!wordList.IsValidWord(word)` → `RejectedNotInDictionary`.
  7. Compute `containsBanned = BannedLetter is { } b && word.Contains(b)`.
  8. `score = containsBanned ? 0 : word.Length;` (scoring pipeline lands in M3 — for now length only).
  9. Append to `PlayedWords`, set `LastWord = word`. If `BannedLetter == word[^1]` set `RequiredStartLetter = null`, else `RequiredStartLetter = word[^1]`.
  10. Add score to player's `Score`.
  11. Advance turn; if turn order wrapped, increment `CurrentRound`. If round count exhausts era×interval, transition to `GameOverState` (intermission still lands in M4).
  12. Reset `PhaseEndTime`.
  13. Return `Accepted` or `AcceptedZeroPointTax`.
- `Services/Logic/Games/FSM/States/RoundState.cs` — `TickAsync`:
  - If `now < state.PhaseEndTime`, return.
  - On timeout:
    - Survival mode: mark current player `IsEliminated = true`. If active (non-eliminated, non-left) count drops below 2, transition to `GameOverState`.
    - Non-survival: add 0 to score; increment `TurnTimeouts`.
  - Advance turn (skip eliminated/left players); reset `PhaseEndTime`.

### Engine

- `Services/Logic/Games/AlphaChainGameEngine.cs`:
  - Inject `IRandomNumberService` (forward to `AlphaChainGameContext` so FSM can use it deterministically in tests).
  - Subscribe a periodic tick driver — see how `KnockBox.HiddenAgenda` drives `Tick(state, now)` (typically a hosted service or `ITickService`); follow that exact pattern. If no host-side ticker is available to plugins, drive ticks from the Razor page's `ITickService` subscription via a `TickCommand`.

### UI

- `Pages/AlphaChainGame.razor` — add:
  - Big banner with `RequiredStartLetter` (or "Any letter" when null) and `BannedLetter`.
  - Text input bound to a local field; submit on Enter; disabled when it is not your turn.
  - Live shot-clock countdown derived from `PhaseEndTime` via `ITickService`.
  - Submitted-words log (timestamp, player, word, score, "zero-point tax" flag).
  - Live leaderboard ordered by `Score` desc; mark eliminated players visually.
  - Inline rejection feedback for the last `SubmitWordResult`.
- `Pages/AlphaChainGame.razor.cs` — debounce/disable input during in-flight submission; clear input on accept.
- `Pages/AlphaChainGame.razor.css` — basic layout, banner styling, leaderboard.

### Tests

- `Unit/Logic/WordList/AlphaChainWordListTests.cs` — known-good / known-bad words; punctuation rejected.
- `Unit/Logic/Games/AlphaChain/States/RoundStateTests.cs`:
  - Each rejection reason returns the right `SubmitWordResult`.
  - Banned letter inside word → `AcceptedZeroPointTax`, score = 0, chain continues.
  - Banned letter as last char → `RequiredStartLetter` becomes null on next turn.
  - Duplicate detection is case-insensitive.
  - Turn timeout in survival mode eliminates the player.
  - Turn timeout in non-survival mode keeps the player and gives 0 points.
  - Survival mode: game ends when only 1 active player remains.
- `Unit/Logic/Games/AlphaChain/AlphaChainGameEnginePlayerLeftTests.cs` — leaving during your turn auto-advances and does not eliminate (unless survival mode).

## Key types & contracts

- `AlphaChainWordList` must be **fully loaded before** the first `IsValidWord` call — initialize eagerly in the constructor, or expose `Task EnsureLoadedAsync()` and call it during `StartAsyncCore`.
- All FSM transitions and state mutations remain inside `state.Execute`/`ExecuteAsync`. The `SubmitWordResult` returned to the page is computed inside the lock and returned via the engine's command dispatcher.

## Step-by-step build order

1. Build and unit-test `AlphaChainWordList` in isolation.
2. Add `SubmitWordCommand` + `SubmitWordResult`.
3. Extend `AlphaChainGameState` with the new fields.
4. Extend `SetupState` to pick a banned letter.
5. Extend `RoundState.HandleCommandAsync` with the 13-step submission flow above.
6. Add the timeout branch to `RoundState.TickAsync`.
7. Wire UI (input, banners, leaderboard, countdown).
8. Tests; manual end-to-end smoke test with 2 browsers.

## Risks & notes

- **Dictionary file size:** A multi-MB CSV inside `wwwroot/` increases plugin staging time and memory. Mitigation: ship the smaller NY-Times-style word list by default; document how to swap a larger one via `{KNOCKBOX_DATA_ROOT}/plugins/alpha-chain/dictionary.csv`.
- **Tick source:** Plugins do not own a hosted background service. Either rely on `ITickService` from the Razor circuit (problematic if every viewer fires ticks — gate to one designated tick driver, e.g., the host), or extend `AbstractGameEngine` with a server-side scheduler. Pick the simplest viable option and document it.
- **Banned letter on first turn:** GDD does not say if the first word can include the banned letter. Default: yes, but it incurs the Zero-Point Tax. Document this in the milestone close-out notes.
- **Casing & accents:** Normalize input to ASCII-lowercase; reject non-letters. The Spardle CSV is already ASCII; if a different dictionary is used, define the normalization rules here.
- **Swappable dictionary boundary:** `IAlphaChainWordList` is the seam where the dictionary backend can change. The M2 implementation is a local CSV; the end-goal is a Spardle-style in-process word list. Both are sync + span-based, so the contract stays sync + span-based — do not introduce `async`/`Task` to "future-proof" for an API backend, because the planned backend is not an API.
