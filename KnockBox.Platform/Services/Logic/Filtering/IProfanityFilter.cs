namespace KnockBox.Services.Logic.Filtering
{
    public readonly record struct ProfanityMatch(int StartIndex, int Length);

    public interface IProfanityFilter
    {
        /// <summary>
        /// Extracts a list of profanities from the provided text.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="ct"></param>
        /// <returns>Null if none are found.</returns>
        ValueTask<List<ProfanityMatch>?> ExtractProfanitiesAsync(string text, CancellationToken ct = default);
    }
}
