using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;

namespace KnockBox.WordServiceTests.Unit;

[TestClass]
public class WordPoolExtensionsTests
{
    private static IWordPool BuildPool() => new CustomWordPool(new Dictionary<int, WordPool>
    {
        [3] = WordPool.Build(3, new[] { "cat", "dog", "ant" }),
        [5] = WordPool.Build(5, new[] { "apple", "brave" }),
    });

    // Deterministic stand-in for rng.GetRandomInt: dequeues pre-supplied values.
    private static Func<int, int> Sequence(params int[] values)
    {
        var queue = new Queue<int>(values);
        return _ => queue.Count > 0 ? queue.Dequeue() : 0;
    }

    [TestMethod]
    public void RandomDistinctPair_ReturnsTwoDistinctNonEmptyWords()
    {
        var (start, destination) = BuildPool().RandomDistinctPair(Sequence(1, 2));

        Assert.IsFalse(string.IsNullOrWhiteSpace(start));
        Assert.IsFalse(string.IsNullOrWhiteSpace(destination));
        Assert.AreNotEqual(start, destination);
    }

    [TestMethod]
    public void RandomDistinctPair_IsDeterministicForGivenDraws()
    {
        // Global order: ant(0) cat(1) dog(2) apple(3) brave(4).
        // i = 3 (apple); j = 1 < i, so no bump -> cat.
        var (start, destination) = BuildPool().RandomDistinctPair(Sequence(3, 1));
        Assert.AreEqual("apple", start);
        Assert.AreEqual("cat", destination);
    }

    [TestMethod]
    public void RandomDistinctPair_SkipsStartIndex_WhenSecondDrawIsAtOrAboveIt()
    {
        // i = 0, j = 0 -> j >= i bumps j to 1: ant, cat.
        var (start, destination) = BuildPool().RandomDistinctPair(Sequence(0, 0));
        Assert.AreEqual("ant", start);
        Assert.AreEqual("cat", destination);
    }

    [TestMethod]
    public void RandomDistinctPair_Throws_WhenPoolHasFewerThanTwoWords()
    {
        var tiny = new CustomWordPool(new Dictionary<int, WordPool>
        {
            [3] = WordPool.Build(3, new[] { "cat" }),
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => tiny.RandomDistinctPair(Sequence(0)));
    }
}
