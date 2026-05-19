using System.Text;

namespace KnockBox.WordService.Contracts;

/// <summary>
/// Ergonomic wrappers around <see cref="IWordListService"/>. The core API exposes
/// raw spans for zero-allocation hot paths; these helpers allocate to make calling
/// code shorter when the caller actually needs a <see cref="string"/>.
/// </summary>
public static class WordListServiceExtensions
{
    /// <summary>
    /// Returns the <paramref name="index"/>-th word of length <paramref name="length"/>
    /// in <paramref name="mode"/> as a freshly allocated <see cref="string"/>.
    /// Prefer <see cref="IWordListService.GetWord"/> when the caller can consume bytes
    /// directly — this overload always allocates.
    /// </summary>
    public static string GetWordAsString(
        this IWordListService service, WordPoolMode mode, int length, int index)
        => Encoding.ASCII.GetString(service.GetWord(mode, length, index));
}
