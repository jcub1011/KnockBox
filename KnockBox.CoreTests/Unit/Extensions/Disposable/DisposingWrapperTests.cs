using KnockBox.Core.Extensions.Disposable;

namespace KnockBox.Tests.Unit.Extensions.Disposable;

[TestClass]
public sealed class DisposingWrapperTests
{
    private sealed class FakeDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    [TestMethod]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DisposingWrapper(null!));
    }

    [TestMethod]
    public void Dispose_DisposesInnerObject()
    {
        var inner = new FakeDisposable();
        var wrapper = new DisposingWrapper(inner);

        wrapper.Dispose();

        Assert.AreEqual(1, inner.DisposeCount);
    }

    [TestMethod]
    public void Dispose_CalledTwice_InnerDisposedOnce()
    {
        var inner = new FakeDisposable();
        var wrapper = new DisposingWrapper(inner);

        wrapper.Dispose();
        wrapper.Dispose();

        Assert.AreEqual(1, inner.DisposeCount);
    }

    [TestMethod]
    public void Dispose_ThreadSafe_InnerDisposedExactlyOnce()
    {
        var inner = new FakeDisposable();
        var wrapper = new DisposingWrapper(inner);

        var threads = Enumerable.Range(0, 20)
            .Select(_ => new Thread(() => wrapper.Dispose()))
            .ToArray();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.AreEqual(1, inner.DisposeCount);
    }

    [TestMethod]
    public void Dispose_ThenGC_InnerDisposedOnce()
    {
        var inner = new FakeDisposable();

        CreateAndDisposeWrapper(inner);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.AreEqual(1, inner.DisposeCount);
    }

    // Helpers keep the wrapper creation out-of-scope so the GC can collect it.
    private static void CreateAndAbandonWrapper(IDisposable inner) => _ = new DisposingWrapper(inner);
    private static void CreateAndDisposeWrapper(IDisposable inner) => new DisposingWrapper(inner).Dispose();
}
