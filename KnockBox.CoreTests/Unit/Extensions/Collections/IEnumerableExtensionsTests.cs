using KnockBox.Extensions.Collections;
using KnockBox.Services.Logic.RandomGeneration;
using Moq;

namespace KnockBox.Tests.Unit.Extensions.Collections;

[TestClass]
public sealed class IEnumerableExtensionsTests
{
    private Mock<IRandomNumberService> _rngMock = default!;

    [TestInitialize]
    public void Setup()
    {
        _rngMock = new Mock<IRandomNumberService>();
    }

    // ── Basic selection ───────────────────────────────────────────────────────

    [TestMethod]
    public void GetRandomWeightedItem_SingleItem_AlwaysReturnsThatItem()
    {
        _rngMock.Setup(r => r.GetRandomInt(0, 5, RandomType.Fast)).Returns(0);

        var result = new[] { "only" }.GetRandomWeightedItem(_ => 5, _rngMock.Object);

        Assert.AreEqual("only", result);
    }

    [TestMethod]
    public void GetRandomWeightedItem_RollZero_ReturnsFirstItem()
    {
        // Items: A=10, B=5, C=1 → totalWeight=16
        // roll=0 → 0 - 10 = -10 < 0 → A
        _rngMock.Setup(r => r.GetRandomInt(0, 16, RandomType.Fast)).Returns(0);

        var items = new[] { "A", "B", "C" };
        var result = items.GetRandomWeightedItem(s => s switch { "A" => 10, "B" => 5, _ => 1 }, _rngMock.Object);

        Assert.AreEqual("A", result);
    }

    [TestMethod]
    public void GetRandomWeightedItem_RollAtLastItemBoundary_ReturnsLastItem()
    {
        // Items: A=10, B=5, C=1 → totalWeight=16
        // roll=15 → 15-10=5, 5-5=0, 0-1=-1 < 0 → C
        _rngMock.Setup(r => r.GetRandomInt(0, 16, RandomType.Fast)).Returns(15);

        var items = new[] { "A", "B", "C" };
        var result = items.GetRandomWeightedItem(s => s switch { "A" => 10, "B" => 5, _ => 1 }, _rngMock.Object);

        Assert.AreEqual("C", result);
    }

    [TestMethod]
    public void GetRandomWeightedItem_RollInMiddleItem_ReturnsMiddleItem()
    {
        // Items: A=10, B=5, C=1 → totalWeight=16
        // roll=10 → 10-10=0, 0-5=-5 < 0 → B
        _rngMock.Setup(r => r.GetRandomInt(0, 16, RandomType.Fast)).Returns(10);

        var items = new[] { "A", "B", "C" };
        var result = items.GetRandomWeightedItem(s => s switch { "A" => 10, "B" => 5, _ => 1 }, _rngMock.Object);

        Assert.AreEqual("B", result);
    }

    // ── Weight proportionality ────────────────────────────────────────────────

    [TestMethod]
    public void GetRandomWeightedItem_EqualWeights_DrawsEachItemExactlyOnceOverFullRange()
    {
        // Two items with weight 1 each → totalWeight=2
        // roll=0 → item 0; roll=1 → item 1
        var items = new[] { "X", "Y" };
        var seen = new HashSet<string>();

        foreach (int roll in new[] { 0, 1 })
        {
            _rngMock.Setup(r => r.GetRandomInt(0, 2, RandomType.Fast)).Returns(roll);
            seen.Add(items.GetRandomWeightedItem(_ => 1, _rngMock.Object));
        }

        CollectionAssert.AreEquivalent(new[] { "X", "Y" }, seen.ToArray());
    }

    [TestMethod]
    public void GetRandomWeightedItem_RarityRatio_TotalWeightMatchesExpected()
    {
        // Verify the RNG is called with the correct totalWeight.
        // Items: A=10, B=1 → totalWeight=11
        int capturedMax = 0;
        _rngMock.Setup(r => r.GetRandomInt(0, It.IsAny<int>(), RandomType.Fast))
            .Callback<int, int, RandomType>((_, max, _) => capturedMax = max)
            .Returns(0);

        new[] { "A", "B" }.GetRandomWeightedItem(s => s == "A" ? 10 : 1, _rngMock.Object);

        Assert.AreEqual(11, capturedMax);
    }

    // ── RandomType forwarding ─────────────────────────────────────────────────

    [TestMethod]
    public void GetRandomWeightedItem_SecureRandomType_PassedToRng()
    {
        _rngMock.Setup(r => r.GetRandomInt(0, It.IsAny<int>(), RandomType.Secure)).Returns(0);

        new[] { "A" }.GetRandomWeightedItem(_ => 1, _rngMock.Object, RandomType.Secure);

        _rngMock.Verify(r => r.GetRandomInt(0, 1, RandomType.Secure), Times.Once);
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [TestMethod]
    public void GetRandomWeightedItem_EmptySequence_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Array.Empty<string>().GetRandomWeightedItem(_ => 1, _rngMock.Object));
    }

    [TestMethod]
    public void GetRandomWeightedItem_ZeroWeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new[] { "A" }.GetRandomWeightedItem(_ => 0, _rngMock.Object));
    }

    [TestMethod]
    public void GetRandomWeightedItem_NegativeWeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new[] { "A" }.GetRandomWeightedItem(_ => -1, _rngMock.Object));
    }
}
