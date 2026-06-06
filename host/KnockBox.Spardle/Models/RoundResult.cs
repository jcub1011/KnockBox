namespace KnockBox.Spardle.Models;

public class RoundResult
{
    public int RoundNumber { get; init; }
    public string Answer { get; init; } = string.Empty;
    public List<PlayerRoundOutcome> Outcomes { get; init; } = [];
}

public class PlayerRoundOutcome
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int GuessCount { get; init; }
    public DateTime? FinishedAt { get; init; }
    public bool Dnf { get; init; }
    public int PointsAwarded { get; init; }
    public int Placement { get; init; }
}
