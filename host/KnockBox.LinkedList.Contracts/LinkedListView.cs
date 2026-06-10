using KnockBox.LinkedList.Services.State.Games;

namespace KnockBox.LinkedList.Contracts
{
    /// <summary>
    /// The per-recipient projection of a Linked List lobby's state, built field-by-field by the
    /// server's <c>LinkedListStateProjector</c> (default-deny) and pushed to each connection over
    /// the hub.
    /// <para>
    /// <b>The competitive secret is a rival group's chain contents.</b> In <em>Collective</em> mode
    /// there is one shared chain, fully public, carried in <see cref="MyGroup"/> for everyone. In
    /// <em>Groups</em> mode a competing player sees only their <em>own</em> group's chain
    /// (<see cref="MyGroup"/>); rivals appear as <see cref="RivalChip"/>s carrying counts only — a
    /// rival's links, pending submission and carried-while-finished word have no field to travel in.
    /// The Auditor sees only the front audit-queue group (<see cref="AuditingGroup"/>); the
    /// non-participant host-observer sees every group (<see cref="AllGroups"/>). Once the round/match
    /// ends every chain is public, so <see cref="AllGroups"/> carries them all then.
    /// </para>
    /// </summary>
    public sealed record LinkedListView
    {
        // ── Identity / lobby (public symmetric) ──────────────────────────────────
        public Guid HostId { get; init; }
        public Guid RecipientId { get; init; }
        public bool IsJoinable { get; init; }
        public bool RecipientIsHost { get; init; }
        public bool RecipientIsParticipant { get; init; }
        public bool HostIsParticipant { get; init; }

        /// <summary>True when the recipient is the host sitting out as the shared display.</summary>
        public bool IsHostObserver { get; init; }

        /// <summary>True when the recipient is the round's Auditor.</summary>
        public bool RecipientIsAuditor { get; init; }

        public int MinPlayerCount { get; init; }
        public int MaxPlayerCount { get; init; }

        // ── Roster (public) ──────────────────────────────────────────────────────
        public IReadOnlyList<LinkedListRosterEntry> Roster { get; init; } = [];

        // ── Match flow (public) ──────────────────────────────────────────────────
        public LinkedListGamePhase Phase { get; init; } = LinkedListGamePhase.Setup;
        public int RoundNumber { get; init; }
        public LinkedListSettingsView Settings { get; init; } = new();

        // ── Journey (public) ─────────────────────────────────────────────────────
        public string StartWord { get; init; } = "";
        public string DestinationWord { get; init; } = "";

        // ── Auditor rotation (public) ────────────────────────────────────────────
        public Guid AuditorPlayerId { get; init; }
        public string AuditorName { get; init; } = "";
        public Guid NextAuditorId { get; init; }
        public string NextAuditorName { get; init; } = "";

        // ── Per-recipient chain projection ───────────────────────────────────────
        /// <summary>The recipient's own group chain (participant), or the single shared chain in
        /// Collective. Null for the Auditor and the host-observer in Groups mode.</summary>
        public GroupChainView? MyGroup { get; init; }

        /// <summary>The group the Auditor is currently judging (front of queue). Auditor-only.</summary>
        public GroupChainView? AuditingGroup { get; init; }

        /// <summary>How many groups are waiting on the Auditor (Groups mode). Auditor-only.</summary>
        public int AuditQueueLength { get; init; }

        /// <summary>Every group's chain — host-observer during play, and everyone once the
        /// round/match is over (no live secret remains). Empty otherwise.</summary>
        public IReadOnlyList<GroupChainView> AllGroups { get; init; } = [];

        /// <summary>Rival groups' progress chips (counts only) for the recipient's tension strip.
        /// Carries no chain contents.</summary>
        public IReadOnlyList<RivalChip> Rivals { get; init; } = [];

        // ── Round / match results (public) ───────────────────────────────────────
        public RoundResultView? LastRoundResult { get; init; }
        public IReadOnlyList<GroupStanding> Standings { get; init; } = [];
        public IReadOnlyList<Superlative> Superlatives { get; init; } = [];
        public IReadOnlyList<LinkedListPlayerScore> Scores { get; init; } = [];
    }

    /// <summary>One roster line: identity + host flag.</summary>
    public sealed record LinkedListRosterEntry(Guid UserId, string DisplayName, bool IsHost);

    /// <summary>
    /// One group's chain contents — the competitive secret. Placed ONLY in
    /// <see cref="LinkedListView.MyGroup"/> / <see cref="LinkedListView.AuditingGroup"/> /
    /// <see cref="LinkedListView.AllGroups"/>, never in a rival chip, so a rival's links can't leak.
    /// </summary>
    public sealed record GroupChainView
    {
        public string GroupId { get; init; } = "";
        public string GroupName { get; init; } = "";

        /// <summary>Index among all groups (0-based) for the per-strand theme colour.</summary>
        public int ColorIndex { get; init; }

        public string CarriedWord { get; init; } = "";
        public IReadOnlyList<ChainLink> Chain { get; init; } = [];

        /// <summary>The submission awaiting the Auditor, or null.</summary>
        public SubmissionView? Pending { get; init; }

        public bool DestinationReached { get; init; }
        public bool Finished { get; init; }
        public int GuessCount { get; init; }

        public Guid CurrentSubmitterId { get; init; }
        public string CurrentSubmitterName { get; init; } = "";

        // ── Fastest Time clock (for the client countdown) ─────────────────────────
        public TimeSpan ElapsedThinkingTime { get; init; }
        public bool ClockRunning { get; init; }

        /// <summary>Absolute UTC deadline for the active turn's per-turn clock, or null.</summary>
        public DateTimeOffset? PhaseExpiresAtUtc { get; init; }
    }

    /// <summary>A submission awaiting the Auditor.</summary>
    public sealed record SubmissionView(Guid PlayerId, string PlayerName, string ProposedWord);

    /// <summary>
    /// A rival group's progress for the tension strip — COUNTS ONLY. Carries no chain, no pending
    /// submission, and only the live carried word (<see cref="CarriedWord"/> is null once the rival
    /// finishes), exactly the partial info the live game already revealed.
    /// </summary>
    public sealed record RivalChip(
        string GroupId, string GroupName, int ColorIndex,
        int GuessCount, bool Finished, string? CarriedWord);

    /// <summary>
    /// The projected equivalent of the server-only <c>RoundResult</c> — the active mode's metric for
    /// the just-finished Collective round.
    /// </summary>
    public sealed record RoundResultView(
        ScoringMode Mode, int Guesses, TimeSpan Elapsed,
        int? Par, bool BeatPar, bool DestinationReached);

    /// <summary>Per-player end-of-match stats for the scoreboard (the projected equivalent of the
    /// server-only <c>LinkedListPlayerState</c> accumulators).</summary>
    public sealed record LinkedListPlayerScore(
        Guid PlayerId, string DisplayName, string GroupId,
        int AcceptedPairs, int RejectionsReceived, int LoopPairsMade,
        TimeSpan? FastestContribution);
}
