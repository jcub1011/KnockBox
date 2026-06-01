# Milestone 2 — Chain Gameplay

## Goal

Wire up real word play. This milestone adds dictionary-backed validation, the Shiritori succession rule, uniqueness enforcement, a working shot-clock with turn-timeout consequences, banned-letter selection at game start, the Zero-Point Tax, baseline (length-only) scoring, and Survival-mode elimination.

## Demonstrable outcome

- A 2–4 player match plays end to end with real words. Players type a word, the chain rule (last letter of prior word → first letter of next) is enforced, duplicates rejected, and dictionary misses rejected.
- The shot clock counts down visibly; running it out either zeros the player's turn (default) or eliminates them (Survival mode).
- A banned letter is shown at the top of the game; using it as any letter inside the word yields 0 points but keeps the chain alive; using it as the **last** letter clears the required start letter for the next player.
- A live leaderboard reflects baseline length-only scores.

## New / changed files

### Word validation — reuse the `KnockBox.WordService` library plugin

Do **not** port or re-implement a word list, and do **not** ship a `dictionary.csv`. The repo already
provides dictionary validation through the `KnockBox.WordService` **library plugin** (the sanctioned
cross-plugin reuse mechanism); `KnockBox.Spardle` consumes it today. Alpha Chain consumes the same
contract.

- `host/KnockBox.AlphaChain/KnockBox.AlphaChain.csproj` — add a compile-time reference to the contracts
  assembly: `<ProjectReference Include="..\KnockBox.WordService.Contracts\KnockBox.WordService.Contracts.csproj" />`
  (mirror `KnockBox.Spardle/KnockBox.Spardle.csproj`). The runtime `IWordListService` implementation lives
  in the `KnockBox.WordService` library plugin, which the host loads **before** any game plugin, so no
  host wiring is needed.
- `Services/Logic/Games/AlphaChainGameEngine.cs` — inject `IWordListService` in the constructor (as
  `SpardleEngine` does) and forward it onto `AlphaChainGameContext` so the FSM can validate words
  deterministically in tests (mock `IWordListService`).
- Validation call: `wordList.IsValidWord(word)` — `IWordListService.IsValidWord(ReadOnlySpan<char>)` is
  already sync + span-based (case-insensitive; non-ASCII → false), exactly the contract this milestone
  needs. Use `WordPoolMode.FullDictionary` for broad Shiritori coverage (document the choice; it can be
  promoted to a setting later if play-testing shows it's too permissive/restrictive).
- No `AlphaChainModule` registration for word lists — the library plugin already registers
  `IWordListService` as a singleton.

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
  1. Validate actor is `TurnManager.CurrentPlayer`; else return `RejectedNotYourTurn`.
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
- `Services/Logic/Games/FSM/States/RoundState.cs` — `Tick`:
  - If `now < state.PhaseEndTime`, return.
  - On timeout:
    - Survival mode: mark current player `IsEliminated = true`. If active (non-eliminated, non-left) count drops below 2, transition to `GameOverState`.
    - Non-survival: add 0 to score; increment `TurnTimeouts`.
  - Advance turn (skip eliminated/left players); reset `PhaseEndTime`.

### Engine

- `Services/Logic/Games/AlphaChainGameEngine.cs`:
  - Inject `IRandomNumberService` (forward to `AlphaChainGameContext` so FSM can use it deterministically in tests).
  - Drive the shot clock via `ITickService`, which is platform-wide and **already injected by
    `LobbyPageBase<>`** (`[Inject] protected ITickService TickService`) — it is not a HiddenAgenda-specific
    pattern. Register a tick callback with `TickService.RegisterTickCallback(...)` and dispose the returned
    `IDisposable`. To avoid every viewer firing the engine `Tick`, gate it to a single designated driver
    (e.g. the host/shared-display circuit) and document the choice.

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

- No dedicated word-list test — validation is owned by the `KnockBox.WordService` library. In
  `RoundStateTests` inject a `Mock<IWordListService>` to exercise the `RejectedNotInDictionary` path
  (return false) and the accept path (return true).
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

- `IWordListService` is provided by the host's DI container (registered by the `KnockBox.WordService`
  library plugin, loaded before any game) and is ready to use as soon as it is injected — no eager-load
  step is required in this plugin.
- All FSM transitions and state mutations remain inside `state.Execute`/`ExecuteAsync`. The `SubmitWordResult` returned to the page is computed inside the lock and returned via the engine's command dispatcher.

## Step-by-step build order

1. Add the `KnockBox.WordService.Contracts` project reference and inject `IWordListService` into the engine/context.
2. Add `SubmitWordCommand` + `SubmitWordResult`.
3. Extend `AlphaChainGameState` with the new fields.
4. Extend `SetupState` to pick a banned letter.
5. Extend `RoundState.HandleCommand` with the 13-step submission flow above.
6. Add the timeout branch to `RoundState.Tick`.
7. Wire UI (input, banners, leaderboard, countdown).
8. Tests; manual end-to-end smoke test with 2 browsers.

## Risks & notes

- **No dictionary asset to ship:** word data lives in the `KnockBox.WordService` library, so there is no
  CSV in this plugin's `wwwroot/` and no staging/memory cost here. The only knob is the chosen
  `WordPoolMode` (`NytStandard` / `ReducedDictionary` / `FullDictionary`).
- **Tick source:** use `ITickService` (already injected by `LobbyPageBase<>`). Gate ticks to one
  designated driver so multiple viewers don't each fire the engine `Tick`. Document the chosen driver.
- **Banned letter on first turn (RULE):** the first word **may** contain the banned letter, but doing so
  incurs the Zero-Point Tax (score 0). This is a stated rule, not an open question.
- **Casing & accents:** `IWordListService.IsValidWord` is case-insensitive and treats non-ASCII as
  invalid. Still normalize submitted input to trimmed lower-case before chain/uniqueness checks; reject
  non-letters.
- **Swappable dictionary boundary:** the seam is now `IWordListService` itself (an interface owned by the
  library contracts). It is sync + span-based; do not introduce `async`/`Task` — the backend is an
  in-process pool, not an API.
