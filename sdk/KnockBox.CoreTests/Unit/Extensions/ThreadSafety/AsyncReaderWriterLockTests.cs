using KnockBox.Core.Primitives.ThreadSafety;

namespace KnockBox.Tests.Unit.Extensions.ThreadSafety;

[TestClass]
public sealed class AsyncReaderWriterLockTests
{
    [TestMethod]
    public void WaitRead_Uncontended_Acquires()
    {
        using var rw = new AsyncReaderWriterLock();
        rw.WaitRead();
        rw.ReleaseRead();
    }

    [TestMethod]
    public void WaitWrite_Uncontended_Acquires()
    {
        using var rw = new AsyncReaderWriterLock();
        rw.WaitWrite();
        rw.ReleaseWrite();
    }

    [TestMethod]
    public void WaitReadAsync_Uncontended_CompletesSynchronously()
    {
        using var rw = new AsyncReaderWriterLock();
        var vt = rw.WaitReadAsync(CancellationToken.None);
        Assert.IsTrue(vt.IsCompletedSuccessfully, "Uncontended WaitReadAsync must complete synchronously.");
        rw.ReleaseRead();
    }

    [TestMethod]
    public void WaitWriteAsync_Uncontended_CompletesSynchronously()
    {
        using var rw = new AsyncReaderWriterLock();
        var vt = rw.WaitWriteAsync(CancellationToken.None);
        Assert.IsTrue(vt.IsCompletedSuccessfully, "Uncontended WaitWriteAsync must complete synchronously.");
        rw.ReleaseWrite();
    }

    [TestMethod]
    public async Task MultipleReaders_RunConcurrently()
    {
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitReadAsync(CancellationToken.None);

        // A second read must acquire immediately while the first read is held.
        var vt = rw.WaitReadAsync(CancellationToken.None);
        Assert.IsTrue(vt.IsCompletedSuccessfully, "Reader must not block another reader.");

        rw.ReleaseRead();
        rw.ReleaseRead();
    }

    [TestMethod]
    public async Task Writer_WaitsForActiveReaders()
    {
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitReadAsync(CancellationToken.None);

        var pendingWrite = rw.WaitWriteAsync(CancellationToken.None).AsTask();
        Assert.IsFalse(pendingWrite.IsCompleted, "Writer must wait for active reader.");

        rw.ReleaseRead();
        await pendingWrite;
        rw.ReleaseWrite();
    }

    [TestMethod]
    public async Task Reader_WaitsForActiveWriter()
    {
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitWriteAsync(CancellationToken.None);

        var pendingRead = rw.WaitReadAsync(CancellationToken.None).AsTask();
        Assert.IsFalse(pendingRead.IsCompleted, "Reader must wait for active writer.");

        rw.ReleaseWrite();
        await pendingRead;
        rw.ReleaseRead();
    }

    [TestMethod]
    public async Task WriterPreference_LateReaderQueuesBehindWaitingWriter()
    {
        // Reader holds. Writer queues. A reader arriving after the writer must
        // queue behind it, not slip in front.
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitReadAsync(CancellationToken.None);

        var pendingWrite = rw.WaitWriteAsync(CancellationToken.None).AsTask();
        await Task.Delay(20);
        Assert.IsFalse(pendingWrite.IsCompleted, "Writer must be queued.");

        var lateRead = rw.WaitReadAsync(CancellationToken.None).AsTask();
        await Task.Delay(20);
        Assert.IsFalse(lateRead.IsCompleted, "Late reader must queue behind the waiting writer.");

        rw.ReleaseRead();
        await pendingWrite;
        Assert.IsFalse(lateRead.IsCompleted, "Late reader must wait for the writer to release.");

        rw.ReleaseWrite();
        await lateRead;
        rw.ReleaseRead();
    }

    [TestMethod]
    public async Task ReleaseWrite_DrainsAllQueuedReaders()
    {
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitWriteAsync(CancellationToken.None);

        var r1 = rw.WaitReadAsync(CancellationToken.None).AsTask();
        var r2 = rw.WaitReadAsync(CancellationToken.None).AsTask();
        var r3 = rw.WaitReadAsync(CancellationToken.None).AsTask();
        await Task.Delay(20);
        Assert.IsFalse(r1.IsCompleted || r2.IsCompleted || r3.IsCompleted);

        rw.ReleaseWrite();
        await Task.WhenAll(r1, r2, r3);

        rw.ReleaseRead();
        rw.ReleaseRead();
        rw.ReleaseRead();
    }

    [TestMethod]
    public async Task WaitReadAsync_Cancellation_DoesNotAcquire()
    {
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitWriteAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var pending = rw.WaitReadAsync(cts.Token).AsTask();
        Assert.IsFalse(pending.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);

        rw.ReleaseWrite();
    }

    [TestMethod]
    public async Task WaitWriteAsync_Cancellation_DoesNotAcquire()
    {
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitReadAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var pending = rw.WaitWriteAsync(cts.Token).AsTask();
        Assert.IsFalse(pending.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);

        rw.ReleaseRead();
    }

    [TestMethod]
    public async Task ReleaseRead_SkipsCanceledWriter_AndHandsToNextLive()
    {
        // Reader holds; one canceled writer + one live writer queue.
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitReadAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var canceledWrite = rw.WaitWriteAsync(cts.Token).AsTask();
        await Task.Delay(20);
        var liveWrite = rw.WaitWriteAsync(CancellationToken.None).AsTask();

        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await canceledWrite);

        Assert.IsFalse(liveWrite.IsCompleted);

        // Releasing the reader must skip the canceled writer and promote the live one.
        rw.ReleaseRead();
        await liveWrite;
        rw.ReleaseWrite();
    }

    [TestMethod]
    public async Task Dispose_CancelsAllPendingWaiters()
    {
        var rw = new AsyncReaderWriterLock();
        await rw.WaitWriteAsync(CancellationToken.None);

        var r1 = rw.WaitReadAsync(CancellationToken.None).AsTask();
        var w1 = rw.WaitWriteAsync(CancellationToken.None).AsTask();

        rw.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await r1);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await w1);
    }

    [TestMethod]
    public async Task WaitReadAsync_AfterDispose_Throws()
    {
        var rw = new AsyncReaderWriterLock();
        rw.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await rw.WaitReadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task WaitWriteAsync_AfterDispose_Throws()
    {
        var rw = new AsyncReaderWriterLock();
        rw.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await rw.WaitWriteAsync(CancellationToken.None));
    }

    [TestMethod]
    public void ReleaseRead_WithoutAcquire_Throws()
    {
        using var rw = new AsyncReaderWriterLock();
        Assert.ThrowsExactly<InvalidOperationException>(() => rw.ReleaseRead());
    }

    [TestMethod]
    public void ReleaseWrite_WithoutAcquire_Throws()
    {
        using var rw = new AsyncReaderWriterLock();
        Assert.ThrowsExactly<InvalidOperationException>(() => rw.ReleaseWrite());
    }

    [TestMethod]
    public async Task WaitWrite_Sync_BlocksUntilRelease()
    {
        // Regression for the sync slow path: a sync WaitWrite() call must
        // park on its kernel event until the holding writer releases,
        // without allocating a Task / blocking on .GetAwaiter().GetResult().
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitWriteAsync(CancellationToken.None);

        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncWaiter = Task.Run(() =>
        {
            rw.WaitWrite();
            observed.SetResult();
            rw.ReleaseWrite();
        });

        // Give the sync waiter time to enqueue and park.
        await Task.Delay(50);
        Assert.IsFalse(observed.Task.IsCompleted, "Sync WaitWrite must block while the writer is held.");

        rw.ReleaseWrite();
        await syncWaiter;
        Assert.IsTrue(observed.Task.IsCompleted, "Sync WaitWrite must wake once the holding writer released.");
    }

    [TestMethod]
    public async Task SyncAndAsyncWaiters_CoexistFifo_WriterPreference()
    {
        // A held writer; one queued async reader (enqueued first), one
        // queued sync reader (enqueued second). On release, both are
        // drained in arrival order regardless of sync/async kind.
        using var rw = new AsyncReaderWriterLock();
        await rw.WaitWriteAsync(CancellationToken.None);

        var asyncReader = rw.WaitReadAsync(CancellationToken.None).AsTask();
        await Task.Delay(20);

        var syncReaderAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncReader = Task.Run(() =>
        {
            rw.WaitRead();
            syncReaderAcquired.SetResult();
            rw.ReleaseRead();
        });

        await Task.Delay(20);
        Assert.IsFalse(asyncReader.IsCompleted);
        Assert.IsFalse(syncReaderAcquired.Task.IsCompleted);

        rw.ReleaseWrite();
        await asyncReader;
        await syncReaderAcquired.Task;
        rw.ReleaseRead();  // close out the async reader
        await syncReader;
    }

    [TestMethod]
    public async Task Dispose_WakesSyncWaiter_WithObjectDisposedException()
    {
        var rw = new AsyncReaderWriterLock();
        await rw.WaitWriteAsync(CancellationToken.None);

        Exception? caught = null;
        var syncWaiter = Task.Run(() =>
        {
            try { rw.WaitWrite(); }
            catch (Exception ex) { caught = ex; }
        });

        await Task.Delay(50);
        Assert.IsFalse(syncWaiter.IsCompleted);

        rw.Dispose();
        await syncWaiter;

        Assert.IsInstanceOfType<ObjectDisposedException>(caught);
    }

    [TestMethod]
    public async Task HeavyContention_NoStarvation()
    {
        // Mix of readers and writers; everyone must eventually finish, no
        // lost updates and no counter sees a partial write.
        using var rw = new AsyncReaderWriterLock();
        int sharedCounter = 0;
        int readCount = 0;
        const int readerCount = 16;
        const int writerCount = 8;
        const int iterations = 50;

        var tasks = new List<Task>();

        for (int i = 0; i < writerCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    await rw.WaitWriteAsync(CancellationToken.None);
                    try { sharedCounter++; }
                    finally { rw.ReleaseWrite(); }
                }
            }));
        }

        for (int i = 0; i < readerCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    await rw.WaitReadAsync(CancellationToken.None);
                    try { Interlocked.Increment(ref readCount); }
                    finally { rw.ReleaseRead(); }
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.AreEqual(writerCount * iterations, sharedCounter, "All writer increments must be visible.");
        Assert.AreEqual(readerCount * iterations, readCount, "All reader passes must complete.");
    }
}
