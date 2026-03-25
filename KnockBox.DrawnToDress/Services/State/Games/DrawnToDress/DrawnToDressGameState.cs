using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.DrawnToDress
{
    public class DrawnToDressGameState(
        User host,
        ILogger<DrawnToDressGameState> logger)
        : AbstractGameState(host, logger)
    {
        // ── Phase ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The current phase of the game. Updated by each FSM state on entry.
        /// </summary>
        public GamePhase Phase { get; private set; } = GamePhase.Lobby;

        // ── Configuration ─────────────────────────────────────────────────────

        /// <summary>
        /// Game configuration (tunable values with GDD defaults).
        /// </summary>
        public DrawnToDressConfig Config { get; set; } = new();

        // ── Players ───────────────────────────────────────────────────────────

        /// <summary>
        /// All player states, keyed by player ID.
        /// </summary>
        public readonly ConcurrentDictionary<string, DrawnToDressPlayerState> GamePlayers = new();

        // ── FSM ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The per-game context that holds helpers and the FSM reference.
        /// Set once during <c>StartAsync</c> and remains for the game's lifetime.
        /// </summary>
        public DrawnToDressGameContext? Context { get; set; }

        // ── Timer tracking ────────────────────────────────────────────────────

        /// <summary>
        /// UTC deadline for the current timed phase.  Timed FSM states write this on
        /// entry so that the UI can display a countdown and so the engine's Tick can
        /// drive the transition when the deadline passes.
        /// </summary>
        public DateTimeOffset? PhaseDeadlineUtc { get; set; }

        // ── Theme ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The theme announced for this game session, or <see langword="null"/> while
        /// still in the lobby / theme-selection phase.
        /// </summary>
        public ThemeDefinition? CurrentTheme { get; set; }

        /// <summary>
        /// <see langword="true"/> once the theme has been revealed to players.
        /// In <see cref="ThemeAnnouncement.BeforeDrawing"/> mode this is set when the
        /// theme is selected; in <see cref="ThemeAnnouncement.AfterDrawing"/> mode it is
        /// set after the drawing phase completes.
        /// </summary>
        public bool ThemeRevealedToPlayers { get; set; }

        /// <summary>
        /// Theme texts submitted by players when <see cref="DrawnToDressConfig.ThemeSource"/>
        /// is <see cref="ThemeSource.PlayerWritten"/>.  Keyed by player ID.
        /// </summary>
        public readonly ConcurrentDictionary<string, string> PlayerThemeSubmissions = new();

        /// <summary>
        /// Candidate themes presented to players for voting when
        /// <see cref="DrawnToDressConfig.ThemeSource"/> is <see cref="ThemeSource.RandomVoting"/>.
        /// Populated on entry to <see cref="GamePhase.ThemeSelection"/>.
        /// </summary>
        public List<ThemeDefinition> ThemeCandidates { get; set; } = [];

        /// <summary>
        /// Votes cast by players in <see cref="ThemeSource.RandomVoting"/> mode.
        /// Keyed by player ID; value is the <see cref="ThemeDefinition.Id"/> of the chosen
        /// candidate.
        /// </summary>
        public readonly ConcurrentDictionary<string, string> ThemeVotes = new();

        // ── Clothing pool ─────────────────────────────────────────────────────

        /// <summary>
        /// All clothing items that have been drawn and placed into the shared pool,
        /// keyed by item ID.
        /// </summary>
        public readonly ConcurrentDictionary<Guid, DrawnClothingItem> ClothingPool = new();

        // ── Voting ────────────────────────────────────────────────────────────

        /// <summary>
        /// All Swiss voting rounds for this session (populated by <c>VotingRoundSetupState</c>).
        /// </summary>
        public List<VotingRound> VotingRounds { get; set; } = [];

        /// <summary>
        /// 0-based index into <see cref="VotingRounds"/> pointing at the round that is
        /// currently active (or was most recently active).
        /// </summary>
        public int CurrentVotingRoundIndex { get; set; }

        /// <summary>
        /// All votes collected across all voting rounds.
        /// </summary>
        public readonly ConcurrentDictionary<Guid, VoteSubmission> Votes = new();

        /// <summary>
        /// The matchup ID that is awaiting a coin-flip tie-break resolution, or
        /// <see langword="null"/> when no flip is in progress.
        /// </summary>
        public Guid? PendingCoinFlipMatchupId { get; set; }

        // ── Methods ───────────────────────────────────────────────────────────

        /// <summary>
        /// Updates the current phase and notifies state-change listeners.
        /// </summary>
        public void SetPhase(GamePhase phase)
        {
            Phase = phase;
            StateChangedEventManager.Notify();
        }
    }

    /// <summary>
    /// Represents the current phase / FSM state of a Drawn To Dress session.
    /// Values that existed before the FSM skeleton are preserved at the same underlying
    /// integer so that any serialized data remains compatible.
    /// </summary>
    public enum GamePhase
    {
        // ── Pre-game ──────────────────────────────────────────────────────────
        Lobby = 0,

        // ── Theme ─────────────────────────────────────────────────────────────
        ThemeSelection = 1,

        // ── Drawing ───────────────────────────────────────────────────────────
        /// <summary>Players are drawing clothing items against a timer.</summary>
        Drawing = 2,

        /// <summary>
        /// The communal pool of drawn items is revealed to all players.
        /// This is a brief display-only transition state before outfit building begins.
        /// </summary>
        PoolReveal = 3,

        // ── Outfit assembly ───────────────────────────────────────────────────
        OutfitBuilding = 4,
        OutfitCustomization = 5,

        /// <summary>
        /// One or more outfits share an item; players must resolve the conflict before
        /// voting can begin.
        /// </summary>
        OutfitDistinctnessResolution = 6,

        // ── Voting ────────────────────────────────────────────────────────────
        VotingRoundSetup = 7,

        /// <summary>Active voting matchup round where players cast their votes.</summary>
        Voting = 8,

        /// <summary>
        /// A tied matchup is resolved by a coin flip before results are revealed.
        /// </summary>
        CoinFlip = 9,

        /// <summary>Per-round results screen shown between Swiss voting rounds.</summary>
        VotingRoundResults = 10,

        // ── End game ──────────────────────────────────────────────────────────
        Results = 11,

        // ── Game control ──────────────────────────────────────────────────────
        Paused = 12,
        Abandoned = 13,
    }
}
