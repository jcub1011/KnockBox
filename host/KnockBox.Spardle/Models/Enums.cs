namespace KnockBox.Spardle.Models;

public enum LetterStatus
{
    Absent,
    Present,
    Correct
}

// WordPoolMode moved to KnockBox.WordService.Contracts so the shared word-service
// library plugin owns the enum alongside IWordListService.

public enum WordOrderMode
{
    RandomNoRepeats,
    RandomWithRepeats,
    ListOrder,
    ReverseListOrder
}

public enum WinConditionMode
{
    Sprinter, // First to solve wins
    Tactician // Fewest guesses wins
}
