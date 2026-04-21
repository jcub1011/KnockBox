namespace KnockBox.Spardle.Models;

public enum LetterStatus
{
    Absent,
    Present,
    Correct
}

public enum WordPoolMode
{
    NytStandard,
    FullDictionary,
    HostDefined,
    CsvUpload
}

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
