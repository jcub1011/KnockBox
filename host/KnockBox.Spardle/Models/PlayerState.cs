using System.Collections.Immutable;

namespace KnockBox.Spardle.Models;

public class PlayerState
{
    public int TotalScore { get; set; }
    public int LastRoundPoints { get; set; }
    public ImmutableList<GuessResult> Guesses { get; set; } = [];
    public bool HasFinishedRound { get; set; }
    public bool Dnf { get; set; }
    public DateTime? FinishedAt { get; set; }

    public void ResetRound()
    {
        Guesses = Guesses.Clear();
        HasFinishedRound = false;
        Dnf = false;
        FinishedAt = null;
        LastRoundPoints = 0;
    }
}
