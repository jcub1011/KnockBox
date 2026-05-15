using KnockBox.Core.Primitives.ThreadSafety;

namespace KnockBox.Tests.Unit.Extensions.ThreadSafety;

[TestClass]
public sealed class AsyncMutexTests
{
    [TestMethod]
    public void Wait_Uncontended_Acquires()
    {
        using var mutex = new AsyncMutex();
        mutex.Wait();
        mutex.Release();
    }

    [TestMethod]
    public async Task WaitAsync_Uncontended_CompletesSynchronously()
    {
        using var mutex = new AsyncMutex();
        var vt = mutex.WaitAsync(CancellationToken.None);
        Assert.IsTrue(vt.IsCompletedSuccessfully, "Uncontended WaitAsync must complete synchronously with no allocation.");
        await vt;
        mutex.Release();
    }

    [TestMethod]
    public async Task WaitAsync_Contended_BlocksUntilRelease()
    {
        using var mutex = new AsyncMutex();
        await mutex.WaitAsync(CancellationToken.None);

        var secondWait = mutex.WaitAsync(CancellationToken.None);
        Assert.IsFalse(secondWait.IsCompleted, "Second waiter must block while lock is held.");

        mutex.Release();
        await secondWait;
        mutex.Release();
    }

    [TestMethod]
    public async Task WaitAsync_Waiters_AreServedFifo()
    {
        using var mutex = new AsyncMutex();
        await mutex.WaitAsync(CancellationToken.None);

        var observed = new List<int>();
        var observedLock = new Lock();

        async Task Waiter(int id)
        {
            await mutex.WaitAsync(CancellationToken.None);
            lock (observedLock) observed.Add(id);
            mutex.Release();
        }

        // Start 4 waiters in order. Small Task.Yield/Delay between starts to
        // make the enqueue ordering deterministic across schedulers.
        var t1 = Waiter(1);
        await Task.Delay(20);
        var t2 = Waiter(2);
        await Task.Delay(20);
        var t3 = Waiter(3);
        await Task.Delay(20);
        var t4 = Waiter(4);
        await Task.Delay(20);

        mutex.Release();
        await Task.WhenAll(t1, t2, t3, t4);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, observed);
    }

    [TestMethod]
    public async Task WaitAsync_Cancellation_DoesNotAcquireLock()
    {
        using var mutex = new AsyncMutex();
        await mutex.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var pending = mutex.WaitAsync(cts.Token).AsTask();
        Assert.IsFalse(pending.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);

        // Another waiter should still be able to acquire once we Release.
        var nextWait = mutex.WaitAsync(CancellationToken.None);
        Assert.IsFalse(nextWait.IsCompleted, "Lock should still be held by the original waiter.");
        mutex.Release();
        await nextWait;
        mutex.Release();
    }

    [TestMethod]
    public async Task Release_SkipsCanceledWaiters_AndHandsToNextLive()
    {
        using var mutex = new AsyncMutex();
        await mutex.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var canceledWait = mutex.WaitAsync(cts.Token).AsTask();
        // Ensure it has been enqueued before the live waiter.
        await Task.Delay(20);
        var liveWait = mutex.WaitAsync(CancellationToken.None);

        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await canceledWait);

        // liveWait still blocked.
        Assert.IsFalse(liveWait.IsCompleted);

        // Release should skip the canceled waiter and hand to the live one.
        mutex.Release();
        await liveWait;
        mutex.Release();
    }

    [TestMethod]
    public async Task Dispose_CancelsAllPendingWaiters()
    {
        var mutex = new AsyncMutex();
        await mutex.WaitAsync(CancellationToken.None);

        var w1 = mutex.WaitAsync(CancellationToken.None).AsTask();
        var w2 = mutex.WaitAsync(CancellationToken.None).AsTask();

        mutex.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await w1);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await w2);
    }

    [TestMethod]
    public async Task WaitAsync_AfterDispose_Throws()
    {
        var mutex = new AsyncMutex();
        mutex.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await mutex.WaitAsync(CancellationToken.None));
    }

    [TestMethod]
    public void Wait_AfterDispose_Throws()
    {
        var mutex = new AsyncMutex();
        mutex.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => mutex.Wait());
    }

    [TestMethod]
    public void Release_WithoutAcquire_Throws()
    {
        using var mutex = new AsyncMutex();
        Assert.ThrowsExactly<InvalidOperationException>(() => mutex.Release());
    }

    [TestMethod]
    public async Task HeavyContention_AllWaitersComplete_NoStarvation()
    {
        using var mutex = new AsyncMutex();
        int counter = 0;
        const int waiterCount = 64;
        const int iterations = 50;

        var tasks = new Task[waiterCount];
        for (int i = 0; i < waiterCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    await mutex.WaitAsync(CancellationToken.None);
                    try { counter++; }
                    finally { mutex.Release(); }
                }
            });
        }

        await Task.WhenAll(tasks);
        Assert.AreEqual(waiterCount * iterations, counter);
    }
}
