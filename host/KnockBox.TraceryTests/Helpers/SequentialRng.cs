using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.Tracery.Tests.Helpers
{
    /// <summary>
    /// Deterministic RNG that returns a queue of pre-supplied values. Use the queue form
    /// (not a counter) so generation tests can encode the exact letter-draw sequence under
    /// test. Each <see cref="LetterDistribution"/> draw consumes one value, so a 2×2 board's
    /// first attempt dequeues four values, etc. (Mirrors DndMapper's test double.)
    /// </summary>
    internal sealed class SequentialRng : IRandomNumberService
    {
        private readonly Queue<int> _values;

        public SequentialRng(params int[] values) => _values = new Queue<int>(values);

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
