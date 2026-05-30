using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Tests.Helpers;

namespace KnockBox.LinkedList.Tests.Unit.Logic
{
    [TestClass]
    public class WordPairSourceTests
    {
        [TestMethod]
        public void Pairs_LoadFromEmbeddedResource_NonEmpty()
        {
            var source = new WordPairSource();

            Assert.IsTrue(source.Pairs.Length > 0);
            foreach (var pair in source.Pairs)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Start));
                Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Destination));
            }
        }

        [TestMethod]
        public void Random_ReturnsNonEmptyPair()
        {
            var source = new WordPairSource();
            var rng = new SequentialRng(0);

            var pair = source.Random(rng);

            Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Start));
            Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Destination));
        }

        [TestMethod]
        public void Random_IsDeterministicWithStubbedRng()
        {
            var source = new WordPairSource();

            var first = source.Random(new SequentialRng(0));
            var second = source.Random(new SequentialRng(2));

            Assert.AreEqual(source.Pairs[0], first);
            Assert.AreEqual(source.Pairs[2], second);
        }
    }
}
