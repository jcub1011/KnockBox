using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Components;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.LinkedList.Services.State.Games
{
    public class LinkedListGameState(
        User host,
        ILogger<LinkedListGameState> logger)
        : AbstractGameState(host, logger)
    {
        /// <summary>The current phase of the game.</summary>
        public LinkedListGamePhase Phase { get; private set; } = LinkedListGamePhase.Setup;

        /// <summary>
        /// Updates the current phase. Notification is intentionally NOT raised here —
        /// callers run inside <c>Execute</c>/<c>ExecuteAsync</c>, which fires
        /// <c>NotifyStateChanged</c> exactly once after the lock is released.
        /// </summary>
        public void SetPhase(LinkedListGamePhase phase) => Phase = phase;

        /// <summary>All player states, keyed by player id.</summary>
        public ConcurrentDictionary<string, LinkedListPlayerState> GamePlayers { get; } = new();

        // ── Per-group round data (§8.2) ──────────────────────────────────────
        //
        // The round's chain(s) live on <see cref="Groups"/>. Collective is exactly
        // one group containing every participant; Groups holds several, each with
        // its own chain, submitter rotation, and clock. The single-chain accessors
        // further down delegate to <see cref="PrimaryGroup"/> so M2–M4 code and
        // tests keep working unchanged through the Collective (single-group) path.

        /// <summary>The round's chains — exactly one for Collective, several for Groups.</summary>
        public List<ChainState> Groups { get; } = [];

        /// <summary>The first group, or <c>null</c> before a round has started. For
        /// Collective this is the only group; the single-chain accessors delegate here.</summary>
        public ChainState? PrimaryGroup => Groups.Count > 0 ? Groups[0] : null;

        /// <summary>The group the given player belongs to. Throws if the player isn't in
        /// any group — call <see cref="TryGroupOf"/> when membership is uncertain.</summary>
        public ChainState GroupOf(string playerId) => Groups.First(g => g.MemberIds.Contains(playerId));

        /// <summary>The group the given player belongs to, or <c>null</c> if none.</summary>
        public ChainState? TryGroupOf(string playerId) => Groups.FirstOrDefault(g => g.MemberIds.Contains(playerId));

        /// <summary>The group with the given id, or <c>null</c> if none.</summary>
        public ChainState? GroupById(string? groupId) =>
            groupId is null ? null : Groups.FirstOrDefault(g => g.GroupId == groupId);

        // ── Group assignment (lobby → engine, Groups mode) ───────────────────

        /// <summary>Pending team assignment chosen in the lobby for Groups mode: each
        /// inner list is one group's member ids. Read by the engine at start. Empty for
        /// Collective (the engine builds a single all-players group regardless).</summary>
        public List<List<string>> GroupAssignments { get; set; } = [];

        // ── Audit queue (staggered/batch auditing, §8.2) ─────────────────────
        //
        // A single human Auditor judges one group's submission at a time. As groups
        // submit, their ids queue here in FIFO order; the Auditor always acts on the
        // front group. Each group appears at most once.

        /// <summary>Group ids awaiting the Auditor, in submission (FIFO) order.</summary>
        public List<string> AuditQueue { get; } = [];

        /// <summary>The group whose submission the Auditor is currently judging — the
        /// front of <see cref="AuditQueue"/>, or <c>null</c> when nothing is pending.</summary>
        public string? AuditingGroupId => AuditQueue.Count > 0 ? AuditQueue[0] : null;

        /// <summary>The group the Auditor is currently judging, or <c>null</c>.</summary>
        public ChainState? AuditingGroup => GroupById(AuditingGroupId);

        // ── Shared round data ────────────────────────────────────────────────

        public string StartWord { get; set; } = "";
        public string DestinationWord { get; set; } = "";

        /// <summary>Drives the global Auditor rotation (§6). Holds every participant's
        /// id in a stable order set once at match start; <see cref="AuditorRotationIndex"/>
        /// indexes into it. Per-group submitter rotation lives on each
        /// <see cref="ChainState.TurnManager"/>, not here.</summary>
        public List<string> ParticipantOrder { get; } = [];

        // ── Single-chain accessors (Collective / PrimaryGroup back-compat) ────
        //
        // M2–M4 engine code and tests read/write the round's chain through these.
        // They delegate to PrimaryGroup so Collective behaves exactly as before;
        // Groups-aware code resolves a specific ChainState instead.

        /// <summary>Submitter rotation for the primary group (Collective: all players).</summary>
        public TurnManager TurnManager => PrimaryGroup!.TurnManager;

        public string CarriedWord
        {
            get => PrimaryGroup?.CarriedWord ?? "";
            set => PrimaryGroup!.CarriedWord = value;
        }

        public List<ChainLink> Chain => PrimaryGroup!.Chain;
        public List<RejectionInfo> RejectionLog => PrimaryGroup!.RejectionLog;

        public int RejectionsThisTurn
        {
            get => PrimaryGroup?.RejectionsThisTurn ?? 0;
            set => PrimaryGroup!.RejectionsThisTurn = value;
        }

        public bool DestinationReached
        {
            get => PrimaryGroup?.DestinationReached ?? false;
            set => PrimaryGroup!.DestinationReached = value;
        }

        public Submission? PendingSubmission
        {
            get => PrimaryGroup?.PendingSubmission;
            set => PrimaryGroup!.PendingSubmission = value;
        }

        public TimeSpan ElapsedThinkingTime
        {
            get => PrimaryGroup?.ElapsedThinkingTime ?? TimeSpan.Zero;
            set => PrimaryGroup!.ElapsedThinkingTime = value;
        }

        public DateTimeOffset? ThinkingSegmentStartedUtc
        {
            get => PrimaryGroup?.ThinkingSegmentStartedUtc;
            set => PrimaryGroup!.ThinkingSegmentStartedUtc = value;
        }

        public bool ClockRunning => PrimaryGroup?.ClockRunning ?? false;

        public int GuessCount => PrimaryGroup?.GuessCount ?? 0;

        public DateTimeOffset? PhaseExpiresAtUtc
        {
            get => PrimaryGroup?.PhaseExpiresAtUtc;
            set => PrimaryGroup!.PhaseExpiresAtUtc = value;
        }

        public int TurnSequence
        {
            get => PrimaryGroup?.TurnSequence ?? 0;
            set => PrimaryGroup!.TurnSequence = value;
        }

        public IScheduledCallbackHandle? TurnTimeoutHandle
        {
            get => PrimaryGroup?.TurnTimeoutHandle;
            set => PrimaryGroup!.TurnTimeoutHandle = value;
        }

        public TimeSpan ContributionBaseline
        {
            get => PrimaryGroup?.ContributionBaseline ?? TimeSpan.Zero;
            set => PrimaryGroup!.ContributionBaseline = value;
        }

        /// <summary>The result of the most recently completed round, computed when
        /// the game enters <see cref="LinkedListGamePhase.RoundOver"/>. For Collective
        /// this is the single group's score; for Groups it mirrors the winning group.</summary>
        public RoundResult? LastRoundResult { get; set; }

        /// <summary>Per-group standings for the most recent round (§8.2), ranked with
        /// cross-metric tie-breaking. Empty for an in-progress or Collective round
        /// (Collective scores through <see cref="LastRoundResult"/>).</summary>
        public IReadOnlyList<GroupStanding> LastStandings { get; set; } = [];

        // ── Auditor rotation (§6) ────────────────────────────────────────────

        /// <summary>The current Auditor's player id. Derived from
        /// <see cref="AuditorRotationIndex"/> by the engine on each rotation.</summary>
        public string AuditorPlayerId { get; set; } = "";

        /// <summary>Index into <see cref="ParticipantOrder"/> identifying the current
        /// Auditor. Advanced by one (with wrap) each round so the role rotates and
        /// everyone audits in turn (§6). Kept separate from the per-group submitter
        /// rotations.</summary>
        public int AuditorRotationIndex { get; set; }

        // ── Match progress (§10) ─────────────────────────────────────────────

        /// <summary>1-based round counter within the current match. Set to 1 at match
        /// start and incremented on each <c>RotateAuditorAndStartRound</c>.</summary>
        public int RoundNumber { get; set; }

        /// <summary>Fun end-of-match awards, computed once on entering
        /// <see cref="LinkedListGamePhase.GameOver"/>.</summary>
        public IReadOnlyList<Superlative> Superlatives { get; set; } = [];

        /// <summary>
        /// Host-configurable match rules. Always replaced atomically via
        /// <see cref="UpdateSettings"/>; the setter is private so callers can't
        /// bypass the lock.
        /// </summary>
        public LinkedListSettings Settings { get; private set; } = new();

        /// <summary>
        /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s
        /// result and reflects the new <c>HostPlaysGame</c> value into
        /// <see cref="AbstractGameState.HostIsParticipant"/> in the same critical
        /// section, so subscribers observe a single consistent transition.
        /// </summary>
        public Result UpdateSettings(Func<LinkedListSettings, LinkedListSettings> mutate) =>
            Execute(() =>
            {
                Settings = mutate(Settings);
                SetHostIsParticipant(Settings.HostPlaysGame);
            });
    }

    #region ChainState

    /// <summary>
    /// One group's per-round chain and the state that drives building it: the
    /// group's submitter rotation, carried word, accepted/rejected links, the
    /// pending submission awaiting the Auditor, and the Fastest-Time clock. A
    /// Collective round holds exactly one of these (all players); a Groups round
    /// holds several. All fields are mutated only inside the owning
    /// <see cref="LinkedListGameState"/>'s execute lock.
    /// </summary>
    public sealed class ChainState
    {
        /// <summary>Stable id for the group (also its audit-queue key).</summary>
        public required string GroupId { get; init; }

        /// <summary>Display name shown on the scoreboard ("Everyone", "Group A", …).</summary>
        public string GroupName { get; set; } = "";

        /// <summary>Member player ids. For Collective this is every participant; for
        /// Groups it's the team. The Auditor is skipped within the rotation when it's
        /// their turn to audit (they're never the active submitter).</summary>
        public List<string> MemberIds { get; } = [];

        /// <summary>Submitter rotation within this group.</summary>
        public TurnManager TurnManager { get; } = new();

        /// <summary>The word the next submission must pair with.</summary>
        public string CarriedWord { get; set; } = "";

        /// <summary>Accepted links in this group's chain.</summary>
        public readonly List<ChainLink> Chain = [];

        /// <summary>Rejected attempts for this group.</summary>
        public readonly List<RejectionInfo> RejectionLog = [];

        /// <summary>Rejections against the current submitter this turn (rejection cap, §7.3).</summary>
        public int RejectionsThisTurn { get; set; }

        /// <summary>True once this group's chain reaches the destination — the group is
        /// finished and stops accepting submissions (§8.2).</summary>
        public bool DestinationReached { get; set; }

        /// <summary>True once the group can no longer submit (destination reached). The
        /// round ends when every group is finished.</summary>
        public bool Finished => DestinationReached;

        /// <summary>This group's submission awaiting the Auditor, or <c>null</c>.</summary>
        public Submission? PendingSubmission { get; set; }

        // ── Fastest Time accrual (§5.2), per group ───────────────────────────

        /// <summary>Banked thinking time for this group's round so far.</summary>
        public TimeSpan ElapsedThinkingTime { get; set; } = TimeSpan.Zero;

        /// <summary>Non-null while this group's clock is running — the UTC instant the
        /// current thinking segment began.</summary>
        public DateTimeOffset? ThinkingSegmentStartedUtc { get; set; }

        /// <summary>True while a thinking segment is accruing for this group.</summary>
        public bool ClockRunning => ThinkingSegmentStartedUtc is not null;

        /// <summary>Accepted pairs in this group's chain — the Fewest-Guesses score.</summary>
        public int GuessCount => Chain.Count;

        /// <summary>UTC instant the current turn's per-turn clock expires, or <c>null</c>.</summary>
        public DateTimeOffset? PhaseExpiresAtUtc { get; set; }

        /// <summary>Monotonic turn token; bumped each new thinking turn so a stale
        /// scheduled timeout recognizes it has been superseded.</summary>
        public int TurnSequence { get; set; }

        /// <summary>Handle to the active per-turn timeout for this group.</summary>
        public IScheduledCallbackHandle? TurnTimeoutHandle { get; set; }

        /// <summary>Banked time captured at the start of the active submitter's current
        /// attempt, so <c>Approve</c> can charge a per-contribution time for "Speed Demon".</summary>
        public TimeSpan ContributionBaseline { get; set; }

        /// <summary>
        /// Starts a thinking segment if the clock is enabled (Fastest Time +
        /// <c>EnableTimers</c>) and not already running. Idempotent. Call inside Execute.
        /// </summary>
        public void StartClock(DateTimeOffset now, LinkedListSettings settings)
        {
            if (settings.ScoringMode == ScoringMode.FastestTime
                && settings.EnableTimers
                && ThinkingSegmentStartedUtc is null)
            {
                ThinkingSegmentStartedUtc = now;
            }
        }

        /// <summary>
        /// Banks the current thinking segment (if running) into
        /// <see cref="ElapsedThinkingTime"/> and stops the clock. Idempotent — a no-op
        /// when the clock is already paused. Call inside Execute.
        /// </summary>
        public void BankClock(DateTimeOffset now)
        {
            if (ThinkingSegmentStartedUtc is { } started)
            {
                ElapsedThinkingTime += now - started;
                ThinkingSegmentStartedUtc = null;
            }
        }
    }

    #endregion

    #region Enums

    public enum LinkedListGamePhase { Setup, Playing, RoundOver, GameOver }

    #endregion

    #region Records

    /// <summary>An accepted link in the chain (<c>FromWord</c> → <c>ToWord</c>).</summary>
    public sealed record ChainLink(string FromWord, string ToWord, string PlayerId, string PlayerName, bool IsLoop);

    /// <summary>A rejected attempt by the Auditor.</summary>
    public sealed record RejectionInfo(string PlayerId, string AttemptedWord);

    /// <summary>A player's proposed next word (the first word is the carried word).</summary>
    public sealed record Submission(string PlayerId, string ProposedWord);

    /// <summary>A fun end-of-match award (§10): a title, the winning player, and a
    /// short detail line explaining why they earned it.</summary>
    public sealed record Superlative(string Title, string Emoji, string PlayerId, string PlayerName, string Detail);

    /// <summary>
    /// Immutable snapshot of a finished round's score, computed once on entering
    /// <see cref="LinkedListGamePhase.RoundOver"/>. <paramref name="Guesses"/> is the
    /// accepted-pair count (Fewest Guesses score); <paramref name="Elapsed"/> is the
    /// banked thinking time (Fastest Time score). <paramref name="BeatPar"/> is only
    /// meaningful for Fewest Guesses with a non-null <paramref name="Par"/>.
    /// </summary>
    public sealed record RoundResult(
        ScoringMode Mode, int Guesses, TimeSpan Elapsed,
        int? Par, bool BeatPar, bool DestinationReached);

    /// <summary>
    /// A group's place on the competitive scoreboard (§8.2). <paramref name="Primary"/>
    /// is the active mode's metric (guess count or elapsed time) and
    /// <paramref name="Secondary"/> the other; ranking sorts on the primary and breaks
    /// ties with the secondary. <paramref name="IsTieBreakWinner"/> flags a group that
    /// out-ranked another on equal primary metric purely via the secondary.
    /// </summary>
    public sealed record GroupStanding(
        string GroupId, string GroupName, int Rank,
        int Guesses, TimeSpan Elapsed, bool DestinationReached,
        bool IsTieBreakWinner);

    public sealed class LinkedListPlayerState
    {
        public required string PlayerId { get; init; }
        public required string DisplayName { get; init; }

        /// <summary>Id of the group this player competes with (Groups mode), or empty for
        /// Collective. Set at match start from the team assignment.</summary>
        public string GroupId { get; set; } = "";

        // ── Match accumulators (persist across rounds; reset only at match start) ──
        public int AcceptedPairs { get; set; }     // for "fewest guesses" + superlatives
        public int RejectionsReceived { get; set; }
        public int LoopPairsMade { get; set; }      // "Loop Lord" superlative

        /// <summary>Fastest single accepted contribution's thinking time (Fastest
        /// Time mode), or <c>null</c> if the player hasn't landed a timed pair yet.
        /// Drives the "Speed Demon" superlative.</summary>
        public TimeSpan? FastestContribution { get; set; }
    }

    #endregion
}
