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
    public void Reduced_IsEmptyUntilItsCsvShips_AndQueriesAreGraceful()
    {
        // reduced-dictionary.csv is not in the repo yet; an absent file yields an empty pool
        // (LoadCsv warns + returns []) rather than a crash. Adjust/remove once the list ships.
        Assert.AreEqual(0, _service.GetWordCount(WordPoolMode.Reduced, 5));
        Assert.IsFalse(_service.GetAvailableLengths(WordPoolMode.Reduced).Any());
        Assert.IsFalse(_service.IsInPool(WordPoolMode.Reduced, "about"));
    }

    [TestMethod]
    [DataRow(WordPoolMode.HostDefined)]
    [DataRow(WordPoolMode.CsvUpload)]
    public void IsInPool_UnbackedModes_ReturnFalse(WordPoolMode mode)
    {
        Assert.IsFalse(_service.IsInPool(mode, "apple"));
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
    public void GetWordCount_UnbackedMode_ReturnsZero()
    {
        Assert.AreEqual(0, _service.GetWordCount(WordPoolMode.HostDefined, 5));
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
    public void GetWord_UnbackedMode_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => { _ = _service.GetWord(WordPoolMode.HostDefined, 5, 0); });
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
    public void GetAvailableLengths_UnbackedMode_ReturnsEmpty()
    {
        Assert.IsEmpty(_service.GetAvailableLengths(WordPoolMode.HostDefined));
        Assert.IsEmpty(_service.GetAvailableLengths(WordPoolMode.CsvUpload));
    }
}
