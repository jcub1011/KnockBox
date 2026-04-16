using KnockBox.Core.Extensions.Collections;

namespace KnockBox.Tests.Unit.Extensions.Collections;

[TestClass]
public sealed class IListExtensionsTests
{
    // ── IndexOf with predicate ──────────────────────────────────────────────

    [TestMethod]
    public void IndexOf_MatchingElement_ReturnsCorrectIndex()
    {
        IReadOnlyList<int> list = [10, 20, 30];

        var index = list.IndexOf(x => x == 20);

        Assert.AreEqual(1, index);
    }

    [TestMethod]
    public void IndexOf_NoMatch_ReturnsNegativeOne()
    {
        IReadOnlyList<int> list = [10, 20, 30];

        var index = list.IndexOf(x => x == 99);

        Assert.AreEqual(-1, index);
    }

    [TestMethod]
    public void IndexOf_EmptyList_ReturnsNegativeOne()
    {
        IReadOnlyList<string> list = [];

        var index = list.IndexOf(x => x == "a");

        Assert.AreEqual(-1, index);
    }

    [TestMethod]
    public void IndexOf_MultipleMatches_ReturnsFirstIndex()
    {
        IReadOnlyList<int> list = [5, 5, 5];

        var index = list.IndexOf(x => x == 5);

        Assert.AreEqual(0, index);
    }

    // ── Remove with predicate ───────────────────────────────────────────────

    [TestMethod]
    public void Remove_MatchingElement_RemovesAndReturnsTrue()
    {
        var list = new List<int> { 1, 2, 3 };

        var removed = list.Remove(x => x == 2);

        Assert.IsTrue(removed);
        CollectionAssert.AreEqual(new[] { 1, 3 }, list);
    }

    [TestMethod]
    public void Remove_NoMatch_ReturnsFalse()
    {
        var list = new List<int> { 1, 2, 3 };

        var removed = list.Remove(x => x == 99);

        Assert.IsFalse(removed);
        Assert.HasCount(3, list);
    }

    [TestMethod]
    public void Remove_EmptyList_ReturnsFalse()
    {
        var list = new List<string>();

        var removed = list.Remove(x => x == "a");

        Assert.IsFalse(removed);
    }

    [TestMethod]
    public void Remove_MultipleMatches_RemovesOnlyFirst()
    {
        var list = new List<int> { 5, 5, 5 };

        var removed = list.Remove(x => x == 5);

        Assert.IsTrue(removed);
        Assert.HasCount(2, list);
        CollectionAssert.AreEqual(new[] { 5, 5 }, list);
    }
}
