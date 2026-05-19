using KnockBox.WordService.Services;

namespace KnockBox.WordServiceTests.Unit;

[TestClass]
public class WordPoolTests
{
    [TestMethod]
    public void Build_FromMixedInput_DedupesTrimsLowercases()
    {
        var pool = WordPool.Build(wordLength: 5, words: new[] { "Apple", " APPLE ", "brave", "crane", "" });

        Assert.AreEqual(5, pool.WordLength);
        Assert.AreEqual(3, pool.WordCount);
        Assert.IsTrue(pool.Contains("apple"));
        Assert.IsTrue(pool.Contains("brave"));
        Assert.IsTrue(pool.Contains("crane"));
    }

    [TestMethod]
    public void Build_SkipsWordsOfWrongLength()
    {
        var pool = WordPool.Build(wordLength: 4, words: new[] { "tree", "hello", "blue", "x" });
        Assert.AreEqual(2, pool.WordCount);
        Assert.IsTrue(pool.Contains("tree"));
        Assert.IsTrue(pool.Contains("blue"));
        Assert.IsFalse(pool.Contains("hello"));
    }

    [TestMethod]
    public void Contains_IsCaseInsensitive()
    {
        var pool = WordPool.Build(5, new[] { "apple" });
        Assert.IsTrue(pool.Contains("APPLE"));
        Assert.IsTrue(pool.Contains("Apple"));
        Assert.IsTrue(pool.Contains("aPpLe"));
    }

    [TestMethod]
    public void Contains_ReturnsFalse_ForMissingWord()
    {
        var pool = WordPool.Build(5, new[] { "apple", "brave" });
        Assert.IsFalse(pool.Contains("crane"));
    }

    [TestMethod]
    public void Contains_ReturnsFalse_ForWrongLength()
    {
        var pool = WordPool.Build(5, new[] { "apple" });
        Assert.IsFalse(pool.Contains("app"));
        Assert.IsFalse(pool.Contains("apples"));
    }

    [TestMethod]
    public void Contains_ReturnsFalse_ForEmptyQuery()
    {
        var pool = WordPool.Build(5, new[] { "apple" });
        Assert.IsFalse(pool.Contains([]));
    }

    [TestMethod]
    public void Contains_ReturnsFalse_ForNonAsciiQuery()
    {
        var pool = WordPool.Build(4, new[] { "cafe" });
        Assert.IsFalse(pool.Contains("café"));
    }

    [TestMethod]
    public void Contains_WorksOnEmptyPool()
    {
        var pool = WordPool.Build(5, Array.Empty<string>());
        Assert.AreEqual(0, pool.WordCount);
        Assert.IsFalse(pool.Contains("apple"));
    }

    [TestMethod]
    public void GetWord_ReturnsWordsInSortedOrder()
    {
        var pool = WordPool.Build(5, new[] { "crane", "apple", "brave" });
        Assert.AreEqual("apple", System.Text.Encoding.ASCII.GetString(pool.GetWord(0)));
        Assert.AreEqual("brave", System.Text.Encoding.ASCII.GetString(pool.GetWord(1)));
        Assert.AreEqual("crane", System.Text.Encoding.ASCII.GetString(pool.GetWord(2)));
    }

    [TestMethod]
    public void GetWord_ThrowsForOutOfRangeIndex()
    {
        var pool = WordPool.Build(5, new[] { "apple" });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = pool.GetWord(-1); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = pool.GetWord(1); });
    }
}
