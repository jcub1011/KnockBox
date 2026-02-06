using KnockBox.Services.State.Shared;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public sealed class AbstractStateTests
{
    private sealed class TestState : AbstractState<TestState>
    {
        public List<PropertyStateChangedArgs> Changes { get; } = new();

        public TestState()
        {
            PropertyStateChanged += (_, args) => Changes.Add(args);
        }

        public void AddUpdater(string propertyName, Func<CancellationToken, Task> updater, params string[] dependencies) =>
            RegisterUpdater(propertyName, updater, dependencies);
    }

    [TestMethod]
    public void GetPropertyState_Unregistered_ReturnsUninitialized()
    {
        using var state = new TestState();

        var result = state.GetPropertyState("missing");

        Assert.AreEqual(PropertyState.Uninitialized, result);
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_SingleProperty_SetsReadyAndRaisesEvent()
    {
        using var state = new TestState();
        var callCount = 0;

        state.AddUpdater("A", _ =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        var results = await state.UpdatePropertiesAsync(default, "A");

        Assert.AreEqual(1, callCount);
        var result = results.Single(r => r.PropertyName == "A");
        Assert.AreEqual(PropertyUpdateResult.Succeeded, result.Status);
        Assert.AreEqual(PropertyState.Ready, state.GetPropertyState("A"));
        Assert.IsTrue(state.Changes.Any(c => c.PropertyName == "A" && c.State == PropertyState.Ready));
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_DependencyRunsBeforeDependent()
    {
        using var state = new TestState();
        var order = new List<string>();

        state.AddUpdater("Base", _ =>
        {
            order.Add("Base");
            return Task.CompletedTask;
        });

        state.AddUpdater("Child", _ =>
        {
            order.Add("Child");
            return Task.CompletedTask;
        }, "Base");

        var results = await state.UpdatePropertiesAsync(default, "Child");

        CollectionAssert.AreEqual(new[] { "Base", "Child" }, order);
        Assert.IsTrue(results.All(r => r.Status == PropertyUpdateResult.Succeeded));
        Assert.AreEqual(PropertyState.Ready, state.GetPropertyState("Base"));
        Assert.AreEqual(PropertyState.Ready, state.GetPropertyState("Child"));
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_DependencyErrorSkipsDependent()
    {
        using var state = new TestState();
        var dependentRan = false;

        state.AddUpdater("Root", _ => throw new InvalidOperationException("boom"));
        state.AddUpdater("Leaf", _ =>
        {
            dependentRan = true;
            return Task.CompletedTask;
        }, "Root");

        var results = await state.UpdatePropertiesAsync(default, "Leaf");
        var dict = results.ToDictionary(r => r.PropertyName);

        Assert.IsFalse(dependentRan);
        Assert.AreEqual(PropertyUpdateResult.Errored, dict["Root"].Status);
        Assert.AreEqual(PropertyUpdateResult.Errored, dict["Leaf"].Status);
        Assert.IsInstanceOfType<AggregateException>(dict["Leaf"].Exception);
        Assert.AreEqual(PropertyState.Errored, state.GetPropertyState("Root"));
        Assert.AreEqual(PropertyState.Errored, state.GetPropertyState("Leaf"));
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_DependencyCancellationPropagates()
    {
        using var state = new TestState();
        var dependentRan = false;

        state.AddUpdater("Root", _ => throw new OperationCanceledException());
        state.AddUpdater("Leaf", _ =>
        {
            dependentRan = true;
            return Task.CompletedTask;
        }, "Root");

        var results = await state.UpdatePropertiesAsync(default, "Leaf");
        var dict = results.ToDictionary(r => r.PropertyName);

        Assert.IsFalse(dependentRan);
        Assert.AreEqual(PropertyUpdateResult.Canceled, dict["Root"].Status);
        Assert.AreEqual(PropertyUpdateResult.Canceled, dict["Leaf"].Status);
        Assert.AreEqual(PropertyState.Canceled, state.GetPropertyState("Root"));
        Assert.AreEqual(PropertyState.Canceled, state.GetPropertyState("Leaf"));
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_RespectsMaxParallelUpdates()
    {
        using var state = new TestState();
        var running = 0;
        var maxRunning = 0;

        Task Updater(CancellationToken ct)
        {
            var current = Interlocked.Increment(ref running);
            UpdateMax(ref maxRunning, current);

            return Task.Delay(100, ct).ContinueWith(_ =>
            {
                Interlocked.Decrement(ref running);
            }, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        state.AddUpdater("A", Updater);
        state.AddUpdater("B", Updater);
        state.AddUpdater("C", Updater);

        var results = await state.UpdatePropertiesAsync(2, CancellationToken.None, "A", "B", "C");

        Assert.IsTrue(results.All(r => r.Status == PropertyUpdateResult.Succeeded));
        Assert.IsLessThanOrEqualTo(2, maxRunning, $"Expected max parallelism <= 2 but observed {maxRunning}");
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_MissingUpdater_ThrowsArgumentException()
    {
        using var state = new TestState();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => state.UpdatePropertiesAsync(default, "Unknown"));

        Assert.Contains("No updaters for properties", ex.Message);
    }

    [TestMethod]
    public void RegisterUpdater_MissingDependency_Throws()
    {
        using var state = new TestState();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            state.AddUpdater("B", _ => Task.CompletedTask, "A"));

        Assert.Contains("Dependency 'A' is not registered", ex.Message);
    }

    [TestMethod]
    public void RegisterUpdater_Duplicate_Throws()
    {
        using var state = new TestState();
        state.AddUpdater("A", _ => Task.CompletedTask);

        Assert.Throws<InvalidOperationException>(() =>
            state.AddUpdater("A", _ => Task.CompletedTask));
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_ConcurrentCallersShareInFlightUpdates()
    {
        using var state = new TestState();
        var fooCount = 0;
        var barCount = 0;
        var zipCount = 0;
        var barStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var barGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        state.AddUpdater("FOO", _ =>
        {
            Interlocked.Increment(ref fooCount);
            return Task.CompletedTask;
        });

        state.AddUpdater("BAR", async ct =>
        {
            Interlocked.Increment(ref barCount);
            barStarted.TrySetResult();
            await barGate.Task.WaitAsync(ct);
        });

        state.AddUpdater("ZIP", _ =>
        {
            Interlocked.Increment(ref zipCount);
            return Task.CompletedTask;
        });

        var callerA = state.UpdatePropertiesAsync(default, "FOO", "BAR");
        await barStarted.Task;

        var callerB = state.UpdatePropertiesAsync(default, "BAR", "ZIP");

        barGate.SetResult();
        var results = await Task.WhenAll(callerA, callerB);

        Assert.AreEqual(1, fooCount);
        Assert.AreEqual(1, barCount);
        Assert.AreEqual(1, zipCount);
        Assert.IsTrue(results.SelectMany(r => r).All(r => r.Status == PropertyUpdateResult.Succeeded));
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_ConcurrentCallersShareDependencyUpdates()
    {
        using var state = new TestState();
        var fooCount = 0;
        var zipCount = 0;
        var fooStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var zipStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fooGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var zipGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        state.AddUpdater("FOO", async ct =>
        {
            Interlocked.Increment(ref fooCount);
            fooStarted.TrySetResult();
            await fooGate.Task.WaitAsync(ct);
        });

        state.AddUpdater("ZIP", async ct =>
        {
            Interlocked.Increment(ref zipCount);
            zipStarted.TrySetResult();
            await zipGate.Task.WaitAsync(ct);
        });

        state.AddUpdater("BAR", _ => Task.CompletedTask, "ZIP");

        var callerA = state.UpdatePropertiesAsync(default, "FOO", "BAR");
        await Task.WhenAll(fooStarted.Task, zipStarted.Task);

        var callerB = state.UpdatePropertiesAsync(default, "FOO", "ZIP");

        fooGate.SetResult();
        zipGate.SetResult();

        await Task.WhenAll(callerA, callerB);

        Assert.AreEqual(1, fooCount);
        Assert.AreEqual(1, zipCount);
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_ConcurrentCallersShareTransitiveDependencies()
    {
        using var state = new TestState();
        var zagCount = 0;
        var zagStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var zagGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        state.AddUpdater("FOO", _ => Task.CompletedTask);
        state.AddUpdater("ZIG", _ => Task.CompletedTask);

        state.AddUpdater("ZAG", async ct =>
        {
            Interlocked.Increment(ref zagCount);
            zagStarted.TrySetResult();
            await zagGate.Task.WaitAsync(ct);
        });

        state.AddUpdater("BAR", _ => Task.CompletedTask, "ZAG");
        state.AddUpdater("ZOP", _ => Task.CompletedTask, "ZAG");

        var callerA = state.UpdatePropertiesAsync(default, "FOO", "BAR");
        await zagStarted.Task;

        var callerB = state.UpdatePropertiesAsync(default, "ZIG", "ZOP");

        zagGate.SetResult();
        await Task.WhenAll(callerA, callerB);

        Assert.AreEqual(1, zagCount);
    }

    [TestMethod]
    public async Task UpdatePropertiesAsync_CancelOneCaller_DoesNotCancelSharedUpdate()
    {
        using var state = new TestState();
        var fooCount = 0;
        var fooStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fooGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        state.AddUpdater("FOO", async ct =>
        {
            Interlocked.Increment(ref fooCount);
            fooStarted.TrySetResult();
            await fooGate.Task.WaitAsync(ct);
        });

        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();

        // Start B first so it's definitely registered.
        var callerB = state.UpdatePropertiesAsync(ctsB.Token, "FOO");
        await fooStarted.Task;

        var callerA = state.UpdatePropertiesAsync(ctsA.Token, "FOO");
        ctsA.Cancel();

        fooGate.SetResult();

        var results = await Task.WhenAll(callerA, callerB);

        var aResult = results[0].Single(r => r.PropertyName == "FOO");
        var bResult = results[1].Single(r => r.PropertyName == "FOO");

        Assert.AreEqual(1, fooCount);
        Assert.AreEqual(PropertyUpdateResult.Canceled, aResult.Status);
        Assert.AreEqual(PropertyUpdateResult.Succeeded, bResult.Status);
        Assert.AreEqual(PropertyState.Ready, state.GetPropertyState("FOO"));
    }

    private static void UpdateMax(ref int target, int value)
    {
        int current;
        do
        {
            current = target;
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}