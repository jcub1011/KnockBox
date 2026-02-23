using KnockBox.Extensions.Events;

namespace KnockBox.Tests.Unit.Extensions.Events;

[TestClass]
public sealed class TypedThreadSafeEventManagerTests
{
    public static IEnumerable<object?[]> InvalidGroups =>
    [
        [null],
        [string.Empty],
        ["   "]
    ];

    [TestMethod]
    [DynamicData(nameof(InvalidGroups))]
    public void Subscribe_InvalidGroup_Throws(string? group)
    {
        using var manager = new TypedThreadSafeEventManager();

        Assert.Throws<ArgumentException>(() =>
            manager.Subscribe<string>(group!, _ => ValueTask.CompletedTask));
    }

    [TestMethod]
    public void Subscribe_NullCallback_Throws()
    {
        using var manager = new TypedThreadSafeEventManager();

        Assert.Throws<ArgumentNullException>(() =>
            manager.Subscribe<string>("group", null!));
    }

    [TestMethod]
    [DynamicData(nameof(InvalidGroups))]
    public void Unsubscribe_InvalidGroup_Throws(string? group)
    {
        using var manager = new TypedThreadSafeEventManager();

        Assert.Throws<ArgumentException>(() =>
            manager.Unsubscribe<string>(group!, _ => ValueTask.CompletedTask));
    }

    [TestMethod]
    public void Unsubscribe_NullCallback_Throws()
    {
        using var manager = new TypedThreadSafeEventManager();

        Assert.Throws<ArgumentNullException>(() =>
            manager.Unsubscribe<string>("group", null!));
    }

    [TestMethod]
    [DynamicData(nameof(InvalidGroups))]
    public async Task NotifyAsync_InvalidGroup_Throws(string? group)
    {
        using var manager = new TypedThreadSafeEventManager();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.NotifyAsync<string>(group!, "payload"));
    }

    [TestMethod]
    public async Task NotifyAsync_NoSubscribers_DoesNotThrow()
    {
        using var manager = new TypedThreadSafeEventManager();

        await manager.NotifyAsync("missing", 42);
    }

    [TestMethod]
    public async Task NotifyAsync_CallsAllSubscribersForGroupAndType()
    {
        using var manager = new TypedThreadSafeEventManager();
        var called = 0;

        manager.Subscribe<string>("group", _ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        manager.Subscribe<string>("group", _ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        await manager.NotifyAsync("group", "ping");

        Assert.AreEqual(2, called);
    }

    [TestMethod]
    public async Task NotifyAsync_OnlyCallsMatchingType()
    {
        using var manager = new TypedThreadSafeEventManager();
        var intCalls = 0;
        var stringCalls = 0;

        manager.Subscribe<int>("group", _ =>
        {
            Interlocked.Increment(ref intCalls);
            return ValueTask.CompletedTask;
        });

        manager.Subscribe<string>("group", _ =>
        {
            Interlocked.Increment(ref stringCalls);
            return ValueTask.CompletedTask;
        });

        await manager.NotifyAsync("group", "payload");

        Assert.AreEqual(0, intCalls);
        Assert.AreEqual(1, stringCalls);
    }

    [TestMethod]
    public async Task Unsubscribe_RemovesCallback()
    {
        using var manager = new TypedThreadSafeEventManager();
        var called = 0;

        ValueTask callback(string _)
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        }

        manager.Subscribe("group", (Func<string, ValueTask>)callback);
        manager.Unsubscribe("group", (Func<string, ValueTask>)callback);

        await manager.NotifyAsync("group", "ping");

        Assert.AreEqual(0, called);
    }

    [TestMethod]
    public async Task NotifyAsync_SwallowsCallbackExceptions_InvokesOthers()
    {
        using var manager = new TypedThreadSafeEventManager();
        var called = 0;

        manager.Subscribe<string>("group", _ => throw new InvalidOperationException("boom"));
        manager.Subscribe<string>("group", _ =>
        {
            Interlocked.Increment(ref called);
            return ValueTask.CompletedTask;
        });

        await manager.NotifyAsync("group", "ping");

        Assert.AreEqual(1, called);
    }

    [TestMethod]
    public async Task NotifyAsync_AwaitsCallbacks()
    {
        using var manager = new TypedThreadSafeEventManager();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.Subscribe<string>("group", async _ => await tcs.Task.ConfigureAwait(false));

        var notifyTask = manager.NotifyAsync("group", "ping");

        await Task.Delay(50);
        Assert.IsFalse(notifyTask.IsCompleted);

        tcs.SetResult();
        await notifyTask;
    }

    [TestMethod]
    public async Task Dispose_ThenOperationsThrow()
    {
        var manager = new TypedThreadSafeEventManager();
        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            manager.Subscribe<string>("group", _ => ValueTask.CompletedTask));

        Assert.Throws<ObjectDisposedException>(() =>
            manager.Unsubscribe<string>("group", _ => ValueTask.CompletedTask));

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            manager.NotifyAsync("group", "payload"));
    }
}