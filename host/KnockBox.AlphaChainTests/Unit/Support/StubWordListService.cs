using KnockBox.WordService.Contracts;

namespace KnockBox.AlphaChain.Tests.Unit.Support
{
    /// <summary>
    /// Hand-rolled <see cref="IWordListService"/> double for FSM tests. Moq cannot stub
    /// methods that take <see cref="ReadOnlySpan{T}"/> (a ref struct can't be an
    /// <c>It.IsAny&lt;&gt;</c> argument), so a small fake backed by a case-insensitive set
    /// gives the deterministic accept/reject control the milestone needs.
    /// </summary>
    internal sealed class StubWordListService(params string[] validWords) : IWordListService
    {
        private readonly HashSet<string> _valid = new(validWords, StringComparer.OrdinalIgnoreCase);

        public bool IsValidWord(ReadOnlySpan<char> word) => _valid.Contains(word.ToString());

        public bool IsInPool(WordPoolMode mode, ReadOnlySpan<char> word) => IsValidWord(word);

        public int GetWordCount(WordPoolMode mode, int length) => 0;

        public ReadOnlySpan<byte> GetWord(WordPoolMode mode, int length, int index) => default;

        public IEnumerable<int> GetAvailableLengths(WordPoolMode mode) => [];

        public IWordPool RegisterCustomPool(string name, IEnumerable<string> words)
            => throw new NotSupportedException();

        public IWordPool? GetCustomPool(string name) => null;
    }
}
