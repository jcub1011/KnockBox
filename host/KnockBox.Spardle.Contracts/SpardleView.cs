using KnockBox.Spardle.Models;

namespace KnockBox.Spardle.Contracts;

/// <summary>
/// The per-recipient projected view of a Spardle game. Built field-by-field by
/// the server's <c>SpardleStateProjector</c> under default-deny: the secret
/// answer (<see cref="Answer"/>) and any rival's guess letters are withheld
/// during play. A competing player gets their own <see cref="MyBoard"/> plus
/// count-only <see cref="Rivals"/>; the display-only host-observer gets every
/// board in <see cref="AllBoards"/>; once a round/match ends the answer and
/// outcomes become public.
/// </summary>
public sealed record SpardleView
{
    // Identity / lobby (public, symmetric)
    public Guid HostId { get; init; }
    public Guid RecipientId { get; init; }
    public bool IsJoinable { get; init; }
    public bool RecipientIsHost { get; init; }
    public bool RecipientIsParticipant { get; init; }
    public bool HostIsParticipant { get; init; }
    public bool IsHostObserver { get; init; }
    public int MinPlayerCount { get; init; }
    public int MaxPlayerCount { get; init; }

    public IReadOnlyList<SpardleRosterEntry> Roster { get; init; } = [];

    // Match flow
    public GamePhase Phase { get; init; } = GamePhase.Lobby;
    public int CurrentRound { get; init; }
    public int TotalRounds { get; init; }
    public SpardleSettingsView Settings { get; init; } = new();
    public DateTimeOffset? PhaseExpiresAtUtc { get; init; }

    // Board sizing — projected so the grid can size without knowing the secret word.
    public int WordLength { get; init; }
    public int MaxGuesses { get; init; }
    public bool IsRoundActive { get; init; }

    // The host lobby surfaces that an uploaded pool was accepted (count only — never the words).
    public bool HasCustomWordPool { get; init; }
    public int CustomWordCount { get; init; }

    // Per-recipient board projection (default-deny).
    public MyBoardView? MyBoard { get; init; }
    public IReadOnlyList<RivalView> Rivals { get; init; } = [];
    public IReadOnlyList<ObserverBoardView> AllBoards { get; init; } = [];

    // Results (post-round / game-over).
    public RoundResultView? LastRoundResult { get; init; }
    public string? Answer { get; init; }
    public IReadOnlyList<PlayerStandingView> Standings { get; init; } = [];
}

/// <summary>One roster row (live lobby roster, or the frozen match roster once started).</summary>
public sealed record SpardleRosterEntry(Guid UserId, string DisplayName, bool IsHost);

/// <summary>The recipient's OWN board — full guesses + per-round/match status. Sent only to its owner.</summary>
public sealed record MyBoardView
{
    public string DisplayName { get; init; } = string.Empty;
    public int Rank { get; init; }
    public IReadOnlyList<GuessResult> Guesses { get; init; } = [];
    public bool HasFinishedRound { get; init; }
    public bool Solved { get; init; }
    public bool Dnf { get; init; }
    public int TotalScore { get; init; }
    public int LastRoundPoints { get; init; }
    public int? FinishedAtElapsedMs { get; init; }
}

/// <summary>
/// One leaderboard entry's PROGRESS only — never their guess words/letters (those
/// reveal solved letters). The compile-time absence of a guesses field is the leak
/// guarantee. The recipient receives one of these per OTHER participant; their own
/// row is rendered from <see cref="MyBoardView"/> (which carries the same Rank).
/// </summary>
public sealed record RivalView(
    int Rank,
    Guid UserId,
    string DisplayName,
    int GuessCount,
    int MaxGuesses,
    bool HasFinishedRound,
    bool Solved,
    bool Dnf,
    int TotalScore,
    int? FinishedAtElapsedMs);

/// <summary>A full board in the host-observer gallery (every player's grid; observer never competes).</summary>
public sealed record ObserverBoardView(
    Guid UserId,
    string DisplayName,
    IReadOnlyList<GuessResult> Guesses,
    int MaxGuesses,
    bool HasFinishedRound,
    bool Solved,
    bool Dnf,
    int? FinishedAtElapsedMs);

/// <summary>The just-completed round's outcomes. <see cref="Answer"/> is null unless RevealAnswer is on.</summary>
public sealed record RoundResultView(
    int RoundNumber,
    string? Answer,
    IReadOnlyList<PlayerOutcomeView> Outcomes);

/// <summary>One player's outcome in a completed round. <see cref="TotalScore"/> is the post-round cumulative.</summary>
public sealed record PlayerOutcomeView(
    Guid UserId,
    string DisplayName,
    int GuessCount,
    bool Dnf,
    int PointsAwarded,
    int Placement,
    int TotalScore,
    int? FinishedAtElapsedMs);

/// <summary>One row of the game-over standings.</summary>
public sealed record PlayerStandingView(
    Guid UserId,
    string DisplayName,
    int Rank,
    int TotalScore,
    int RoundsWon,
    bool IsHost);
