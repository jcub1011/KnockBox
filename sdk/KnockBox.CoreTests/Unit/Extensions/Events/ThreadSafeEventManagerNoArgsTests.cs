using KnockBox.Core.Primitives.Events;

namespace KnockBox.Tests.Unit.Extensions.Events;

[TestClass]
public sealed class ThreadSafeEventManagerNoArgsTests
{
    [TestMethod]
    public void Subscribe_NullCallback_Throws()
    {
        var manager = new ThreadSafeEventManager();

        Assert.Throws<ArgumentNullException>(() => manager.Subscribe(null!));
    }

    [TestMethod]
    public async Task NotifyAsync_NoSubscribers_DoesNotThrow()
    {
        var manager = new ThreadSafeEventManager();

        await manager.NotifyAsync();
    }

    [TestMethod]
    public async Task NotifyAsync_CallsAllSubscribers()
    {
        var manager = new ThreadSafeEventManager();
        var called = 0;

        manager.Subscribe(() =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        manager.Subscribe(() =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        await manager.NotifyAsync();

        Assert.AreEqual(2, called);
    }

    [TestMethod]
    public async Task Subscribe_Dispose_RemovesCallback()
    {
        var manager = new ThreadSafeEventManager();
        var called = 0;

        var subscription = manager.Subscribe(() =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        subscription.Dispose();

        await manager.NotifyAsync();

        Assert.AreEqual(0, called);
    }

    [TestMethod]
    public async Task Subscribe_Dispose_IsIdempotent()
    {
        var manager = new ThreadSafeEventManager();
        var called = 0;

        var subscription = manager.Subscribe(() =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        subscription.Dispose();
        subscription.Dispose();

        await manager.NotifyAsync();

        Assert.AreEqual(0, called);
    }

    [TestMethod]
    public async Task NotifyAsync_SwallowsCallbackExceptions_InvokesOtherSubscribers()
    {
        var manager = new ThreadSafeEventManager();
        var called = 0;

        manager.Subscribe(() => throw new InvalidOperationException("boom"));
        manager.Subscribe(() =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        await manager.NotifyAsync();

        Assert.AreEqual(1, called);
    }

    [TestMethod]
    public async Task NotifyAsync_AwaitsCallbacks()
    {
        var manager = new ThreadSafeEventManager();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.Subscribe(async () => await tcs.Task.ConfigureAwait(false));

        var notifyTask = manager.NotifyAsync();

        await Task.Delay(50);
        Assert.IsFalse(notifyTask.IsCompleted);

        tcs.SetResult();
        await notifyTask;
    }

    [TestMethod]
    public async Task Notify_FireAndForget_CallsSubscribers()
    {
        var manager = new ThreadSafeEventManager();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.Subscribe(() =>
        {
            tcs.TrySetResult();
            return ValueTask.CompletedTask;
        });

        manager.Notify();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(tcs.Task, completed, "Subscriber was not called within the timeout.");
    }

    [TestMethod]
    public async Task MultipleSubscribers_OneDisposed_OnlyActiveSubscriberCalled()
    {
        var manager = new ThreadSafeEventManager();
        var firstCalled = 0;
        var secondCalled = 0;

        var first = manager.Subscribe(() =>
        {
            Interlocked.Increment(ref firstCalled);
            return ValueTask.CompletedTask;
        });

        manager.Subscribe(() =>
        {
            Interlocked.Increment(ref secondCalled);
            return ValueTask.CompletedTask;
        });

        first.Dispose();
        await manager.NotifyAsync();

        Assert.AreEqual(0, firstCalled);
        Assert.AreEqual(1, secondCalled);
    }

    [TestMethod]
    public async Task Subscribe_Unsubscribe_Notify_UnderConcurrency_DoesNotThrow()
    {
        // Exercises the lock-free ImmutableInterlocked.Update path: N tasks
        // churn subscribe/unsubscribe while another runs Notify in a loop.
        // No exception should escape; the surviving-subscriber count must
        // match what we hold onto at the end.
        var manager = new ThreadSafeEventManager();
        var survivorCount = 0;
        const int churnTasks = 8;
        const int churnIterations = 500;

        // Hold a fixed set of subscribers that should never be disposed.
        var survivors = new List<IDisposable>();
        for (int i = 0; i < 4; i++)
        {
            survivors.Add(manager.Subscribe(() =>
            {
                Interlocked.Increment(ref survivorCount);
                return ValueTask.CompletedTask;
            }));
        }

        using var cts = new CancellationTokenSource();
        var notifier = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await manager.NotifyAsync();
            }
        });

        var churn = Enumerable.Range(0, churnTasks).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < churnIterations; i++)
            {
                var sub = manager.Subscribe(() => ValueTask.CompletedTask);
                sub.Dispose();
            }
        })).ToArray();

        await Task.WhenAll(churn);
        cts.Cancel();
        await notifier;

        // Reset the counter so the post-churn Notify gives us a clean read.
        Volatile.Write(ref survivorCount, 0);
        await manager.NotifyAsync();
        Assert.AreEqual(survivors.Count, survivorCount,
            "Only the held survivors should remain after the churn settles.");

        foreach (var s in survivors) s.Dispose();
    }
}
