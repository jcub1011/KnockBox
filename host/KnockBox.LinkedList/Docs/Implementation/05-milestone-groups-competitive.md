# Milestone 5 — Groups (Competitive)

## Goal

Add the competitive **Groups** player structure (§8.2): players split into groups (min 2 per group), each group builds its **own chain** from the same start/destination and is scored independently; best score wins, ties broken by the other metric (time breaks guess ties and vice versa). Because v1 has a single human Auditor, use **staggered/batch auditing** (§8.2 default): the Auditor judges one group's submission at a time. This is the deliberately deferred-late milestone — it generalizes the single-chain core into per-group chains without disturbing Collective play.

## Prerequisites

- Milestones 1–4 complete. Collective mode is the well-tested baseline; Groups must not regress it. `Settings.PlayerStructure` already exists (M1).

## Architecture context (restated)

- The core loop currently keeps one chain on the state. Groups requires **per-group round data**. Refactor the single-chain fields into a reusable `ChainState` and hold either one (Collective) or several (Groups). Keep Collective behavior identical by treating it as exactly one group.
- A single Auditor + multiple groups means an **audit queue**: only one group's `PendingSubmission` is in front of the Auditor at a time. Other groups can still have players thinking/submitting into their own group's pending slot, but the Auditor clears them one at a time.
- All mutation remains inside `Execute`; per-group fields are mutated the same way.

## Files to create / modify

**Modify**
- `Services/State/Games/LinkedListGameState.cs` — extract `ChainState`; hold `IReadOnlyList<GroupState>`; audit-queue tracking; group-aware accessors.
- `Services/Logic/Games/LinkedListGameEngine.cs` — group-aware `SubmitPair`/`Approve`/`Reject`; group assignment at start; audit handoff; standings + tie-break.
- `Pages/LobbyPhase.razor(.cs)` — group assignment UI when `PlayerStructure == Groups`.
- `Pages/PlayingPhase.razor` — per-group chain display; Auditor audit-queue view (which group is up).
- `Pages/RoundOverPhase.razor` / `Pages/GameOverPhase.razor` — group standings + tie-break display.

## Implementation detail

### 1. Extract per-group chain state

```csharp
public sealed class ChainState
{
    public required string GroupId { get; init; }
    public string GroupName { get; set; } = "";
    public List<string> MemberIds { get; } = [];
    public TurnManager TurnManager { get; } = new();   // submitter rotation within the group
    public string CarriedWord { get; set; } = "";
    public readonly List<ChainLink> Chain = [];
    public readonly List<RejectionInfo> RejectionLog = [];
    public int RejectionsThisTurn { get; set; }
    public bool DestinationReached { get; set; }
    public Submission? PendingSubmission { get; set; }
    public TimeSpan ElapsedThinkingTime { get; set; }
    public DateTimeOffset? ThinkingSegmentStartedUtc { get; set; }
    public int GuessCount => Chain.Count;
}
```

On `LinkedListGameState`:
```csharp
public List<ChainState> Groups { get; } = [];   // exactly one for Collective
public ChainState GroupOf(string playerId) => Groups.First(g => g.MemberIds.Contains(playerId));
```
- For **Collective**: at start, create a single `ChainState` containing all participants. All existing engine logic operates on `Groups[0]`. Behavior is unchanged.
- The Auditor belongs to no group's submitter rotation (still excluded from playing).

### 2. Group assignment (start, Groups mode)

- Lobby UI (`LobbyPhase`) when `PlayerStructure == Groups`: assign each participant to a group (auto-balance button + manual override), enforce **min 2 per group** and ≥ 2 groups. Persist the assignment so the engine reads it at start.
- `StartAsyncCore` (Groups branch): build one `ChainState` per group, set each group's `TurnManager.SetTurnOrder(memberIds)`, `CarriedWord = StartWord`. Same start/destination for all groups.

### 3. Group-aware actions

- `SubmitPair(user, state, word)`: resolve `var g = state.GroupOf(user.Id)`; validate against `g.TurnManager.CurrentPlayer` and `g.PendingSubmission`. Bank the group's clock.
- `Approve`/`Reject`: the Auditor acts on the **group currently at the front of the audit queue** (see below). Mutate that group's `Chain`/`CarriedWord`/`RejectionsThisTurn`/`DestinationReached`. Destination reached by a group marks that group finished (it stops submitting); the round ends when all groups finish or a limit hits.

### 4. Audit queue (staggered/batch, §8.2)

- Track `public string? AuditingGroupId { get; set; }` — the group whose submission the Auditor is currently judging.
- When a group's `PendingSubmission` becomes non-null and `AuditingGroupId` is null, set `AuditingGroupId` to that group (FIFO across groups; keep an ordered list of waiting group ids for fairness). After `Approve`/`Reject` resolves the front group, advance `AuditingGroupId` to the next group with a pending submission, or null if none.
- Auditor view shows a queue: "Group A is up (2 waiting)". Only the front group's Approve/Reject is active.

### 5. Scoring, standings, tie-break

- Per-group score per the active mode: Fewest Guesses → `GuessCount`; Fastest Time → `ElapsedThinkingTime`.
- **Standings**: rank groups by the primary metric; **tie-break by the other metric** — time breaks guess ties; guesses break time ties (§8.2). Compute a `GroupStanding(groupId, primary, secondary, rank)` list for the scoreboard.
- Group that didn't reach the destination ranks below any that did (or by partial progress — pick one rule and document it on the screen).

### 6. UI

- `PlayingPhase`: show the player's **own group** chain prominently; optionally show rival group progress (links count / carried word) for tension. Auditor sees the audit queue and the front group's pending pair.
- `RoundOverPhase` / `GameOverPhase`: group standings table with primary + secondary metric columns and the tie-break winner highlighted.

## Tests

- **Collective unchanged**: a Collective match still runs through `Groups[0]` with identical results (regression guard — reuse M2/M3 assertions through the single-group path).
- **Group isolation**: an approve/reject/rejection-cap in one group does not alter another group's `Chain`/`CarriedWord`/`RejectionsThisTurn`.
- **Audit queue**: two groups submit; the Auditor resolves them one at a time in FIFO order; `AuditingGroupId` advances correctly and becomes null when the queue drains.
- **Tie-break**: equal guess counts → lower elapsed time wins (Fewest Guesses match using time as tiebreak), and the symmetric case for Fastest Time.
- **Assignment validation**: < 2 per group or < 2 groups is rejected at start.

## Verification

- `dotnet test …` green, including the Collective regression tests.
- Manual: run a Groups match with 2 groups (≥ 5 players + 1 Auditor). Confirm independent chains, the Auditor judging one group at a time via the queue, correct standings, and the right tie-break winner when scores match.

## Done-when checklist

- [ ] Single-chain state refactored into `ChainState`; Collective is one group with unchanged behavior.
- [ ] Group assignment UI enforces ≥ 2 groups and ≥ 2 members each.
- [ ] Engine actions are group-scoped; one group's state can't affect another.
- [ ] Single-Auditor audit queue resolves groups one at a time (FIFO) with a clear UI.
- [ ] Standings rank groups with correct cross-metric tie-breaking.
- [ ] Collective regression tests still pass; Groups tests pass; manual 2-group match verified.
