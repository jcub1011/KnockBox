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

        /// <summary>Drives the submitting-player rotation.</summary>
        public TurnManager TurnManager { get; } = new();

        /// <summary>All player states, keyed by player id.</summary>
        public ConcurrentDictionary<string, LinkedListPlayerState> GamePlayers { get; } = new();

        // ── Round data (single shared chain for Collective; Groups extends this in M5) ──

        public string StartWord { get; set; } = "";
        public string DestinationWord { get; set; } = "";
        public string CarriedWord { get; set; } = "";
        public readonly List<ChainLink> Chain = [];
        public readonly List<RejectionInfo> RejectionLog = [];
        public int RejectionsThisTurn { get; set; }
        public bool DestinationReached { get; set; }

        /// <summary>
        /// The submitter's word currently awaiting the Auditor's call, or
        /// <c>null</c> between turns (no pending decision). Set by
        /// <c>SubmitPair</c>; cleared by <c>Approve</c>/<c>Reject</c>.
        /// </summary>
        public Submission? PendingSubmission { get; set; }

        /// <summary>The Auditor's most recent rejection reason, surfaced to the
        /// whole table for banter (§6/§9.2). Cleared on the next accepted pair.</summary>
        public string? LastRejectionReason { get; set; }

        // ── Fastest Time accrual (§5.2) ──────────────────────────────────────
        //
        // The clock measures *thinking* time only. It accrues while a submitter
        // is thinking, banks (pauses) the moment a submission goes to the Auditor,
        // and resumes on a rejection. Auditor deliberation never counts. All of
        // these fields are mutated exclusively inside Execute via the engine.

        /// <summary>Banked thinking time for the round so far.</summary>
        public TimeSpan ElapsedThinkingTime { get; set; } = TimeSpan.Zero;

        /// <summary>Non-null while the clock is "running" — the UTC instant the
        /// current thinking segment began. Banked and cleared when the clock pauses.</summary>
        public DateTimeOffset? ThinkingSegmentStartedUtc { get; set; }

        /// <summary>True while a thinking segment is accruing.</summary>
        public bool ClockRunning => ThinkingSegmentStartedUtc is not null;

        /// <summary>Accepted pairs in the chain — the Fewest-Guesses score.</summary>
        public int GuessCount => Chain.Count;

        /// <summary>UTC instant the current turn's per-turn clock expires, or
        /// <c>null</c> when no per-turn timeout is armed. The UI reads this to
        /// render a live countdown; the engine reads it for nothing (the timeout
        /// itself fires via <see cref="AbstractGameState.ScheduleCallback"/>).</summary>
        public DateTimeOffset? PhaseExpiresAtUtc { get; set; }

        /// <summary>Monotonic turn token. Bumped each time a new thinking turn
        /// begins so a stale scheduled timeout can recognize it has been superseded.</summary>
        public int TurnSequence { get; set; }

        /// <summary>Handle to the active per-turn timeout, cancelled when the turn
        /// ends early (submission, approval, or round end). Not part of serialized
        /// state — it's a live scheduling handle owned by the engine.</summary>
        public IScheduledCallbackHandle? TurnTimeoutHandle { get; set; }

        /// <summary>The result of the most recently completed round, computed when
        /// the game enters <see cref="LinkedListGamePhase.RoundOver"/>.</summary>
        public RoundResult? LastRoundResult { get; set; }

        /// <summary>
        /// Starts a thinking segment if the clock is enabled (Fastest Time +
        /// <c>EnableTimers</c>) and not already running. Idempotent. Call inside Execute.
        /// </summary>
        public void StartClock(DateTimeOffset now)
        {
            if (Settings.ScoringMode == ScoringMode.FastestTime
                && Settings.EnableTimers
                && ThinkingSegmentStartedUtc is null)
            {
                ThinkingSegmentStartedUtc = now;
            }
        }

        /// <summary>
        /// Banks the current thinking segment (if running) into
        /// <see cref="ElapsedThinkingTime"/> and stops the clock. Idempotent —
        /// a no-op when the clock is already paused. Call inside Execute.
        /// </summary>
        public void BankClock(DateTimeOffset now)
        {
            if (ThinkingSegmentStartedUtc is { } started)
            {
                ElapsedThinkingTime += now - started;
                ThinkingSegmentStartedUtc = null;
            }
        }

        // ── Auditor rotation (§6) ────────────────────────────────────────────

        /// <summary>The current Auditor's player id. Derived from
        /// <see cref="AuditorRotationIndex"/> by the engine on each rotation.</summary>
        public string AuditorPlayerId { get; set; } = "";

        /// <summary>Index into <see cref="TurnManager"/>'s <c>TurnOrder</c> identifying
        /// the current Auditor. Advanced by one (with wrap) each round so the role
        /// rotates and everyone audits in turn (§6). Kept separate from the submitter
        /// rotation, which <see cref="TurnManager"/> drives.</summary>
        public int AuditorRotationIndex { get; set; }

        /// <summary>The Auditor's cosmetic persona for the current round (§6). No rule
        /// effect — shown as flavor + an informal difficulty hint. Reset each round.</summary>
        public AuditorPersona Persona { get; set; } = AuditorPersona.Neutral;

        // ── Match progress (§10) ─────────────────────────────────────────────

        /// <summary>1-based round counter within the current match. Set to 1 at match
        /// start and incremented on each <c>RotateAuditorAndStartRound</c>.</summary>
        public int RoundNumber { get; set; }

        /// <summary>Fun end-of-match awards, computed once on entering
        /// <see cref="LinkedListGamePhase.GameOver"/>.</summary>
        public IReadOnlyList<Superlative> Superlatives { get; set; } = [];

        // ── Reactions (§9.1) — heckle/cheer flavor, never scored ──────────────

        /// <summary>Reactions currently floating over the chain view. Each is trimmed
        /// by a scheduled clear ~2s after it's broadcast (see
        /// <c>LinkedListGameEngine.BroadcastReaction</c>).</summary>
        public readonly List<ReactionEvent> RecentReactions = [];

        /// <summary>Monotonic counter assigning each reaction a unique
        /// <see cref="ReactionEvent.Seq"/>, so the scheduled clear can remove exactly
        /// the reaction it queued.</summary>
        public long ReactionSequence { get; set; }

        /// <summary>Banked thinking time captured at the start of the active submitter's
        /// current attempt, so <c>Approve</c> can charge a per-contribution time for
        /// the "Speed Demon" superlative. Only meaningful when the clock runs
        /// (Fastest Time + timers).</summary>
        public TimeSpan ContributionBaseline { get; set; }

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

    #region Enums

    public enum LinkedListGamePhase { Setup, Playing, RoundOver, GameOver }

    #endregion

    #region Records

    /// <summary>An accepted link in the chain (<c>FromWord</c> → <c>ToWord</c>).</summary>
    public sealed record ChainLink(string FromWord, string ToWord, string PlayerId, string PlayerName, bool IsLoop);

    /// <summary>A rejected attempt and the Auditor's reason.</summary>
    public sealed record RejectionInfo(string PlayerId, string AttemptedWord, string Reason);

    /// <summary>A player's proposed next word (the first word is the carried word).</summary>
    public sealed record Submission(string PlayerId, string ProposedWord);

    /// <summary>A transient reaction broadcast by a non-active player (§9.1). Cleared
    /// shortly after display; <paramref name="Seq"/> uniquely identifies it so the
    /// scheduled clear removes exactly the right one.</summary>
    public sealed record ReactionEvent(string PlayerId, string Emoji, long Seq);

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

    public sealed class LinkedListPlayerState
    {
        public required string PlayerId { get; init; }
        public required string DisplayName { get; init; }

        // ── Match accumulators (persist across rounds; reset only at match start) ──
        public int AcceptedPairs { get; set; }     // for "fewest guesses" + superlatives
        public int RejectionsReceived { get; set; }
        public int LoopPairsMade { get; set; }      // "Loop Lord" superlative

        /// <summary>Fastest single accepted contribution's thinking time (Fastest
        /// Time mode), or <c>null</c> if the player hasn't landed a timed pair yet.
        /// Drives the "Speed Demon" superlative.</summary>
        public TimeSpan? FastestContribution { get; set; }
        // group id added in later milestones
    }

    #endregion
}
