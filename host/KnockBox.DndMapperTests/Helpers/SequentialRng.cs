using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.DndMapperTests.Helpers
{
    /// <summary>
    /// Deterministic RNG that returns a queue of pre-supplied values.
    /// Use the queue form (not a counter) so dice tests can encode the exact
    /// roll sequence under test.
    /// </summary>
    internal sealed class SequentialRng : IRandomNumberService
    {
        private readonly Queue<int> _values;

        public SequentialRng(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public void Enqueue(params int[] values)
        {
            foreach (var v in values) _values.Enqueue(v);
        }

        public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast)
            => _values.Dequeue();

        public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast)
            => _values.Dequeue();

        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast)
            => new byte[length];
    }
}
