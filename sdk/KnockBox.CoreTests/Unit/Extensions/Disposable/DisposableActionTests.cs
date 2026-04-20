using KnockBox.Core.Primitives.Disposable;

namespace KnockBox.Tests.Unit.Extensions.Disposable;

[TestClass]
public sealed class DisposableActionTests
{
    [TestMethod]
    public void Constructor_NullAction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DisposableAction(null!));
    }

    [TestMethod]
    public void Dispose_InvokesAction()
    {
        var invoked = false;
        var disposable = new DisposableAction(() => invoked = true);

        disposable.Dispose();

        Assert.IsTrue(invoked);
    }

    [TestMethod]
    public void Dispose_CalledTwice_ActionInvokedOnce()
    {
        var invokeCount = 0;
        var disposable = new DisposableAction(() => invokeCount++);

        disposable.Dispose();
        disposable.Dispose();

        Assert.AreEqual(1, invokeCount);
    }

    [TestMethod]
    public void Dispose_ThreadSafe_ActionInvokedExactlyOnce()
    {
        var invokeCount = 0;
        var disposable = new DisposableAction(() => Interlocked.Increment(ref invokeCount));

        var threads = Enumerable.Range(0, 20)
            .Select(_ => new Thread(() => disposable.Dispose()))
            .ToArray();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.AreEqual(1, invokeCount);
    }
}
