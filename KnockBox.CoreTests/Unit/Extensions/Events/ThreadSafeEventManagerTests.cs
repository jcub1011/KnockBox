using KnockBox.Core.Extensions.Events;

namespace KnockBox.Tests.Unit.Extensions.Events;

[TestClass]
public sealed class ThreadSafeEventManagerTests
{
    [TestMethod]
    public void Subscribe_NullCallback_Throws()
    {
        var manager = new ThreadSafeEventManager<string>();

        Assert.Throws<ArgumentNullException>(() =>
            manager.Subscribe(null!));
    }

    [TestMethod]
    public async Task NotifyAsync_NoSubscribers_DoesNotThrow()
    {
        var manager = new ThreadSafeEventManager<int>();

        await manager.NotifyAsync(42);
    }

    [TestMethod]
    public async Task NotifyAsync_CallsAllSubscribers()
    {
        var manager = new ThreadSafeEventManager<string>();
        var called = 0;

        manager.Subscribe(_ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        manager.Subscribe(_ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        await manager.NotifyAsync("ping");

        Assert.AreEqual(2, called);
    }

    [TestMethod]
    public async Task Subscribe_Dispose_RemovesCallback()
    {
        var manager = new ThreadSafeEventManager<string>();
        var called = 0;

        var subscription = manager.Subscribe(_ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        subscription.Dispose();

        await manager.NotifyAsync("ping");

        Assert.AreEqual(0, called);
    }

    [TestMethod]
    public async Task Subscribe_Dispose_IsIdempotent()
    {
        var manager = new ThreadSafeEventManager<string>();
        var called = 0;

        var subscription = manager.Subscribe(_ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        subscription.Dispose();
        subscription.Dispose();

        await manager.NotifyAsync("ping");

        Assert.AreEqual(0, called);
    }

    [TestMethod]
    public async Task NotifyAsync_SwallowsCallbackExceptions_InvokesOthers()
    {
        var manager = new ThreadSafeEventManager<string>();
        var called = 0;

        manager.Subscribe(_ => throw new InvalidOperationException("boom"));
        manager.Subscribe(_ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        await manager.NotifyAsync("ping");

        Assert.AreEqual(1, called);
    }

    [TestMethod]
    public async Task NotifyAsync_AwaitsCallbacks()
    {
        var manager = new ThreadSafeEventManager<string>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.Subscribe(async _ => await tcs.Task.ConfigureAwait(false));

        var notifyTask = manager.NotifyAsync("ping");

        await Task.Delay(50);
        Assert.IsFalse(notifyTask.IsCompleted);

        tcs.SetResult();
        await notifyTask;
    }
}
