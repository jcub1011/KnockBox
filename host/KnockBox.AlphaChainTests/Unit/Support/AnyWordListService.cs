using KnockBox.WordService.Contracts;

namespace KnockBox.AlphaChain.Tests.Unit.Support
{
    /// <summary>
    /// Permissive <see cref="IWordListService"/> double for the full-game simulation: any
    /// non-empty all-letter token is a valid word. The simulation generates chained, unique,
    /// banned-letter-free tokens on the fly, so this lets it drive a complete match without
    /// hand-curating a dictionary that satisfies every required start letter.
    /// </summary>
    internal sealed class AnyWordListService : IWordListService
    {
        public bool IsValidWord(ReadOnlySpan<char> word)
        {
            if (word.Length == 0) return false;
            foreach (var c in word)
                if (c is < 'a' or > 'z') return false;
            return true;
        }

        public bool IsInPool(WordPoolMode mode, ReadOnlySpan<char> word) => IsValidWord(word);

        public int GetWordCount(WordPoolMode mode, int length) => 0;

        public ReadOnlySpan<byte> GetWord(WordPoolMode mode, int length, int index) => default;

        public IEnumerable<int> GetAvailableLengths(WordPoolMode mode) => [];

        public IWordPool RegisterCustomPool(string name, IEnumerable<string> words)
            => throw new NotSupportedException();

        public IWordPool? GetCustomPool(string name) => null;
    }
}
