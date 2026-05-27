using System.Collections.Concurrent;
using System.Collections.Immutable;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle.Models;
using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging;

namespace KnockBox.Spardle;

public class SpardleState(User host, ILogger logger) : AbstractGameState(host, logger)
{
    // Settings
    public WordPoolMode WordPoolMode { get; set; } = WordPoolMode.NytStandard;
    public WordOrderMode WordOrderMode { get; set; } = WordOrderMode.RandomNoRepeats;
    public WinConditionMode WinCondition { get; set; } = WinConditionMode.Sprinter;

    /// <summary>
    /// When true, the engine picks all round words at a single fixed
    /// <see cref="TargetWordLength"/>. When false, words are sampled across
    /// <see cref="MinWordLength"/>–<see cref="MaxWordLength"/> inclusive.
    /// Only consulted when <see cref="WordPoolMode"/> is
    /// <see cref="WordPoolMode.FullDictionary"/>.
    /// </summary>
    public bool ConstantWordLength { get; set; } = true;

    /// <summary>
    /// Target word length when <see cref="ConstantWordLength"/> is true.
    /// Forced to 5 by the engine when <see cref="WordPoolMode"/> is
    /// <see cref="WordPoolMode.NytStandard"/>. Ignored when
    /// <see cref="CustomWordPool"/> is non-empty.
    /// </summary>
    public int TargetWordLength { get; set; } = 5;

    /// <summary>
    /// Minimum word length (inclusive) when <see cref="ConstantWordLength"/> is false.
    /// </summary>
    public int MinWordLength { get; set; } = 3;

    /// <summary>
    /// Maximum word length (inclusive) when <see cref="ConstantWordLength"/> is false.
    /// </summary>
    public int MaxWordLength { get; set; } = 8;

    public bool HardModeEnabled { get; set; } = false;
    public TimeSpan RoundTimer { get; set; } = TimeSpan.FromMinutes(3);
    public bool AllowDictionaryFallback { get; set; } = true;
    public bool AllowCompoundWords { get; set; } = false;
    public double DifficultyMultiplier { get; set; } = 2.0;
    
    // Dynamic defaults
    public bool WaitForAll { get; set; } = true;
    public bool RevealAnswer { get; set; } = true;

    /// <summary>
    /// When true and other players are present, the host plays as a normal
    /// participant instead of becoming the display-only observer. Off by default,
    /// preserving the "host is the shared display once others join" behavior.
    /// </summary>
    public bool HostPlaysAlong { get; set; } = false;

    // Game state
    public int TotalRounds { get; set; } = 5;
    public int CurrentRound { get; set; } = 0;
    public string TargetWord { get; set; } = string.Empty;
    public DateTime? RoundStartTime { get; set; }
    public bool IsRoundActive { get; set; } = false;
    public bool IsGameOver { get; set; } = false;

    // Phase / transition
    public GamePhase Phase { get; set; } = GamePhase.Lobby;
    public DateTimeOffset? PhaseExpiresAtUtc { get; set; }
    public TimeSpan TransitionDuration { get; set; } = TimeSpan.FromSeconds(5);
    public ImmutableList<RoundResult> RoundHistory { get; set; } = [];
    public string? LastCompletedAnswer { get; set; }

    // Word lists
    // CustomWordPool is the canonical ordered list (drives display + round-queue selection).
    // CustomWordPoolLookup is the O(1) membership view consumed by SpardleEngine.ValidateGuess;
    // it auto-derives from the setter so callers cannot desync the two.
    public ImmutableList<string> CustomWordPool
    {
        get;
        set
        {
            field = value;
            CustomWordPoolLookup = value.ToImmutableHashSet(StringComparer.Ordinal);
        }
    } = [];
    public ImmutableHashSet<string> CustomWordPoolLookup { get; private set; } = ImmutableHashSet<string>.Empty;
    public ImmutableList<string> RoundQueue { get; set; } = [];

    // Player tracking. Writes are owned by SpardleEngine and only ever happen inside
    // Execute/ExecuteAsync. Render-thread callers read via TryGetPlayerState — they must
    // never invoke CreatePlayerState, which would mutate the dictionary unlocked.
    private readonly ConcurrentDictionary<string, PlayerState> _playerStates = new();
    public IReadOnlyDictionary<string, PlayerState> PlayerStates => _playerStates;

    // True when the host is playing alongside everyone else; false when the host is a
    // display-only observer (set at StartAsync time based on whether any other players
    // joined, then locked for the duration of the game).
    public bool HostIsParticipant { get; private set; } = true;

    internal void SetHostIsParticipant(bool value) => HostIsParticipant = value;

    // The participant roster captured at game start, frozen for the match. Used by
    // the final standings screen so players who disconnect (and are dropped from the
    // live Players roster) still appear on the end-screen leaderboard. PlayerStates
    // already persists their TotalScore, so leavers keep their final score.
    public ImmutableArray<PlayerEntry> Participants { get; private set; } = [];

    internal void SetParticipants(IEnumerable<PlayerEntry> participants) =>
        // Drop the unsubscriber token so the long-lived snapshot doesn't retain
        // registration handles; only User + DisplayName are needed for display.
        Participants = participants
            .Select(e => new PlayerEntry(e.User, e.DisplayName, null))
            .ToImmutableArray();

    /// <summary>
    /// Creates (or returns the existing) <see cref="PlayerState"/> for <paramref name="userId"/>.
    /// Mutates <see cref="PlayerStates"/>; callers MUST be inside <c>Execute</c>/<c>ExecuteAsync</c>.
    /// </summary>
    internal PlayerState CreatePlayerState(string userId)
    {
        if (!_playerStates.TryGetValue(userId, out var state))
        {
            state = new PlayerState();
            _playerStates[userId] = state;
        }
        return state;
    }

    /// <summary>
    /// Read-only lookup for render-thread callers. Returns false when no entry exists
    /// (e.g., an observing host, or a spectator who joined mid-round).
    /// </summary>
    public bool TryGetPlayerState(string userId, out PlayerState state)
        => _playerStates.TryGetValue(userId, out state!);
}
