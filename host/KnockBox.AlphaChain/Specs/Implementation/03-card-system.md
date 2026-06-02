# Milestone 3 — Card System

> **Superseded (unified Modifier tier — no action or reaction cards).** This milestone first shipped
> hand-played *action* cards, then an auto-firing *reaction* hand. Both tiers are now **abolished**:
> every card is a persistent Engine Bay **Modifier**. `ReactionCard`/`ReactionLibrary`/`ReactionHand`/
> `ReactionResolver` and `ReactionsDealtPerEra` are removed; the offensive reactions are re-homed as
> automated modifiers (Flak Cannon, Scattershot, Bounty Hunter, Tracer Round, The Toll Booth) and a
> shield (The Titanium Mirror), all resolved in `Services/Logic/Games/FSM/EngineEffectResolver.cs`.
> The modifier/scoring half of this milestone is unchanged. See **`alpha-chain-gdd.md` §3** for the
> current catalogue. The text below is retained for historical context.

## Goal

Introduce Modifier cards (the Engine Bay) and Action cards plus the full scoring pipeline `Score = (L + ΣA) × ΠM` with conditional triggers. Cards do not yet enter play through Intermission (that lands in M4). Instead, M3 ships a host-only debug "Grant Cards" command so the pipeline and UI can be exercised in isolation.

## Demonstrable outcome

- Host clicks "Grant Cards" → each player receives a small starter set (e.g., 2 modifiers + 1 action).
- Players reorder modifiers in their Engine Bay before the round resumes; reordering is locked once the round is live.
- Submitted words are scored through the pipeline; conditional modifiers (e.g., **Vowel Surge**: ×2 if vowels > consonants) only contribute when their trigger fires.
- Players can play action cards:
  - **The Pivot** clears `RequiredStartLetter` for their next submission.
  - **Amnesty** suppresses the Zero-Point Tax for their next submission.
  - **Time Thief** reduces the opponent's `PhaseEndTime` by 5 seconds.

## New / changed files

### Card data model

- `Services/Logic/Games/Data/Cards/CardCategory.cs` — `enum { Modifier, Action }`.
- `Services/Logic/Games/Data/Cards/ModifierKind.cs` — `enum { Additive, Multiplicative }`.
- `Services/Logic/Games/Data/Cards/WordContext.cs` — small immutable record:
  - `string Word`
  - `int Length`
  - `int Vowels`
  - `int Consonants`
  - `char? BannedLetter`
  - `bool ContainsBannedLetter`
  - Built once per submission for trigger and value evaluation.
- `Services/Logic/Games/Data/Cards/ModifierCard.cs` — record:
  - `string Id` (stable; used to identify across the network)
  - `string Name`, `string Description`
  - `ModifierKind Kind`
  - `Func<WordContext, bool> Trigger` (always returns true for unconditional cards)
  - `Func<WordContext, double> Value` (additive bonus or multiplicative factor)
- `Services/Logic/Games/Data/Cards/ModifierLibrary.cs` — static immutable list. Initial set (GDD + filler for variety):
  - **The Anchor** — Additive, +12, no trigger.
  - **Consonant Crunch** — Additive, +2 × ctx.Consonants.
  - **Vowel Surge** — Multiplicative, ×2 when `ctx.Vowels > ctx.Consonants`.
  - **The Architect** — Multiplicative, ×1.5 when `ctx.Length >= 8`.
  - **Brick Layer** — Additive, +ctx.Length when `ctx.Length >= 6`.
  - **Sprinter** — Multiplicative, ×1.25 when `ctx.Length <= 4`.
  - **Letter Hoarder** — Additive, +1 per unique letter.
  - **Tax Collector** — Multiplicative, ×1.5 when `ctx.ContainsBannedLetter` (rewards risk).
- `Services/Logic/Games/Data/Cards/ActionCard.cs` — record:
  - `string Id`, `string Name`, `string Description`, `ActionKind Kind`.
  - `enum ActionKind { Pivot, Amnesty, TimeThief }`.
- `Services/Logic/Games/Data/Cards/ActionLibrary.cs` — static list with the three GDD actions; leave room for more.

### Scoring

- `Services/Logic/Scoring/IScoreCalculator.cs`:
  - `int Calculate(WordContext word, IReadOnlyList<ModifierCard> orderedBay);`
- `Services/Logic/Scoring/ScoreCalculator.cs`:
  - Iterate `orderedBay` left → right.
  - Maintain `double current = word.Length;`.
  - For each card whose `Trigger(word)` returns true:
    - If `Additive`: `current += card.Value(word);`
    - If `Multiplicative`: `current *= card.Value(word);`
  - Round half-up to int at the end.
  - Document the left-to-right intent: additives stack first (left), multiplicatives explode last (right). Players who place a × before a + lose value — that is by design (GDD).
- Register `IScoreCalculator` in `AlphaChainModule.cs` as a singleton.

### Per-player state extensions

- `Services/State/Games/Data/AlphaChainPlayerState.cs` — add:
  - `int ModifierSlots = 3` (default starting capacity; Intermission Expansion in M4 grows it).
  - `List<ModifierCard> EngineBay = new();` (ordered; bounded by `ModifierSlots`).
  - `List<ActionCard> ActionHand = new();`
  - `ActionKind? PendingAction = null;` (set when a Pivot/Amnesty is queued for the next submission).

### Commands

- `Services/Logic/Games/FSM/ReorderEngineBayCommand.cs` — `record(string ActorUserId, IReadOnlyList<string> CardIds)`. Only valid between rounds (M3 also allows during the round for testing; M4 locks it to Intermission).
- `Services/Logic/Games/FSM/PlayActionCommand.cs` — `record(string ActorUserId, string CardId, string? TargetUserId)`.
- `Services/Logic/Games/FSM/GrantCardsDebugCommand.cs` — host-only; deals random N modifiers + M actions to all players. Removed (or gated behind a debug flag) before release.

### FSM updates

- `Services/Logic/Games/FSM/States/RoundState.HandleCommand`:
  - For `SubmitWordCommand`:
    - Build `WordContext`.
    - If `PendingAction == Pivot`, set `RequiredStartLetter = null` (only for this submission), consume Pivot.
    - Compute `containsBanned`. If `PendingAction == Amnesty` and `containsBanned`, treat as non-banned (suppress tax), consume Amnesty.
    - Compute `baseScore = ScoreCalculator.Calculate(ctx, player.EngineBay)`.
    - If `containsBanned && PendingAction != Amnesty`, score = 0 (tax preserved).
    - Continue with the M2 flow.
  - For `PlayActionCommand`:
    - Validate possession.
    - Validate timing: Pivot/Amnesty can be queued only when it's your turn and you have not yet submitted; Time Thief can be played anytime against an opponent whose `PhaseEndTime` is still in the future.
    - Apply effect:
      - `Pivot`/`Amnesty` → set `PendingAction`.
      - `TimeThief` → mutate target's effective `PhaseEndTime` (since `PhaseEndTime` is on `state`, subtract 5 s from the global turn timer when the target is the current player; otherwise queue a debuff record applied next time the target is current).
    - Remove the card from `ActionHand`.
  - For `ReorderEngineBayCommand`:
    - Replace `player.EngineBay` with the new ordering. Reject if any card id is missing/duplicate or count exceeds `ModifierSlots`.

### UI

- `Components/EngineBay.razor`:
  - Ordered list of slot cards, click-to-swap reorder in M3 (drag-reorder deferred to M5 polish).
  - Reorder disabled when `GameState.Phase == Round` after M4 (in M3 keep it always enabled to ease testing).
- `Components/ActionHand.razor`:
  - Click a card → small target-picker for Time Thief; otherwise queue immediately.
- `Components/CardTooltip.razor` — shared hover/focus tooltip displaying name, description, and category.
- `Pages/AlphaChainGame.razor`:
  - Embed `EngineBay` (own player) and a smaller read-only Engine Bay summary for opponents (will become fog-of-war during Intermission Optimization in M4).
  - Embed `ActionHand`.
  - Host-only "Grant Cards (debug)" button.
  - Show "Pivot pending" / "Amnesty pending" badge near the input when queued.

### Tests

- `Unit/Logic/Scoring/ScoreCalculatorTests.cs`:
  - Empty bay → score == length.
  - Single additive → `L + value`.
  - Single multiplicative → `L × factor`.
  - Two additives then a multiplicative → `(L + a1 + a2) × m`.
  - Multiplicative before additive (suboptimal order) → still respects pipeline.
  - Conditional miss → card ignored.
  - Vowel Surge specific cases.
- `Unit/Logic/Games/AlphaChain/States/RoundStateActionsTests.cs`:
  - Pivot consumes itself on next submission and clears `RequiredStartLetter`.
  - Amnesty suppresses Zero-Point Tax exactly once.
  - Time Thief shrinks opponent's `PhaseEndTime` when they are the current player.
  - Time Thief on a non-current opponent queues a debuff for their next turn.
- `Unit/Logic/Games/AlphaChain/States/RoundStateReorderTests.cs`:
  - Valid reorder applied.
  - Reorder rejected when card id missing.
  - Reorder rejected when length > `ModifierSlots`.

## Key types & contracts

- `WordContext` must stay small and immutable so triggers can be evaluated cheaply. Do not capture state references inside trigger lambdas.
- `ModifierCard` and `ActionCard` are identified by stable `Id` strings — never by index. The UI sends `Id`s; the engine looks them up against the library.
- Scoring is deterministic: same `WordContext` + same ordered bay → same score. Do not introduce randomness inside `ScoreCalculator`.

## Step-by-step build order

1. Define the enums + `WordContext` + `ModifierCard` + `ActionCard` records.
2. Author `ModifierLibrary` and `ActionLibrary` static lists.
3. Implement and test `ScoreCalculator` in isolation.
4. Extend `AlphaChainPlayerState` with hand/bay fields.
5. Add the three commands and wire them through `RoundState.HandleCommand`.
6. Update `RoundState` submission flow to use `ScoreCalculator` and honor pending actions.
7. Build `EngineBay`, `ActionHand`, `CardTooltip` components.
8. Add the debug `Grant Cards` button.
9. Tests; smoke test in browser.

## Risks & notes

- **Conditional explosion:** Multiplicative conditionals can quickly produce huge scores. Cap individual word scores at, say, `10_000` to keep UI sane, or document that it's deliberate.
- **Lambdas in cards:** Modifier `Trigger` and `Value` are `Func<>` delegates — they are not serialisable. They live on the singleton library list and are referenced by `Id`. Per-player state should store only `Id`s (and resolve to the library at evaluation time) **or** the immutable record reference — either is safe so long as the per-player list is not persisted to disk yet.
- **Time Thief vs. concurrent ticks:** Mutating `PhaseEndTime` from one player while the turn-tick handler is firing must go through `state.Execute`. Ensure the FSM transition is the single writer of `PhaseEndTime`.
