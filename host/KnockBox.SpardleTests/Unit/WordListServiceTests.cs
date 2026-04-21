using KnockBox.Spardle.Models;
using KnockBox.Spardle.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.SpardleTests.Unit;

[TestClass]
public class WordListServiceTests
{
    private static WordListService _service = default!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _service = new WordListService(NullLogger<WordListService>.Instance);
    }

    [TestMethod]
    public void Ctor_LoadsBothFiles_FromDeployedDataDir()
    {
        Assert.IsGreaterThan(9000, _service.GetFullDictionary().Count);
        Assert.IsGreaterThan(2000, _service.GetTargetWordPool(WordPoolMode.NytStandard).Count);
    }

    [TestMethod]
    public void FullDictionary_ContainsAllNytWords()
    {
        var full = _service.GetFullDictionary();
        var ny = _service.GetTargetWordPool(WordPoolMode.NytStandard);

        foreach (var word in ny)
        {
            Assert.Contains(word, full, $"NY word '{word}' missing from merged full dictionary.");
        }
    }

    [TestMethod]
    public void IsValidWord_ReturnsTrue_ForGoogleCommonWord()
    {
        // "the" is in google-10000 but almost certainly not a NY 5-letter answer.
        Assert.IsTrue(_service.IsValidWord("the"));
    }

    [TestMethod]
    public void IsValidWord_ReturnsTrue_ForNyWord()
    {
        // "aback" is the first NY entry; verifies NY words are included after merge.
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
    public void GetTargetWordPool_NytStandard_ExcludesGoogleOnlyWords()
    {
        var ny = _service.GetTargetWordPool(WordPoolMode.NytStandard);
        // "the" is not a valid 5-letter NYT answer.
        Assert.DoesNotContain("the", ny);
    }

    [TestMethod]
    public void GetTargetWordPool_FullDictionary_ReturnsMergedSet()
    {
        Assert.AreSame(_service.GetFullDictionary(), _service.GetTargetWordPool(WordPoolMode.FullDictionary));
    }

    [TestMethod]
    [DataRow(WordPoolMode.HostDefined)]
    [DataRow(WordPoolMode.CsvUpload)]
    public void GetTargetWordPool_UnscopedModes_ReturnEmpty(WordPoolMode mode)
    {
        Assert.IsEmpty(_service.GetTargetWordPool(mode));
    }
}
