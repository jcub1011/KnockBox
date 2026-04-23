namespace KnockBox.Spardle.Models;

public class GuessResult
{
    public string Word { get; init; } = string.Empty;
    public LetterStatus[] Statuses { get; init; } = [];
    public bool IsCorrect { get; init; }
}
