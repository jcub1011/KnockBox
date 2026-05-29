namespace KnockBox.WordService.Contracts;

/// <summary>
/// Identifies a word pool that <see cref="IWordListService"/> can answer
/// lookups against.
/// </summary>
public enum WordPoolMode
{
    /// <summary>The NYT Wordle daily answer list.</summary>
    NytStandard,

    /// <summary>Union of NYT plus Google-10k common words.</summary>
    FullDictionary,

    /// <summary>
    /// Pool defined by the host (e.g., a custom list configured by an admin).
    /// The library plugin does not back this pool — implementations return
    /// false / 0 / empty for queries against this mode unless an upper layer
    /// provides the data.
    /// </summary>
    HostDefined,

    /// <summary>
    /// Pool sourced from a CSV upload at lobby creation time. The library
    /// plugin does not back this pool — implementations return
    /// false / 0 / empty for queries against this mode unless an upper layer
    /// provides the data.
    /// </summary>
    CsvUpload,

    /// <summary>
    /// Curated list of common, recognizable words, backed by
    /// <c>reduced-dictionary.csv</c>. Intended for board generation so boards are
    /// dense with words a normal player knows, while a wider pool can still
    /// validate answers. Until the CSV ships this pool is empty and callers should
    /// fall back to <see cref="FullDictionary"/>.
    /// </summary>
    Reduced,
}
