using KnockBox.Extensions.Collections;

namespace KnockBox.Tests.Unit.Extensions.Collections;

[TestClass]
public sealed class StackExtensionsTests
{
    // ── PushRange ───────────────────────────────────────────────────────────

    [TestMethod]
    public void PushRange_AddsAllElementsInOrder()
    {
        var stack = new Stack<int>();

        stack.PushRange([1, 2, 3]);

        // Stack pops in LIFO order, so the last pushed is popped first
        Assert.AreEqual(3, stack.Pop());
        Assert.AreEqual(2, stack.Pop());
        Assert.AreEqual(1, stack.Pop());
    }

    [TestMethod]
    public void PushRange_EmptyRange_StackUnchanged()
    {
        var stack = new Stack<int>();
        stack.Push(99);

        stack.PushRange([]);

        Assert.AreEqual(1, stack.Count);
        Assert.AreEqual(99, stack.Peek());
    }

    // ── PopRange (returns array) ────────────────────────────────────────────

    [TestMethod]
    public void PopRange_CountZero_ReturnsEmptyArray()
    {
        var stack = new Stack<int>(new[] { 1, 2, 3 });

        var result = stack.PopRange(0);

        Assert.AreEqual(0, result.Length);
        Assert.AreEqual(3, stack.Count);
    }

    [TestMethod]
    public void PopRange_ValidCount_RemovesAndReturnsElements()
    {
        var stack = new Stack<int>();
        stack.PushRange([1, 2, 3]);

        var result = stack.PopRange(2);

        // Top two popped first (LIFO): 3, 2
        Assert.AreEqual(2, result.Length);
        Assert.AreEqual(3, result[0]);
        Assert.AreEqual(2, result[1]);
        Assert.AreEqual(1, stack.Count);
    }

    [TestMethod]
    public void PopRange_AllElements_EmptiesStack()
    {
        var stack = new Stack<int>(new[] { 10, 20, 30 });

        var result = stack.PopRange(3);

        Assert.AreEqual(3, result.Length);
        Assert.AreEqual(0, stack.Count);
    }

    [TestMethod]
    public void PopRange_NegativeCount_Throws()
    {
        var stack = new Stack<int>(new[] { 1 });

        Assert.Throws<ArgumentOutOfRangeException>(() => stack.PopRange(-1));
    }

    [TestMethod]
    public void PopRange_CountExceedsStackSize_Throws()
    {
        var stack = new Stack<int>(new[] { 1, 2 });

        Assert.Throws<ArgumentOutOfRangeException>(() => stack.PopRange(3));
    }

    // ── PopRange (output list overload) ─────────────────────────────────────

    [TestMethod]
    public void PopRange_IntoList_AppendsElements()
    {
        var stack = new Stack<int>();
        stack.PushRange([1, 2, 3]);

        var output = new List<int> { 0 };
        stack.PopRange(2, ref output);

        // 3 and 2 are popped (LIFO) and appended
        Assert.AreEqual(3, output.Count);
        Assert.AreEqual(0, output[0]);
        Assert.AreEqual(3, output[1]);
        Assert.AreEqual(2, output[2]);
        Assert.AreEqual(1, stack.Count);
    }

    [TestMethod]
    public void PopRange_IntoList_NegativeCount_Throws()
    {
        var stack = new Stack<int>(new[] { 1 });
        var output = new List<int>();

        Assert.Throws<ArgumentOutOfRangeException>(() => stack.PopRange(-1, ref output));
    }

    [TestMethod]
    public void PopRange_IntoList_CountExceedsStackSize_Throws()
    {
        var stack = new Stack<int>(new[] { 1 });
        var output = new List<int>();

        Assert.Throws<ArgumentOutOfRangeException>(() => stack.PopRange(5, ref output));
    }
}
