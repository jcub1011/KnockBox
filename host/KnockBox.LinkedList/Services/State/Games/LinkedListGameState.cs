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

        // ── Auditor (rotation logic lands in M4; M1 just assigns the first one) ──

        public string AuditorPlayerId { get; set; } = "";

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
        public int AcceptedPairs { get; set; }     // for "fewest guesses" + superlatives
        public int RejectionsReceived { get; set; }
        // group id / time accrual added in later milestones
    }

    #endregion
}
