using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.WordServiceTests.Unit;

[TestClass]
public class WordListServiceTests
{
    private static IWordListService _service = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _service = new WordListService(NullLogger<WordListService>.Instance);
    }

    [TestMethod]
    public void IsValidWord_ReturnsTrue_ForGoogleCommonWord()
    {
        Assert.IsTrue(_service.IsValidWord("the"));
    }

    [TestMethod]
    public void IsValidWord_ReturnsTrue_ForNyWord()
    {
        Assert.IsTrue(_service.IsValidWord("aback"));
    }

    [TestMethod]
    public void IsValidWord_IsCaseInsensitive()
    {
        Assert.IsTrue(_service.IsValidWord("ABOUT"));
        Assert.IsTrue(_service.IsValidWord("about"));
        Assert.IsTrue(_service.IsValidWord("About"));
    }

    [TestMethod]
    public void IsValidWord_ReturnsFalse_ForGarbage()
    {
        Assert.IsFalse(_service.IsValidWord("xyzzyq"));
        Assert.IsFalse(_service.IsValidWord("qqqqq"));
    }

    [TestMethod]
    public void IsValidWord_AcceptsSpanFromSubstringRange()
    {
        ReadOnlySpan<char> span = "thesun".AsSpan(0, 3);
        Assert.IsTrue(_service.IsValidWord(span));
    }

    [TestMethod]
    public void IsInPool_NytStandard_ContainsAback()
    {
        Assert.IsTrue(_service.IsInPool(WordPoolMode.NytStandard, "aback"));
    }

    [TestMethod]
    public void IsInPool_NytStandard_ExcludesGoogleOnlyWords()
    {
        Assert.IsFalse(_service.IsInPool(WordPoolMode.NytStandard, "the"));
    }

    [TestMethod]
    public void IsInPool_FullDictionary_ContainsNytAndGoogleWords()
    {
        Assert.IsTrue(_service.IsInPool(WordPoolMode.FullDictionary, "aback"));
        Assert.IsTrue(_service.IsInPool(WordPoolMode.FullDictionary, "the"));
    }

    [TestMethod]
    public void Reduced_ShipsCommonWords_AndIsAStrictSubsetOfFull()
    {
        // reduced-dictionary.csv ships the curated common-word (Google-10k) list, so the pool
        // is populated and answers lookups directly.
        Assert.IsTrue(_service.GetAvailableLengths(WordPoolMode.ReducedDictionary).Any());
        Assert.IsGreaterThan(0, _service.GetWordCount(WordPoolMode.ReducedDictionary, 5));
        Assert.IsTrue(_service.IsInPool(WordPoolMode.ReducedDictionary, "about"));

        // It is a common-word subset: fewer five-letter words than the full dictionary.
        Assert.IsLessThan(
            _service.GetWordCount(WordPoolMode.FullDictionary, 5),
            _service.GetWordCount(WordPoolMode.ReducedDictionary, 5));
    }

    [TestMethod]
    public void IsInPool_UnknownMode_ReturnsFalse()
    {
        Assert.IsFalse(_service.IsInPool((WordPoolMode)(-1), "apple"));
    }

    [TestMethod]
    public void GetWordCount_NytStandard_HasAllFiveLetterAnswers()
    {
        Assert.IsGreaterThan(2000, _service.GetWordCount(WordPoolMode.NytStandard, 5));
    }

    [TestMethod]
    public void GetWordCount_FullDictionary_HasMultipleLengths()
    {
        Assert.IsGreaterThan(0, _service.GetWordCount(WordPoolMode.FullDictionary, 3));
        Assert.IsGreaterThan(0, _service.GetWordCount(WordPoolMode.FullDictionary, 5));
        Assert.IsGreaterThan(0, _service.GetWordCount(WordPoolMode.FullDictionary, 7));
    }

    [TestMethod]
    public void GetWordCount_LengthWithNoWords_ReturnsZero()
    {
        Assert.AreEqual(0, _service.GetWordCount(WordPoolMode.NytStandard, 100));
    }

    [TestMethod]
    public void GetWordCount_UnknownMode_ReturnsZero()
    {
        Assert.AreEqual(0, _service.GetWordCount((WordPoolMode)(-1), 5));
    }

    [TestMethod]
    public void GetWord_ReturnsValidWordOfRequestedLength()
    {
        int count = _service.GetWordCount(WordPoolMode.NytStandard, 5);
        var firstBytes = _service.GetWord(WordPoolMode.NytStandard, 5, 0);
        Assert.AreEqual(5, firstBytes.Length);
        var first = System.Text.Encoding.ASCII.GetString(firstBytes);
        Assert.IsTrue(_service.IsInPool(WordPoolMode.NytStandard, first));

        var lastBytes = _service.GetWord(WordPoolMode.NytStandard, 5, count - 1);
        Assert.AreEqual(5, lastBytes.Length);
        var last = System.Text.Encoding.ASCII.GetString(lastBytes);
        Assert.IsTrue(_service.IsInPool(WordPoolMode.NytStandard, last));
    }

    [TestMethod]
    public void GetWord_FirstNytEntryIsAback()
    {
        var bytes = _service.GetWord(WordPoolMode.NytStandard, 5, 0);
        Assert.AreEqual("aback", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [TestMethod]
    public void GetWordAsString_DecodesSpanToString()
    {
        var word = _service.GetWordAsString(WordPoolMode.NytStandard, 5, 0);
        Assert.AreEqual("aback", word);
    }

    [TestMethod]
    public void GetWord_OutOfRangeIndex_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => { _ = _service.GetWord(WordPoolMode.NytStandard, 5, -1); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => { _ = _service.GetWord(WordPoolMode.NytStandard, 5, int.MaxValue); });
    }

    [TestMethod]
    public void GetWord_UnknownMode_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => { _ = _service.GetWord((WordPoolMode)(-1), 5, 0); });
    }

    [TestMethod]
    public void GetWord_LengthWithNoWords_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => { _ = _service.GetWord(WordPoolMode.NytStandard, 100, 0); });
    }

    [TestMethod]
    public void GetAvailableLengths_NytStandard_ContainsOnlyFive()
    {
        var lengths = _service.GetAvailableLengths(WordPoolMode.NytStandard).ToList();
        CollectionAssert.AreEqual(new[] { 5 }, lengths);
    }

    [TestMethod]
    public void GetAvailableLengths_FullDictionary_IsSortedAscending()
    {
        var lengths = _service.GetAvailableLengths(WordPoolMode.FullDictionary).ToList();
        Assert.IsGreaterThan(1, lengths.Count);
        CollectionAssert.AreEqual(lengths.OrderBy(x => x).ToList(), lengths);
        Assert.Contains(5, lengths);
    }

    [TestMethod]
    public void GetAvailableLengths_UnknownMode_ReturnsEmpty()
    {
        Assert.IsEmpty(_service.GetAvailableLengths((WordPoolMode)(-1)));
    }

    [TestMethod]
    public void RegisterCustomPool_BuildsLengthBucketedPool()
    {
        var pool = _service.RegisterCustomPool(
            "custom-builds",
            new[] { "cat", "dog", "apple", "brave", "Apple" });

        // "Apple" dedupes against "apple"; total distinct = 4.
        Assert.AreEqual(4, pool.TotalWordCount);
        CollectionAssert.AreEqual(new[] { 3, 5 }, pool.AvailableLengths.ToArray());
        Assert.IsTrue(pool.Contains("apple"));
    }

    [TestMethod]
    public void RegisterCustomPool_IsIdempotentByName()
    {
        var first = _service.RegisterCustomPool("custom-idempotent", new[] { "cat", "dog" });
        // Same name, different words — the original pool is returned unchanged.
        var second = _service.RegisterCustomPool("custom-idempotent", new[] { "apple", "brave", "crane" });

        Assert.AreSame(first, second);
        Assert.AreEqual(2, second.TotalWordCount);
    }

    [TestMethod]
    public void GetCustomPool_ReturnsRegisteredPool_OrNull()
    {
        var registered = _service.RegisterCustomPool("custom-lookup", new[] { "cat", "dog" });

        Assert.AreSame(registered, _service.GetCustomPool("custom-lookup"));
        Assert.IsNull(_service.GetCustomPool("never-registered"));
    }
}
