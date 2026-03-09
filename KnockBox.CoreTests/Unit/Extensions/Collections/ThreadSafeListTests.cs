using KnockBox.Extensions.Collections;
using KnockBox.Extensions.ThreadSafety;

namespace KnockBox.Tests.Unit.Extensions.Collections;

[TestClass]
public sealed class ThreadSafeListTests
{
    // ── Add / Count ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Add_IncreasesCount()
    {
        using var list = new ThreadSafeList<int>();

        list.Add(1);
        list.Add(2);

        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public void Add_ElementRetrievable()
    {
        using var list = new ThreadSafeList<string>();

        list.Add("hello");

        Assert.AreEqual("hello", list[0]);
    }

    // ── Contains ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Contains_PresentElement_ReturnsTrue()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(42);

        Assert.IsTrue(list.Contains(42));
    }

    [TestMethod]
    public void Contains_AbsentElement_ReturnsFalse()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);

        Assert.IsFalse(list.Contains(99));
    }

    // ── Remove ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Remove_ExistingElement_ReturnsTrueAndRemoves()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var removed = list.Remove(2);

        Assert.IsTrue(removed);
        Assert.AreEqual(2, list.Count);
        Assert.IsFalse(list.Contains(2));
    }

    [TestMethod]
    public void Remove_AbsentElement_ReturnsFalse()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);

        var removed = list.Remove(99);

        Assert.IsFalse(removed);
        Assert.AreEqual(1, list.Count);
    }

    // ── RemoveAt ────────────────────────────────────────────────────────────

    [TestMethod]
    public void RemoveAt_ValidIndex_RemovesElement()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.RemoveAt(1);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(30, list[1]);
    }

    // ── Insert ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Insert_InsertsAtCorrectPosition()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);
        list.Add(3);

        list.Insert(1, 2);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(2, list[1]);
    }

    // ── IndexOf ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void IndexOf_PresentElement_ReturnsIndex()
    {
        using var list = new ThreadSafeList<string>();
        list.Add("a");
        list.Add("b");

        Assert.AreEqual(1, list.IndexOf("b"));
    }

    [TestMethod]
    public void IndexOf_AbsentElement_ReturnsNegativeOne()
    {
        using var list = new ThreadSafeList<string>();
        list.Add("a");

        Assert.AreEqual(-1, list.IndexOf("z"));
    }

    // ── Clear ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Clear_EmptiesList()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);
        list.Add(2);

        list.Clear();

        Assert.AreEqual(0, list.Count);
    }

    // ── CopyTo ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void CopyTo_CopiesAllElements()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var array = new int[3];
        list.CopyTo(array, 0);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, array);
    }

    // ── Indexer set ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Indexer_Set_UpdatesElement()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(0);

        list[0] = 99;

        Assert.AreEqual(99, list[0]);
    }

    // ── Enumerator ──────────────────────────────────────────────────────────

    [TestMethod]
    public void GetEnumerator_EnumeratesAllElements()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var enumerated = list.ToList();

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, enumerated);
    }

    // ── Dispose ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_SetsIsDisposedToTrue()
    {
        var list = new ThreadSafeList<int>();
        list.Add(1);

        list.Dispose();

        Assert.IsTrue(list.IsDisposed);
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var list = new ThreadSafeList<int>();

        list.Dispose();
        list.Dispose();
    }

    [TestMethod]
    public void Add_AfterDispose_Throws()
    {
        var list = new ThreadSafeList<int>();
        list.Dispose();

        Assert.Throws<ObjectDisposedException>(() => list.Add(1));
    }

    [TestMethod]
    public void Contains_AfterDispose_Throws()
    {
        var list = new ThreadSafeList<int>();
        list.Dispose();

        Assert.Throws<ObjectDisposedException>(() => list.Contains(1));
    }

    [TestMethod]
    public void Count_AfterDispose_Throws()
    {
        var list = new ThreadSafeList<int>();
        list.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = list.Count);
    }

    // ── Lock scope overloads ─────────────────────────────────────────────────

    [TestMethod]
    public void GetCount_WithReadLock_ReturnsCorrectCount()
    {
        using var list = new ThreadSafeList<int>();
        list.Add(1);
        list.Add(2);

        using var scope = list.EnterReadLockScope();
        var count = list.GetCount(scope);

        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public void Add_WithWriteLock_AddsElement()
    {
        using var list = new ThreadSafeList<int>();

        using var scope = list.EnterWriteLockScope();
        list.Add(42, scope);

        Assert.AreEqual(42, list.At(0, scope));
    }
}
