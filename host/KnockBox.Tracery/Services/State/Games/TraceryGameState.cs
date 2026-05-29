using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using KnockBox.Tracery.Models;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Tracery.Services.State.Games
{
    public class TraceryGameState(
        User host,
        ILogger<TraceryGameState> logger)
        : AbstractGameState(host, logger)
    {
        // Host-configurable match rules. Always replaced atomically via UpdateSettings; the
        // setter is private so callers can't bypass the lock. Persisted to the host's browser
        // localStorage by the room page so preferred rules survive across sessions.
        public TracerySettings Settings { get; private set; } = new();

        /// <summary>
        /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s result
        /// inside <see cref="AbstractGameState.Execute(Action)"/>, so subscribers observe a
        /// single consistent transition and notification fires once after the lock releases.
        /// </summary>
        public Result UpdateSettings(Func<TracerySettings, TracerySettings> mutate) =>
            // Normalize after every mutation so out-of-range values can't reach the engine no matter
            // how they arrive (UI edit, restored localStorage, deserialization) — chiefly the 8×8 grid
            // cap the solver's performance bound relies on.
            Execute(() => { Settings = mutate(Settings).Normalize(); });

        // Phase / transition. Mutated only inside Execute (by the engine); read lock-free by
        // the room page to switch its rendered view.
        public GamePhase Phase { get; set; } = GamePhase.Lobby;
        public DateTimeOffset? PhaseExpiresAtUtc { get; set; }
        public int CurrentRound { get; set; } = 0;
        public ImmutableList<RoundResult> RoundResults { get; set; } = [];

        // The assembled reveal beats for the most recently completed round (Milestone 07), set by
        // the engine in CompleteRound and read lock-free by the host reveal view. Null until the
        // first round closes; replaced each round so the Reveal phase always renders the latest.
        public RevealData? CurrentReveal { get; set; }

        // Per-round board + authoritative solve, set by the engine on entering Playing and
        // read lock-free by the room page (M05) for rendering/validation. Null/empty outside
        // an active round.
        public Grid? CurrentGrid { get; set; }

        /// <summary>
        /// Every word findable on the board under the <em>answer</em> (validation) dictionary —
        /// the complete set a player may bank. Drives the reveal's theoretical maximum and the
        /// word-beat path lookups, so a player's score can never exceed it.
        /// </summary>
        public IReadOnlyDictionary<string, TracedWord> FindableWords { get; set; }
            = ImmutableDictionary<string, TracedWord>.Empty;

        /// <summary>
        /// Words findable under the <em>board</em> (generation) dictionary — the common-word
        /// set the board was built from. Drives the reveal's "words nobody found" list so it
        /// stays a clean list of recognizable words. Equals <see cref="FindableWords"/> when the
        /// generation and validation dictionaries resolve to the same pool.
        /// </summary>
        public IReadOnlyDictionary<string, TracedWord> BoardFindableWords { get; set; }
            = ImmutableDictionary<string, TracedWord>.Empty;
        public DateTimeOffset? RoundStartTime { get; set; }

        /// <summary>
        /// Search mode: the round's shared list of target words (lower-cased), the same for every
        /// player. Set by the engine in <c>EnterPlaying</c> from the board's findable words; empty
        /// outside an active Search round and always empty in Standard mode. Read lock-free by the
        /// room page to render the search checklist (the order matters), so it stays an array; the
        /// setter also maintains <see cref="_searchListSet"/> for the hot-path membership test.
        /// </summary>
        public ImmutableArray<string> SearchList
        {
            get => _searchList;
            set
            {
                _searchList = value;
                // Ordinal set so SubmitTrace's per-submission membership check is O(1) instead of a
                // linear scan inside the execute lock. The solver emits lowercase keys and the list is
                // drawn from them, so both sides are lowercase — ordinal is correct and explicit.
                _searchListSet = value.IsDefaultOrEmpty
                    ? FrozenSet<string>.Empty
                    : value.ToFrozenSet(StringComparer.Ordinal);
            }
        }
        private ImmutableArray<string> _searchList = [];
        private FrozenSet<string> _searchListSet = FrozenSet<string>.Empty;

        /// <summary>O(1) membership test for the round's <see cref="SearchList"/> (Search mode).</summary>
        public bool IsSearchTarget(string word) => _searchListSet.Contains(word);

        /// <summary>
        /// Search mode: how many players have found every word on <see cref="SearchList"/> so far
        /// this round. Incremented as each player completes to assign their
        /// <c>TraceryPlayerState.CompletionRank</c>. Reset at the start of each round.
        /// </summary>
        public int SearchCompletionsThisRound { get; set; }

        // The input gate: true only while the Playing phase is accepting traces. M05's
        // SubmitTrace early-returns a failure unless Phase == Playing && IsRoundActive. Flipped
        // false the moment the round ends (timer fires or round completes), so late submissions
        // are rejected with no separate lock flag.
        public bool IsRoundActive { get; set; }

        // Player tracking. Writes are owned by TraceryGameEngine and only ever happen inside
        // Execute/ExecuteAsync. Render-thread callers read via TryGetPlayerState — they must
        // never invoke CreatePlayerState, which would mutate the dictionary unlocked.
        private readonly ConcurrentDictionary<string, TraceryPlayerState> _playerStates = new();
        public IReadOnlyDictionary<string, TraceryPlayerState> PlayerStates => _playerStates;

        // True when the host plays alongside everyone else; false when the host is a
        // display-only observer (set at StartAsync time based on whether any other players
        // joined and the HostPlaysAlong setting, then locked for the duration of the match).
        //
        // Hides AbstractGameState.HostIsParticipant on purpose: the base property is the
        // dynamic, lobby-time toggle; Tracery's is the frozen-at-start snapshot the engine
        // and UI read from for the rest of the match (mirrors SpardleState).
        public new bool HostIsParticipant { get; private set; } = true;

        internal new void SetHostIsParticipant(bool value) => HostIsParticipant = value;

        // The participant roster captured at game start, frozen for the match. Used by the
        // final-standings screen so players who disconnect (and are dropped from the live
        // Players roster) still appear on the end-screen leaderboard. PlayerStates already
        // persists their CumulativeScore, so leavers keep their final score.
        //
        // Hides AbstractGameState.Participants for the same reason: this is the immutable
        // match roster, not the dynamic participant snapshot the base exposes.
        public new ImmutableArray<PlayerEntry> Participants { get; private set; } = [];

        internal void SetParticipants(IEnumerable<PlayerEntry> participants) =>
            // Drop the unsubscriber token so the long-lived snapshot doesn't retain
            // registration handles; only User + DisplayName are needed for display.
            Participants = participants
                .Select(e => new PlayerEntry(e.User, e.DisplayName, null))
                .ToImmutableArray();

        /// <summary>
        /// Creates (or returns the existing) <see cref="TraceryPlayerState"/> for
        /// <paramref name="userId"/>. Mutates <see cref="PlayerStates"/>; callers MUST be
        /// inside <c>Execute</c>/<c>ExecuteAsync</c>.
        /// </summary>
        internal TraceryPlayerState CreatePlayerState(string userId)
        {
            if (!_playerStates.TryGetValue(userId, out var state))
            {
                state = new TraceryPlayerState();
                _playerStates[userId] = state;
            }
            return state;
        }

        /// <summary>
        /// Read-only lookup for render-thread callers. Returns false when no entry exists
        /// (e.g., an observing host, or a spectator who joined mid-round).
        /// </summary>
        public bool TryGetPlayerState(string userId, out TraceryPlayerState state)
            => _playerStates.TryGetValue(userId, out state!);
    }
}
