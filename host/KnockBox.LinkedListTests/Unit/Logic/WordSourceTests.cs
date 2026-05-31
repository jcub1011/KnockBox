using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Tests.Helpers;

namespace KnockBox.LinkedList.Tests.Unit.Logic
{
    [TestClass]
    public class WordSourceTests
    {
        [TestMethod]
        public void Words_LoadFromEmbeddedResource_NonEmpty()
        {
            var source = new WordSource();

            Assert.IsTrue(source.Words.Length >= 2);
            foreach (var word in source.Words)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(word));
            }
        }

        [TestMethod]
        public void RandomPair_ReturnsTwoDistinctNonEmptyWords()
        {
            var source = new WordSource();
            var rng = new SequentialRng(0, 0);

            var pair = source.RandomPair(rng);

            Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Start));
            Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Destination));
            Assert.AreNotEqual(pair.Start, pair.Destination);
        }

        [TestMethod]
        public void RandomPair_IsDeterministicWithStubbedRng()
        {
            var source = new WordSource();

            // Second draw (j) collides with i = 0, so j >= i bumps it to index 1.
            var pair = source.RandomPair(new SequentialRng(0, 0));

            Assert.AreEqual(source.Words[0], pair.Start);
            Assert.AreEqual(source.Words[1], pair.Destination);
        }

        [TestMethod]
        public void RandomPair_SkipsStartIndex_WhenSecondDrawIsAtOrAboveIt()
        {
            var source = new WordSource();

            // i = 2, j = 2 -> j >= i bumps to 3.
            var pair = source.RandomPair(new SequentialRng(2, 2));

            Assert.AreEqual(source.Words[2], pair.Start);
            Assert.AreEqual(source.Words[3], pair.Destination);
        }
    }
}
