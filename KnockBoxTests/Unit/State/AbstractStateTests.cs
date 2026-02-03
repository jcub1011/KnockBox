using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnockBox.Services.State.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        Assert.IsInstanceOfType(dict["Leaf"].Exception, typeof(AggregateException));
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
    public async Task UpdatePropertiesAsync_WhenPropertyAlreadyUpdating_Throws()
    {
        using var state = new TestState();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        state.AddUpdater("A", async ct => await gate.Task.WaitAsync(ct));

        var first = state.UpdatePropertiesAsync(default, "A");
        await Task.Delay(20); // allow first call to lock the property

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => state.UpdatePropertiesAsync(default, "A"));

        gate.SetResult();
        await first;

        StringAssert.Contains(ex.Message, "already being updated");
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