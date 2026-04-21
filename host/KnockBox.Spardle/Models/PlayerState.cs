namespace KnockBox.Spardle.Models;

public class PlayerState
{
    public int Score { get; set; }
    public List<GuessResult> Guesses { get; } = new();
    public bool HasFinishedRound { get; set; }
    public bool Dnf { get; set; }
    public DateTime? FinishedAt { get; set; }

    public void ResetRound()
    {
        Guesses.Clear();
        HasFinishedRound = false;
        Dnf = false;
        FinishedAt = null;
    }
}
