using KnockBox.WordService.Contracts;

namespace KnockBox.AlphaChain.Pages.Bench
{
    /// <summary>
    /// Permissive <see cref="IWordListService"/> for the hidden card bench: any non-empty all-letter
    /// token validates, so a balancer can type contrived words to force an exact score or land on a
    /// banned letter without hunting for a real dictionary entry that fits. The pool methods are inert
    /// (the bench never draws words from a pool). Production-side twin of the test-only
    /// <c>AnyWordListService</c> — kept here so the bench page can drive the real engine without
    /// depending on the test project.
    /// </summary>
    internal sealed class BenchWordListService : IWordListService
    {
        public bool IsValidWord(ReadOnlySpan<char> word)
        {
            // The engine normalizes (trims + lower-cases) the raw submission before it reaches the word
            // list, so an all-lower-case a–z check is sufficient here; mixed-case/whitespace never arrives.
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
