namespace KnockBox.Spardle.Models;

// Moved into the shared Contracts assembly (keeping the KnockBox.Spardle.Models
// namespace). This is the per-row board cell the client grid renders — the
// guessed word plus its per-letter feedback. Statuses stays a plain array (an
// array of string-enums round-trips cleanly; do NOT make it an enum-keyed dict).
public class GuessResult
{
    public string Word { get; init; } = string.Empty;
    public LetterStatus[] Statuses { get; init; } = [];
    public bool IsCorrect { get; init; }
}
