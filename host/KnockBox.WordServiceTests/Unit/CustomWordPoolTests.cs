using System.Text;
using KnockBox.WordService.Services;

namespace KnockBox.WordServiceTests.Unit;

[TestClass]
public class CustomWordPoolTests
{
    private static CustomWordPool BuildPool() => new(new Dictionary<int, WordPool>
    {
        [3] = WordPool.Build(3, new[] { "cat", "dog", "ant" }),
        [5] = WordPool.Build(5, new[] { "apple", "brave" }),
    });

    [TestMethod]
    public void TotalWordCount_SumsAllLengthBuckets()
    {
        Assert.AreEqual(5, BuildPool().TotalWordCount);
    }

    [TestMethod]
    public void AvailableLengths_AreSortedAscending()
    {
        CollectionAssert.AreEqual(new[] { 3, 5 }, BuildPool().AvailableLengths.ToArray());
    }

    [TestMethod]
    public void GetWordCount_ReturnsPerLengthCount()
    {
        var pool = BuildPool();
        Assert.AreEqual(3, pool.GetWordCount(3));
        Assert.AreEqual(2, pool.GetWordCount(5));
        Assert.AreEqual(0, pool.GetWordCount(4));
    }

    [TestMethod]
    public void GetWord_ByGlobalIndex_WalksBucketsInLengthThenOrdinalOrder()
    {
        var pool = BuildPool();

        // Length 3 bucket (ordinal): ant, cat, dog -> global 0,1,2
        Assert.AreEqual("ant", Decode(pool.GetWord(0)));
        Assert.AreEqual("cat", Decode(pool.GetWord(1)));
        Assert.AreEqual("dog", Decode(pool.GetWord(2)));
        // Length 5 bucket (ordinal): apple, brave -> global 3,4
        Assert.AreEqual("apple", Decode(pool.GetWord(3)));
        Assert.AreEqual("brave", Decode(pool.GetWord(4)));
    }

    [TestMethod]
    public void GetWord_ByGlobalIndex_CoversExactlyTheDedupedInputSet()
    {
        var pool = BuildPool();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < pool.TotalWordCount; i++)
            seen.Add(Decode(pool.GetWord(i)));

        CollectionAssert.AreEquivalent(
            new[] { "ant", "cat", "dog", "apple", "brave" },
            seen.ToArray());
    }

    [TestMethod]
    public void GetWord_ByGlobalIndex_OutOfRange_Throws()
    {
        var pool = BuildPool();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = pool.GetWord(-1); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = pool.GetWord(5); });
    }

    [TestMethod]
    public void Contains_DelegatesToLengthBucket()
    {
        var pool = BuildPool();
        Assert.IsTrue(pool.Contains("APPLE"));
        Assert.IsTrue(pool.Contains("ant"));
        Assert.IsFalse(pool.Contains("zebra"));
    }

    private static string Decode(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);
}
