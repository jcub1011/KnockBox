using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Logic.Filtering
{
    public readonly record struct ProfanityMatch(int StartIndex, int Length);

    public interface IProfanityFilter
    {
        /// <summary>
        /// Extracts a list of profanities from the provided text.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="ct"></param>
        /// <returns>
        /// A success result whose value is the list of matches, or <c>null</c> when none
        /// are found; a failure result if extraction fails; or a cancellation result.
        /// </returns>
        ValueTask<ValueResult<List<ProfanityMatch>?>> ExtractProfanitiesAsync(string text, CancellationToken ct = default);
    }
}
