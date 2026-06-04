using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Components;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.AlphaChain.Services.State.Games
{
    /// <summary>
    /// Per-room state for an Alpha Chain game. All mutation flows through
    /// <c>Execute</c>/<c>ExecuteAsync</c>; reads use <c>WithExclusiveRead</c>.
    /// </summary>
    public class AlphaChainGameState(
        User host,
        ILogger<AlphaChainGameState> logger)
        : AbstractGameState(host, logger),
          IPhasedGameState<AlphaChainGamePhase>,
          IPlayerTrackedGameState<AlphaChainPlayerState>,
          IFsmContextGameState<AlphaChainGameContext>
    {
        /// <summary>The FSM context for this game instance. Set once when the game starts.</summary>
        public AlphaChainGameContext? Context { get; set; }

        /// <summary>The current phase of the game.</summary>
        public AlphaChainGamePhase Phase { get; private set; }

        /// <summary>
        /// Updates the current phase. Notification is intentionally NOT raised here —
        /// callers run inside <c>Execute</c>/<c>ExecuteAsync</c>, which fires the change
        /// notification once after the lock is released.
        /// </summary>
        public void SetPhase(AlphaChainGamePhase phase) => Phase = phase;

        /// <summary>All player states, keyed by <c>User.Id</c>.</summary>
        public ConcurrentDictionary<Guid, AlphaChainPlayerState> GamePlayers { get; } = new();

        /// <summary>Manages turn order and the active player.</summary>
        public TurnManager TurnManager { get; } = new();

        /// <summary>Current round number, 1-based. Set to 1 in <c>SetupState</c>.</summary>
        public int CurrentRound { get; set; }

        /// <summary>Current era number, 1-based. Set to 1 in <c>SetupState</c>; advanced at Intermission (M4).</summary>
        public int CurrentEra { get; set; }

        /// <summary>When the current phase's timer expires. Set on entering <c>RoundState</c> and reset after every turn via <see cref="ResetTurnTimer"/>.</summary>
        public DateTimeOffset PhaseEndTime { get; set; }

        /// <summary>When the match started (set in <c>SetupState</c>). Used to compute <c>GameResults.Duration</c>.</summary>
        public DateTimeOffset StartedAt { get; set; }

        // ── Intermission (M4) ─────────────────────────────────────────────────

        /// <summary>The current Intermission sub-phase. Only meaningful while <see cref="Phase"/> is <c>Intermission</c>.</summary>
        public IntermissionSubPhase IntermissionPhase { get; set; }

        // ── Tutorials ─────────────────────────────────────────────────────────

        /// <summary>Which scripted tutorial is showing. Meaningful while <see cref="Phase"/> is
        /// <c>Tutorial</c> (full-screen Shiritori/Engine) or while <see cref="IntermissionPhase"/>
        /// is <c>TaxTutorial</c>.</summary>
        public TutorialKind CurrentTutorial { get; set; }

        /// <summary>Tutorials already played this match, so each shows at most once. Written inside
        /// the execute lock when a tutorial is entered; cleared by <c>ResetForLobby</c>.</summary>
        public HashSet<TutorialKind> ShownTutorials { get; } = new();

        /// <summary>
        /// When the current Intermission sub-phase's timer expires. Kept separate from
        /// <see cref="PhaseEndTime"/> so the round shot clock and the intermission countdowns
        /// never clobber one another.
        /// </summary>
        public DateTimeOffset SubPhaseEndTime { get; set; }

        /// <summary>
        /// Pending Optimization orderings, keyed by <c>User.Id</c>. Seeded with each active
        /// player's current bay order (<c>Submitted = false</c>) when Optimization begins;
        /// applied to the live bays only when the sub-phase ends.
        /// </summary>
        public Dictionary<Guid, OptimizationSubmission> OptimizationSubmissions { get; } = new();

        /// <summary>
        /// The player resolved to pick the next era's banned letter (lowest-score active
        /// player; ties broken by earliest turn-order index). Resolved when the Sniper Ban
        /// sub-phase begins; null outside it.
        /// </summary>
        public Guid? SniperBanUserId { get; set; }

        /// <summary>
        /// Every word played this match, used for O(1) duplicate rejection. Case-insensitive
        /// (words are normalized to lower-case before insertion, but the comparer keeps the
        /// check robust). Order is not preserved here — <see cref="SubmissionHistory"/> backs the UI feed.
        /// </summary>
        public HashSet<string> PlayedWords { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The match's single chronological record of accepted submissions — the one source of truth
        /// for the mid-game play feed, the game-over totals, the post-game history screen, and the
        /// prior-words snapshot handed to scoring. An <see cref="System.Collections.Immutable.ImmutableList{T}"/>
        /// so the long, frequently-appended feed shares structure cheaply and the evaluation context can
        /// be handed a stable snapshot: appended after a play is credited, so snapshotting it before the
        /// append naturally excludes the current word. Each entry carries its
        /// <see cref="Data.AlphaChainSubmission.Engine"/> scoring trace.
        /// </summary>
        public System.Collections.Immutable.ImmutableList<AlphaChainSubmission> SubmissionHistory { get; set; } =
            System.Collections.Immutable.ImmutableList<AlphaChainSubmission>.Empty;

        /// <summary>
        /// The most recent accepted word's scoring trace, for the center-stage score-replay
        /// animation every client plays. Null before the first play. Set inside the execute
        /// lock in <c>RoundState</c>; the change notification (fired after unlock) drives the
        /// replay on every circuit.
        /// </summary>
        public ScoreReplay? LatestScoreReplay { get; set; }

        /// <summary>Monotonic counter bumped on each accepted play so the overlay replays once per word.</summary>
        public int ScoreReplaySequence { get; set; }

        /// <summary>
        /// When set, the round-ending word has been accepted and the FSM is holding in
        /// <c>RoundState</c> until this time so the inline score animation can finish before the
        /// end-of-era/match transition fires. Submissions and action plays are refused while it is
        /// set; the round shot clock is paused. Cleared when the transition fires (or a new round
        /// begins). <see cref="PendingTransitionIsGameOver"/> says which transition is pending.
        /// </summary>
        public DateTimeOffset? PendingTransitionAt { get; set; }

        /// <summary>True when the pending hold ends in <c>GameOver</c> (the final scheduled round);
        /// false when it opens the Intermission. Only meaningful while <see cref="PendingTransitionAt"/> is set.</summary>
        public bool PendingTransitionIsGameOver { get; set; }

        /// <summary>The most recently accepted word (normalized), or null before the first play.</summary>
        public string? LastWord { get; set; }

        /// <summary>
        /// The letter the next word must start with (lower-case), or null when the next
        /// player has a free choice — at game start and after a banned-letter-as-last-letter
        /// play. A player holding The Wildcard may ignore this requirement on their own turn.
        /// </summary>
        public char? RequiredStartLetter { get; set; }

        /// <summary>
        /// The match's banned letter (lower-case), or null when none is in effect. Era 1 is
        /// ban-free (null); from era 2 on it is set by the Sniper Ban at each Intermission, the
        /// sole writer. Using it anywhere in a word triggers the Zero-Point Tax; using it as the
        /// last letter also clears <see cref="RequiredStartLetter"/> for the next player.
        /// </summary>
        public char? BannedLetter { get; set; }

        /// <summary>
        /// The player marked as the round's leader at round start (highest score; ties broken by
        /// earliest turn order), or null before the first mark. The Bounty Hunter docks this player
        /// if they submit a too-short word on their turn this round. Re-snapshotted each round wrap.
        /// </summary>
        public Guid? RoundLeaderUserId { get; set; }

        // ── Engine-effect notice channel (off-submission automated effects) ───

        /// <summary>
        /// Automated engine effects that fired outside a word submission (e.g. a Titanium Mirror
        /// reflection landing on a later turn), published so clients animate them. Submission-time
        /// effects instead ride on the word's <see cref="LatestScoreReplay"/>. Replaced wholesale
        /// on each fire.
        /// </summary>
        public IReadOnlyList<EngineEffectEvent> LatestEngineNotices { get; set; } = [];

        /// <summary>Monotonic counter bumped whenever <see cref="LatestEngineNotices"/> changes,
        /// so the engine-effect overlay <c>@key</c>s off it and animates exactly once.</summary>
        public int EngineNoticeSequence { get; set; }

        /// <summary>The shortest a shot clock can be armed to, so the glass-cannon clock cards
        /// (Vault, Redline, Panic Button, Hyper-Drive) can shorten but never zero/invert it.</summary>
        public const int MinShotClockSeconds = 3;

        /// <summary>
        /// Re-arms the shot clock from <paramref name="now"/> for the active player, applying that
        /// player's modifier clock effects (see <see cref="ComputeArmedShotClockSeconds"/>). Called
        /// on entering <c>RoundState</c> and after every turn (submission or timeout). Caller must
        /// already hold the execute lock.
        /// </summary>
        public void ResetTurnTimer(DateTimeOffset now)
        {
            int seconds = Settings.ShotClockSeconds;
            if (TurnManager.CurrentPlayer is { } id && GamePlayers.TryGetValue(id, out var player))
                seconds = ComputeArmedShotClockSeconds(player);
            PhaseEndTime = now.AddSeconds(seconds);
        }

        /// <summary>
        /// The shot-clock length to arm for <paramref name="player"/>: the configured base, then every
        /// <see cref="IShotClockModifier"/> in their Engine Bay folded in (fractions first, then flat
        /// seconds), then any <see cref="IShotClockCap"/> applied (Hyper-Drive lowers a longer clock to
        /// its cap but never raises a shorter one), floored at <see cref="MinShotClockSeconds"/>. Pure
        /// function of the player's bay + match settings.
        /// <para>
        /// The Anchor Chain's <see cref="IShotClockOverride"/> short-circuits all of that: it pins the
        /// clock to a strict, unmodifiable length (the smallest override if several are equipped),
        /// ignoring every clock effect and cap alike.
        /// </para>
        /// </summary>
        public int ComputeArmedShotClockSeconds(AlphaChainPlayerState player)
        {
            // A single-player context so the clock capabilities can read this owner via
            // ctx.GetPlayer(PlayerIndex) and resolve room state services. Services is null only outside
            // a started game, where this is never called.
            var ctx = new EngineEvaluationContext(string.Empty, Array.Empty<char>(), new[] { player })
            {
                Bay = player.EngineBay,
                Services = Context?.EvaluationServices,
                PlayerIndex = 0,
            };
            var bay = (IReadOnlyList<IModifierCard>)player.EngineBay;

            // The Anchor Chain pins the clock: unmodifiable, ignores clock effects + Hyper-Drive.
            if (bay.FixedShotClockSeconds(ctx) is { } pinned)
                return Math.Max(MinShotClockSeconds, pinned);

            // The base clock — or a latched Hyper-Drive's replacement — then per-owner clock effects:
            // all fractions, then all flat seconds.
            double seconds = bay.BaseShotClockSeconds(ctx) ?? Settings.ShotClockSeconds;
            var (fraction, flat) = bay.ShotClockEffect();
            seconds = seconds * (1 + fraction) + flat;

            int armed = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);

            // A shot-clock cap (Hyper-Drive's 5s) lowers a longer clock but never raises a shorter one.
            if (bay.ShotClockCapSeconds(ctx) is { } cap)
                armed = Math.Min(armed, cap);

            return Math.Max(MinShotClockSeconds, armed);
        }

        /// <summary>Monotonic counter feeding <see cref="AlphaChainPlayerState.EliminationOrder"/>.</summary>
        private int _eliminationCounter;

        /// <summary>
        /// Marks <paramref name="player"/> eliminated and stamps its
        /// <see cref="AlphaChainPlayerState.EliminationOrder"/> the first time it happens, so
        /// <c>GameOverState</c> can rank eliminated players by how long they lasted. Idempotent —
        /// re-marking an already-eliminated player keeps the original order. Caller must hold the
        /// execute lock.
        /// </summary>
        public void MarkEliminated(AlphaChainPlayerState player)
        {
            player.EliminationOrder ??= ++_eliminationCounter;
            player.IsEliminated = true;
        }

        /// <summary>Final standings, populated by <c>GameOverState</c>.</summary>
        public GameResults? Results { get; set; }

        /// <summary>
        /// Host-configurable match rules. Replaced atomically via <see cref="UpdateSettings"/>;
        /// the setter is private so callers can't bypass the lock.
        /// </summary>
        public AlphaChainSettings Settings { get; private set; } = new();

        /// <summary>
        /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s result
        /// and reflects the new <c>HostPlays</c> value into <c>HostIsParticipant</c> in the same
        /// critical section (mirrors <c>OperatorGameState.UpdateSettings</c>).
        /// </summary>
        public Result UpdateSettings(Func<AlphaChainSettings, AlphaChainSettings> mutate) =>
            Execute(() =>
            {
                Settings = mutate(Settings);
                SetHostIsParticipant(Settings.HostPlays);
            });
    }
}
