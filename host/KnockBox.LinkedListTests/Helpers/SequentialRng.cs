using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.LinkedList.Tests.Helpers
{
    /// <summary>
    /// Deterministic RNG that returns a queue of pre-supplied values, so tests can
    /// pin exactly which words <c>WordSource.RandomPair</c> picks.
    /// </summary>
    internal sealed class SequentialRng : IRandomNumberService
    {
        private readonly Queue<int> _values;

        public SequentialRng(params int[] values) => _values = new Queue<int>(values);

        public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast)
            => _values.Count > 0 ? _values.Dequeue() : 0;

        public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast)
            => _values.Count > 0 ? _values.Dequeue() : inclusiveMin;

        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast)
            => new byte[length];
    }
}
