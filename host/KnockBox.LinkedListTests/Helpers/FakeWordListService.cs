using System.Text;
using KnockBox.WordService.Contracts;

namespace KnockBox.LinkedList.Tests.Helpers
{
    /// <summary>
    /// Minimal in-memory <see cref="IWordListService"/> for testing <c>WordSource</c>
    /// in isolation from the real WordService library. Mirrors the real
    /// build-from-words behaviour (trim, lowercase, dedupe, sort ordinal) and records
    /// the words registered under each pool name so tests can assert what was pushed.
    /// </summary>
    internal sealed class FakeWordListService : IWordListService
    {
        private readonly Dictionary<string, IWordPool> _pools = new(StringComparer.Ordinal);

        /// <summary>Words passed to <see cref="RegisterCustomPool"/>, keyed by pool name.</summary>
        public Dictionary<string, IReadOnlyList<string>> Registered { get; } = new(StringComparer.Ordinal);

        public IWordPool RegisterCustomPool(string name, IEnumerable<string> words)
        {
            if (_pools.TryGetValue(name, out var existing)) return existing;

            var list = words.ToList();
            Registered[name] = list;
            var pool = new FakeWordPool(list);
            _pools[name] = pool;
            return pool;
        }

        public IWordPool? GetCustomPool(string name)
            => _pools.TryGetValue(name, out var pool) ? pool : null;

        // Built-in pool surface is unused by WordSource.
        public bool IsValidWord(ReadOnlySpan<char> word) => false;
        public bool IsInPool(WordPoolMode mode, ReadOnlySpan<char> word) => false;
        public int GetWordCount(WordPoolMode mode, int length) => 0;
        public ReadOnlySpan<byte> GetWord(WordPoolMode mode, int length, int index) => throw new NotSupportedException();
        public IEnumerable<int> GetAvailableLengths(WordPoolMode mode) => [];

        private sealed class FakeWordPool : IWordPool
        {
            private readonly List<byte[]> _words;

            public FakeWordPool(IEnumerable<string> words)
            {
                _words = words
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .Select(w => w.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(w => w, StringComparer.Ordinal)
                    .Select(Encoding.ASCII.GetBytes)
                    .ToList();
            }

            public int TotalWordCount => _words.Count;
            public IReadOnlyList<int> AvailableLengths => _words.Select(w => w.Length).Distinct().OrderBy(x => x).ToList();
            public int GetWordCount(int length) => _words.Count(w => w.Length == length);
            public ReadOnlySpan<byte> GetWord(int globalIndex) => _words[globalIndex];
            public ReadOnlySpan<byte> GetWord(int length, int index) => _words.Where(w => w.Length == length).ElementAt(index);
            public bool Contains(ReadOnlySpan<char> word)
            {
                var query = word.ToString().ToLowerInvariant();
                return _words.Any(w => Encoding.ASCII.GetString(w) == query);
            }
        }
    }
}
