# Milestone 4 — Auditor Flavor & Match Flow

## Goal

Turn the single playable round into a full match. Add **Auditor rotation** each round (§6), the cosmetic **persona dial**, one-tap **reason presets** plus free text (§9.2, §12 Q1), **emoji reactions** for non-active players (§9.1), and the complete **match flow** (§10): Lobby → Round → Scoreboard → rotate & repeat → final **Results** with fun superlatives ("Most Rejected," "Speed Demon").

## Prerequisites

- Milestones 1–3 complete: rounds play end to end with scoring and a `RoundOver` screen.

## Architecture context (restated)

- The Auditor can't play, so the role **rotates each round** automatically (§6). Drive rotation with a stored auditor index separate from `TurnManager` (which orders submitters). The current Auditor is excluded from the submitter rotation (already handled by `AdvanceToNextSubmitter` skipping the Auditor in M2).
- Transient broadcasts (reactions) still go through `Execute` so all clients see them; clear them after a short delay with `ScheduleCallback` or component-side `ScheduleClear` (see `DisposableComponent.ScheduleClear`).
- Match-level scoreboard mirrors `host/KnockBox.Codeword/Pages/GameOverPhase.razor` (ranked rows, medals).
- Reason presets + free text mirror the reject UI from M2; presets are just buttons that prefill the reason then submit.

## Files to create / modify

**Modify**
- `Services/State/Games/LinkedListGameState.cs` — auditor rotation state, persona, reactions, match accumulators, round counter.
- `Services/Logic/Games/LinkedListGameEngine.cs` — `RotateAuditorAndStartRound`, `EndMatch`, reaction broadcast, superlative computation.
- `Pages/PlayingPhase.razor` — persona indicator, reason presets, reaction buttons + reaction overlay, last-rejection banner.
- `Pages/RoundOverPhase.razor` — "next round" vs "end match" host controls; show who the next Auditor is.
- `Pages/LinkedListLobby.razor` — dispatch `GameOver` → `<GameOverPhase>`.

**Create**
- `Pages/GameOverPhase.razor` (+ `.css`) — final results + superlatives.
- (optional) `LinkedListPersona.cs` — persona enum + display metadata.

## Implementation detail

### 1. Auditor rotation (§6)

- Add `public int AuditorRotationIndex { get; set; }` (index into `TurnManager.TurnOrder` identifying the Auditor) and keep `AuditorPlayerId` derived from it.
- `RotateAuditorAndStartRound(state, now)` (inside `Execute`): advance `AuditorRotationIndex = (AuditorRotationIndex + 1) % TurnOrder.Count`; set `AuditorPlayerId`; reset round data (`Chain.Clear()`, `RejectionLog.Clear()`, `RejectionsThisTurn = 0`, `DestinationReached = false`, time accruals reset); pick a new start/destination (curated or host-set); set `CarriedWord = StartWord`; set the submitter to the first non-Auditor in turn order; `SetPhase(Playing)`; `StartClock()` if timed.
- Track match progress: `public int RoundNumber { get; set; }` and a host-set `RoundsPerMatch` (add to settings, default e.g. 5, or "one round per player so everyone audits once").

### 2. Persona dial (cosmetic)

```csharp
public enum AuditorPersona { Neutral, MercilessJudge, EasyMark, Pedant, WildCard }
public AuditorPersona Persona { get; set; } = AuditorPersona.Neutral;
```
- Chosen by the Auditor at round start (a control on the Auditor view) or carried from a default. **No rule effect** — purely shown on the Auditor view and the chain/spectator views as flavor + an informal difficulty hint.

### 3. Reason presets (§9.2)

- Define a small preset list (tune for banter, §12 Q1), e.g. `["Not a thing", "Too much of a stretch", "I just don't buy it", "Cute, but no", "Try harder"]`.
- Auditor reject UI: preset buttons that set the reason and immediately call `Reject`, plus a free-text field for custom reasons. Reason remains **required** (enforced in M2's `Reject`).

### 4. Reactions (§9.1)

```csharp
public sealed record ReactionEvent(string PlayerId, string Emoji, long Seq);
public readonly List<ReactionEvent> RecentReactions = [];   // trimmed/cleared after display
```
- `BroadcastReaction(user, state, emoji)` (inside `Execute`): append a `ReactionEvent`; schedule a clear (`ScheduleCallback`) to drop it after ~2s. Non-active players get emoji buttons; reactions float over the chain view. Keep this lightweight — it's heckle/cheer flavor, not scored.

### 5. Match flow (§10) + superlatives

- After `RoundOver`, the host chooses **Next Round** (→ `RotateAuditorAndStartRound`) or **End Match** (→ `EndMatch`). Auto-end when `RoundNumber >= RoundsPerMatch`.
- Accumulate per-player/match stats across rounds (extend `LinkedListPlayerState`: total accepted pairs, total rejections received, fastest solo contribution, etc.) and per-round results in `state`.
- `EndMatch(state)`: `SetPhase(GameOver)`; compute superlatives from accumulators:
  - **Most Rejected** — highest `RejectionsReceived`.
  - **Speed Demon** — fastest accepted contribution (Fastest Time) or most accepted pairs (Fewest Guesses).
  - Add 1–2 more for fun (e.g. "Loop Lord" for most loop pairs, "Smooth Operator" for zero rejections).
  - Store as `IReadOnlyList<Superlative>` (`record Superlative(string Title, string PlayerId, string Detail)`).
- `GameOverPhase`: ranked scoreboard (best run / par results for Collective; per-player credits) + superlative cards + a replay of the best chain. Mirror Codeword's `GameOverPhase` layout.

## Tests

- Auditor rotation: after each round the Auditor advances by one in turn order and wraps; the new Auditor is excluded from submitting that round.
- Persona: setting persona changes `Persona` only; no effect on `Approve`/`Reject` outcomes.
- Reject still requires a reason; a preset reject records the preset text and increments rejection stats.
- Reactions: `BroadcastReaction` appends and is cleared after the scheduled delay (test the append + that a manual clear empties it).
- Superlatives: with crafted accumulators, "Most Rejected" / "Speed Demon" pick the correct player; ties resolved deterministically.
- Match end: `EndMatch` sets `GameOver`; auto-ends at `RoundsPerMatch`.

## Verification

- `dotnet test …` green.
- Manual: play a multi-round match. Confirm the Auditor rotates each round and is shown on `RoundOver` ("Next Auditor: …"), persona shows as flavor, preset reasons work, non-active players can react with emoji, and the final Results screen shows ranked scores + superlatives.

## Done-when checklist

- [ ] Auditor rotates automatically each round; excluded from submitting; shown to players.
- [ ] Persona dial is cosmetic and visible; no rule effect.
- [ ] Reject offers presets + free text; reason still required.
- [ ] Emoji reactions broadcast and auto-clear.
- [ ] Match flow: round → scoreboard → rotate/repeat → `GameOver`; auto-ends at round limit.
- [ ] Results screen shows ranked scores + superlatives (Most Rejected, Speed Demon, …).
- [ ] Tests pass; multi-round match verified manually.
