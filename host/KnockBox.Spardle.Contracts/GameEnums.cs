namespace KnockBox.Spardle.Models;

// Moved from the server plugin into the shared Contracts assembly (keeping the
// KnockBox.Spardle.Models namespace) so both the server and the streamed WASM
// client bind the same CLR types. The server code that `using`s this namespace
// is unchanged.

public enum LetterStatus
{
    Absent,
    Present,
    Correct
}

/// <summary>
/// Word source a Spardle host picks in the lobby. NytStandard and FullDictionary are
/// served by the shared word-service library; HostDefined and CsvUpload are Spardle-local
/// pools the host supplies at lobby time (typed in or CSV-uploaded), stored on
/// SpardleState.CustomWordPool and never backed by IWordListService.
/// </summary>
public enum SpardleWordSource
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
