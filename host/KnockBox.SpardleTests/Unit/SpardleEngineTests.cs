using KnockBox.Spardle;
using KnockBox.Spardle.Models;
using KnockBox.Spardle.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.SpardleTests.Unit;

[TestClass]
public class SpardleEngineTests
{
    private SpardleEngine _engine = default!;

    [TestInitialize]
    public void Setup()
    {
        _engine = new SpardleEngine(new WordListService(), new NullLoggerFactory());
    }

    [TestMethod]
    [DataRow(5, 2.0, 6)] // Standard 5-letter
    [DataRow(10, 2.0, 7)] // G = 6 + 2 * ln(2) = 6 + 1.38 = 7
    [DataRow(15, 2.0, 8)] // G = 6 + 2 * ln(3) = 6 + 2.19 = 8
    public void CalculateMaxGuesses_ReturnsExpected(int length, double multiplier, int expected)
    {
        int result = SpardleEngine.CalculateMaxGuesses(length, multiplier);
        Assert.AreEqual(expected, result);
    }
}
