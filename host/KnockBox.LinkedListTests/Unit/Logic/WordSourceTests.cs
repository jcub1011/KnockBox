using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Tests.Helpers;

namespace KnockBox.LinkedList.Tests.Unit.Logic
{
    [TestClass]
    public class WordSourceTests
    {
        [TestMethod]
        public void Ctor_RegistersEmbeddedAuditedList_AsCustomPool()
        {
            var words = new FakeWordListService();

            var source = new WordSource(words);

            Assert.IsTrue(words.Registered.ContainsKey(WordSource.PoolName));
            var registered = words.Registered[WordSource.PoolName];
            Assert.IsGreaterThanOrEqualTo(2, registered.Count);
            Assert.IsTrue(registered.All(w => !string.IsNullOrWhiteSpace(w)));
            // The embedded list is stored lowercase, one word per line.
            Assert.IsTrue(registered.All(w => w == w.ToLowerInvariant()));
            Assert.AreEqual(registered.Count, source.WordCount);
        }

        [TestMethod]
        public void RandomPair_ReturnsTwoDistinctNonEmptyUppercaseWords()
        {
            var source = new WordSource(new FakeWordListService());
            var rng = new SequentialRng(0, 0);

            var pair = source.RandomPair(rng);

            Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Start));
            Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Destination));
            Assert.AreNotEqual(pair.Start, pair.Destination);
            Assert.AreEqual(pair.Start, pair.Start.ToUpperInvariant());
            Assert.AreEqual(pair.Destination, pair.Destination.ToUpperInvariant());
        }

        [TestMethod]
        public void RandomPair_IsDeterministicWithStubbedRng()
        {
            var source = new WordSource(new FakeWordListService());

            var first = source.RandomPair(new SequentialRng(0, 0));
            var second = source.RandomPair(new SequentialRng(0, 0));

            Assert.AreEqual(first, second);
        }
    }
}
