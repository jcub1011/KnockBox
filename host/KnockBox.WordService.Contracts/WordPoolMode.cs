namespace KnockBox.WordService.Contracts;

/// <summary>
/// Identifies a word pool that <see cref="IWordListService"/> can answer
/// lookups against.
/// </summary>
public enum WordPoolMode
{
    /// <summary>The NYT Wordle daily answer list.</summary>
    NytStandard,

    /// <summary>Google-10k common words.</summary>
    ReducedDictionary,

    /// <summary>Union of NYT plus 350k+ dictionary words.</summary>
    FullDictionary,
}
