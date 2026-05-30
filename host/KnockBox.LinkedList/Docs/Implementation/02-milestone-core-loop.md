# Milestone 2 — Core Gameplay Loop

## Goal

Implement the playable GDD §4 loop for the prototype configuration: **Collective (one shared chain) + Fewest Guesses + human Auditor**. A player submits a word that pairs with the carried word; the Auditor approves (chain advances, carried word updates, turn passes) or rejects with a reason (counts toward the per-turn cap; at the cap the turn is forfeited). Reaching the destination ends the round (`RoundOver`). The chain renders as connected "links" (the linked-list visual) inside the player/Auditor pages — no separate Stage route.

This is the prototype the GDD names as the immediate next step.

## Prerequisites

- Milestone 1 complete: settings, state model, engine start wiring, `Playing` phase reachable.

## Architecture context (restated)

- Engine actions mutate state only inside `state.Execute(...)`. The base subscription wired by `LobbyPageBase` re-renders all clients after each `Execute`.
- Player input that shouldn't trigger a broadcast (per-keystroke text) is handled locally in the component without `Execute` — see `host/KnockBox.Codeword/Pages/CluePhase.razor.cs` (`OnClueInput`, lines ~28–61) and bump an `@key` to reset the input after submit.
- Role-conditioned rendering: compute `myId`, `isAuditor`, `isMyTurn`, `isHostSpectator` at the top of the component, then branch. Mirror `host/KnockBox.Codeword/Pages/CluePhase.razor` and `host/KnockBox.CardCounter/Pages/PlayingPhase.razor`.
- `TurnManager.NextTurn()` advances the submitter; the Auditor is excluded from being the active submitter (M1 assigned a first Auditor; full rotation is M4 — for M2, skip the Auditor when advancing turns).

## Files to create / modify

**Modify**
- `Services/Logic/Games/LinkedListGameEngine.cs` — add `SubmitPair`, `Approve`, `Reject` action methods.
- `Services/State/Games/LinkedListGameState.cs` — add the current `Submission?` pending-audit field + helpers if needed.
- `Pages/LinkedListLobby.razor` — dispatch `Playing` → `<PlayingPhase>`.

**Create**
- `Pages/PlayingPhase.razor` + `.razor.cs` + `.razor.css` — the three role-conditioned views and the chain-as-links visual.

## Implementation detail

### 1. Pending submission on state

Add to `LinkedListGameState`:
```csharp
public Submission? PendingSubmission { get; set; }   // awaiting the Auditor's call; null between turns
public string? LastRejectionReason { get; set; }     // surfaced to the table for banter
```

### 2. Engine actions (all inside `state.Execute`)

`SubmitPair(User user, LinkedListGameState state, string word)` → `Result`:
- Validate: phase is `Playing`; `user.Id == TurnManager.CurrentPlayer`; `user.Id != AuditorPlayerId`; no `PendingSubmission` already; `word` non-empty after trim.
- Optional rigor (`Settings.NoImmediateRepeat`): reject (return failure) if `(CarriedWord, word)` equals the immediately previous accepted pair.
- Set `PendingSubmission = new Submission(user.Id, word.Trim())`. Return success. (No phase change — the Auditor view reacts.)

`Approve(User auditor, LinkedListGameState state)` → `Result`:
- Validate: `auditor.Id == AuditorPlayerId`; `PendingSubmission` is non-null.
- Compute loop flag: `isLoop = Chain.Any(l => l.FromWord == CarriedWord && l.ToWord == proposed)`.
- Append `new ChainLink(CarriedWord, proposed, sub.PlayerId, playerName, isLoop)`; increment `GamePlayers[sub.PlayerId].AcceptedPairs`.
- If `proposed` equals `DestinationWord` (case-insensitive): set `DestinationReached = true`, `SetPhase(RoundOver)`. Else advance: `CarriedWord = proposed`, `RejectionsThisTurn = 0`, `AdvanceToNextSubmitter()`.
- Clear `PendingSubmission`, `LastRejectionReason = null`. Return success.

`Reject(User auditor, LinkedListGameState state, string reason)` → `Result`:
- Validate: `auditor.Id == AuditorPlayerId`; `PendingSubmission` non-null; `reason` non-empty (banter fuel, §6/§9.2 — required).
- Append `new RejectionInfo(sub.PlayerId, sub.ProposedWord, reason.Trim())`; `LastRejectionReason = reason.Trim()`; increment `GamePlayers[sub.PlayerId].RejectionsReceived`; `RejectionsThisTurn++`; clear `PendingSubmission`.
- If `Settings.RejectionCap > 0 && RejectionsThisTurn >= Settings.RejectionCap`: **forfeit turn** (§7.3) — discard partial (already cleared), `RejectionsThisTurn = 0`, `AdvanceToNextSubmitter()`. The chain stays put.
- Return success.

`AdvanceToNextSubmitter()` (private helper, called inside `Execute`): call `TurnManager.NextTurn()` repeatedly until `CurrentPlayer != AuditorPlayerId` (so the Auditor never has to play their own submissions). Guard against an infinite loop when only the Auditor remains.

### 3. UI (`PlayingPhase.razor`)

`@inherits` the same base used by other phase sub-components (a plain `ComponentBase` receiving `[Parameter] GameState` + an `OnError` callback, like Codeword's phase components). Compute at top:
```csharp
var myId = UserService.CurrentUser?.Id ?? "";
var isAuditor = myId == GameState.AuditorPlayerId;
var isMyTurn = myId == GameState.TurnManager.CurrentPlayer && !isAuditor;
var pending = GameState.PendingSubmission;
```

Branches:
- **Submitter** (`isMyTurn`): start→destination banner; large **carried word** ("build from: HOUSE"); a single text input + Submit (keystroke-local, submit calls `GameEngine.SubmitPair`, bump `@key` to clear); show "waiting for Auditor…" once `pending != null`.
- **Auditor** (`isAuditor`): if `pending != null`, show the proposed pair large (`CarriedWord` **→** `pending.ProposedWord`) with **Approve** / **Reject** buttons; Reject reveals a required reason field (free text in M2; presets in M4) and only enables Reject when non-empty. If `pending == null`, show "waiting for a submission…". Also show the live chain.
- **Everyone else**: "waiting for {submitter}…", the live chain, and `LastRejectionReason` if present.

**Chain-as-links visual**: render `GameState.Chain` as connected pills/links, each showing `FromWord → ToWord`, the contributor's name, and a loop badge when `IsLoop`. Start and destination anchor the ends. This is the "linked list" identity of the game; style it in `PlayingPhase.razor.css`.

Wire `<PlayingPhase GameState="GameState" OnError="ShowError" />` into the `@switch` in `LinkedListLobby.razor`, and route errors to a toast (copy the `_errorMessage`/`ShowError` pattern from `CodewordLobby`).

## Tests

Engine (`LinkedListGameEngineTests`):
- `SubmitPair` then `Approve` advances `CarriedWord` to the proposed word and appends a `ChainLink`; `AcceptedPairs` increments.
- `Approve` with `ProposedWord == DestinationWord` sets `DestinationReached` and `Phase == RoundOver`.
- `Reject` requires a reason (empty reason → failure), logs a `RejectionInfo`, sets `LastRejectionReason`, increments `RejectionsThisTurn`.
- Hitting `RejectionCap` forfeits the turn: `RejectionsThisTurn` resets and the active submitter advances; the chain is unchanged. With `RejectionCap == 0`, rejections never forfeit.
- `SubmitPair` rejected when caller isn't the active submitter, when caller is the Auditor, or when a submission is already pending.
- `NoImmediateRepeat` blocks a pair identical to the previous accepted pair; with it off, the link is appended and flagged `IsLoop`.
- `AdvanceToNextSubmitter` skips the Auditor.

## Verification

- `dotnet test …/KnockBox.LinkedListTests.csproj` green.
- Manual multi-tab: host + ≥3 players. Configure Collective / Fewest Guesses. Play a full chain start→destination; confirm carried word updates, chain links render, the Auditor's reject reason shows to the table, the rejection cap forfeits a turn, and a loop pair shows a loop badge but isn't blocked (unless the toggle is on).

## Done-when checklist

- [ ] `SubmitPair`/`Approve`/`Reject` enforce all guards and mutate only via `Execute`.
- [ ] Carried word advances on approve; destination detection ends the round (`RoundOver`).
- [ ] Rejection requires a reason; cap forfeits the turn and discards the partial attempt.
- [ ] Loop pairs are flagged for display; blocked only when `NoImmediateRepeat` is on.
- [ ] Auditor is never made the active submitter.
- [ ] `PlayingPhase` renders submitter / Auditor / spectator views and the chain-as-links visual.
- [ ] Tests pass; manual playthrough reaches the destination.
