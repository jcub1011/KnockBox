using KnockBox.Tracery.Models;

namespace KnockBox.Tracery.Contracts
{
    /// <summary>
    /// The per-recipient projection of a Tracery lobby's state, built field-by-field by the
    /// server's <c>TraceryStateProjector</c> (default-deny) and pushed to each connection over the
    /// hub. Everything here is either symmetric public state or scoped to the recipient: the only
    /// banked-word data is <em>the recipient's own</em> (<see cref="MyBankedWords"/>) — never another
    /// player's in-progress banks, and never the server's full findable-word answer key.
    /// </summary>
    public sealed record TraceryView
    {
        // ── Identity / lobby ──────────────────────────────────────────────────────
        public Guid HostId { get; init; }
        public Guid RecipientId { get; init; }
        public bool IsJoinable { get; init; }
        public bool RecipientIsHost { get; init; }
        public bool RecipientIsParticipant { get; init; }
        public bool HostIsParticipant { get; init; }

        /// <summary>True when the recipient is the host sitting out as the shared display.</summary>
        public bool IsHostObserver { get; init; }
        public int MinPlayerCount { get; init; }
        public int MaxPlayerCount { get; init; }

        // ── Roster (public) ────────────────────────────────────────────────────────
        public IReadOnlyList<TraceryRosterEntry> Roster { get; init; } = [];

        // ── Match flow (public) ──────────────────────────────────────────────────────
        public GamePhase Phase { get; init; } = GamePhase.Lobby;
        public int CurrentRound { get; init; }
        public int TotalRounds { get; init; }
        public bool IsRoundActive { get; init; }
        public TracerySettingsView Settings { get; init; } = new();

        // ── Timing (public) ──────────────────────────────────────────────────────────
        /// <summary>Absolute UTC deadline for the current phase, or null when the phase is untimed.</summary>
        public DateTimeOffset? PhaseEndsAtUtc { get; init; }

        /// <summary>The current phase's configured total span, in seconds (for the countdown clock).</summary>
        public int PhaseDurationSeconds { get; init; }

        // ── Board (public; populated only during Playing/Reveal) ───────────────────────
        public Grid? Grid { get; init; }

        /// <summary>Search mode only: the round's shared target list (lower-cased). Empty otherwise.</summary>
        public IReadOnlyList<string> SearchList { get; init; } = [];

        // ── Reveal / standings (public, after a round closes) ──────────────────────────
        public RevealData? CurrentReveal { get; init; }
        public IReadOnlyList<RoundResult> RoundResults { get; init; } = [];

        /// <summary>
        /// Live per-participant banked-word counts for the host-observer's standings rail. Populated
        /// ONLY when the recipient is the observing host (<see cref="IsHostObserver"/>) — a competing
        /// player must never see opponents' in-progress find counts, so it is empty for everyone else.
        /// </summary>
        public IReadOnlyList<TraceryLiveStanding> HostBoardStandings { get; init; } = [];

        /// <summary>
        /// The reveal-time board explorer's word set: every recognizable board word (plus any exotic
        /// word a player actually banked) pre-scored with a representative path. Populated ONLY during
        /// the <see cref="GamePhase.Reveal"/> phase — the round is over, so the answer key is no longer
        /// a live secret. Empty in every other phase.
        /// </summary>
        public IReadOnlyList<RevealBoardWord> RevealBoardWords { get; init; } = [];

        // ── Per-recipient PRIVATE state (the competitive secret) ───────────────────────
        /// <summary>The recipient's own banked words this round, acceptance order, with provisional points.</summary>
        public IReadOnlyList<TraceryBankedWord> MyBankedWords { get; init; } = [];

        /// <summary>The recipient's provisional running round score (excludes the unique-find multiplier).</summary>
        public int MyProvisionalRoundScore { get; init; }

        /// <summary>Search mode only: the recipient's 1-based finishing place, or null if not completed.</summary>
        public int? MyCompletionRank { get; init; }
    }

    /// <summary>One roster line: identity, host flag, and the public cumulative score.</summary>
    public sealed record TraceryRosterEntry(Guid UserId, string DisplayName, bool IsHost, int CumulativeScore);

    /// <summary>A word the recipient banked this round, with its provisional point value.</summary>
    public sealed record TraceryBankedWord(string Word, int ProvisionalPoints);

    /// <summary>One participant's live progress on the host-observer's standings rail.</summary>
    public sealed record TraceryLiveStanding(Guid UserId, string DisplayName, int BankedCount, int CumulativeScore);

    /// <summary>A board word in the reveal explorer: the word, its round-scoring value, and a
    /// representative grid path (cell ids) that spells it.</summary>
    public sealed record RevealBoardWord(string Word, int Points, IReadOnlyList<int> Path);
}
