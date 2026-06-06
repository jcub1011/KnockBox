using System.Collections.Concurrent;
using System.Collections.Immutable;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle.Models;
using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging;

namespace KnockBox.Spardle;

public class SpardleState(User host, ILogger logger) : AbstractGameState(host, logger)
{
    // Host-configurable match rules. Always replaced atomically via UpdateSettings; the
    // setter is private so callers can't bypass the lock. Persisted to the host's browser
    // localStorage by the lobby page so preferred rules survive across sessions.
    public SpardleSettings Settings { get; private set; } = new();

    /// <summary>
    /// Atomically replaces <see cref="Settings"/> with <paramref name="mutate"/>'s result
    /// inside <see cref="AbstractGameState.Execute(Action)"/>, so subscribers observe a
    /// single consistent transition and notification fires once after the lock releases.
    /// </summary>
    public Result UpdateSettings(Func<SpardleSettings, SpardleSettings> mutate) =>
        Execute(() => { Settings = mutate(Settings); });

    // Spardle treats the host as a participant by default before the game starts;
    // StartAsync re-fixes HostIsParticipant based on whether other players joined.
    protected override bool DefaultHostIsParticipant => true;

    // Game state
    public int CurrentRound { get; set; } = 0;
    public string TargetWord { get; set; } = string.Empty;
    public DateTime? RoundStartTime { get; set; }
    public bool IsRoundActive { get; set; } = false;
    public bool IsGameOver { get; set; } = false;

    // Phase / transition
    public GamePhase Phase { get; set; } = GamePhase.Lobby;
    public DateTimeOffset? PhaseExpiresAtUtc { get; set; }
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
    private readonly ConcurrentDictionary<Guid, PlayerState> _playerStates = new();
    public IReadOnlyDictionary<Guid, PlayerState> PlayerStates => _playerStates;

    // The participant roster captured at game start, frozen for the match. Used by
    // the final standings screen so players who disconnect (and are dropped from the
    // live Players roster) still appear on the end-screen leaderboard. PlayerStates
    // already persists their TotalScore, so leavers keep their final score.
    //
    // Distinct from the base AbstractGameState.Participants (which tracks the live
    // roster and prunes leavers); this is the immutable match snapshot. The base's
    // HostIsParticipant toggle drives whether the host is included — set once at
    // StartAsync time and never changed, so it is effectively frozen for the match.
    public ImmutableArray<PlayerEntry> MatchParticipants { get; private set; } = [];

    internal void SetMatchParticipants(IEnumerable<PlayerEntry> participants) =>
        // Drop the unsubscriber token so the long-lived snapshot doesn't retain
        // registration handles; only User + DisplayName are needed for display.
        MatchParticipants = participants
            .Select(e => new PlayerEntry(e.User, e.DisplayName, null))
            .ToImmutableArray();

    /// <summary>
    /// Creates (or returns the existing) <see cref="PlayerState"/> for <paramref name="userId"/>.
    /// Mutates <see cref="PlayerStates"/>; callers MUST be inside <c>Execute</c>/<c>ExecuteAsync</c>.
    /// </summary>
    internal PlayerState CreatePlayerState(Guid userId)
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
    public bool TryGetPlayerState(Guid userId, out PlayerState state)
        => _playerStates.TryGetValue(userId, out state!);

    /// <summary>
    /// Clears all per-match data so the room can return to the joinable lobby. Caller
    /// MUST be inside <c>Execute</c>/<c>ExecuteAsync</c>. <see cref="Settings"/> and
    /// <see cref="CustomWordPool"/> are lobby config and intentionally preserved.
    /// </summary>
    internal void ResetForLobby()
    {
        _playerStates.Clear();
        MatchParticipants = [];
        CurrentRound = 0;
        TargetWord = string.Empty;
        RoundStartTime = null;
        IsRoundActive = false;
        IsGameOver = false;
        PhaseExpiresAtUtc = null;
        RoundHistory = RoundHistory.Clear();
        LastCompletedAnswer = null;
        RoundQueue = RoundQueue.Clear();
    }
}
