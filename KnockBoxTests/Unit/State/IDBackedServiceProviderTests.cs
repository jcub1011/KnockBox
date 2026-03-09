using KnockBox.Services.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public sealed class IDBackedServiceProviderTests
{
    // Use a very short disposal delay so timer-based tests finish quickly.
    private static readonly TimeSpan TestDisposalDelay = TimeSpan.FromMilliseconds(100);

    private interface ITestService { }
    private interface IOtherService { }

    private sealed class TestService : ITestService, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class OtherService : IOtherService { }

    private static IDBackedServiceProvider CreateProvider(
        IServiceProvider? innerProvider = null,
        TimeSpan? disposalDelay = null)
    {
        innerProvider ??= new ServiceCollection().BuildServiceProvider();
        return new IDBackedServiceProvider(
            innerProvider,
            NullLogger<IDBackedServiceProvider>.Instance,
            disposalDelay ?? TimeSpan.FromMinutes(5));
    }

    // ─── GetService ─────────────────────────────────────────────────────────

    [TestMethod]
    public void GetService_UnregisteredType_ReturnsNull()
    {
        var provider = CreateProvider();

        var result = provider.GetService<ITestService>("user1");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetService_RegisteredType_ReturnsInstance()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider());

        var result = provider.GetService<ITestService>("user1");

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<ITestService>(result);
    }

    [TestMethod]
    public void GetService_SameIdAndType_ReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>(); // transient — would normally give new instances
        var provider = CreateProvider(services.BuildServiceProvider());

        var first = provider.GetService<ITestService>("user1");
        var second = provider.GetService<ITestService>("user1");

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetService_DifferentIds_ReturnsDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider());

        var forUser1 = provider.GetService<ITestService>("user1");
        var forUser2 = provider.GetService<ITestService>("user2");

        Assert.IsNotNull(forUser1);
        Assert.IsNotNull(forUser2);
        Assert.AreNotSame(forUser1, forUser2);
    }

    [TestMethod]
    public void GetService_SameIdDifferentTypes_ReturnsDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        services.AddTransient<IOtherService, OtherService>();
        var provider = CreateProvider(services.BuildServiceProvider());

        var testService = provider.GetService<ITestService>("user1");
        var otherService = provider.GetService<IOtherService>("user1");

        Assert.IsNotNull(testService);
        Assert.IsNotNull(otherService);
    }

    // ─── Circuit tracking ────────────────────────────────────────────────────

    [TestMethod]
    public void NotifyCircuitActive_ThenClosed_LastCircuit_StartsTimer()
    {
        // Arrange — register a service so there is something to dispose.
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider(), TestDisposalDelay);

        var svc = (TestService)provider.GetService<ITestService>("user1")!;
        provider.NotifyCircuitActive("user1", "circuit1");

        // Act
        provider.NotifyCircuitClosed("user1", "circuit1");

        // The service should NOT be disposed yet (timer hasn't expired).
        Assert.IsFalse(svc.Disposed);
    }

    [TestMethod]
    public async Task NotifyCircuitClosed_LastCircuit_DisposesServiceAfterDelay()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider(), TestDisposalDelay);

        var svc = (TestService)provider.GetService<ITestService>("user1")!;
        provider.NotifyCircuitActive("user1", "circuit1");
        provider.NotifyCircuitClosed("user1", "circuit1");

        // Wait longer than the disposal delay.
        await Task.Delay(TestDisposalDelay * 3);

        Assert.IsTrue(svc.Disposed);
    }

    [TestMethod]
    public async Task NotifyCircuitClosed_MultipleCircuits_DisposesOnlyAfterAllClose()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider(), TestDisposalDelay);

        var svc = (TestService)provider.GetService<ITestService>("user1")!;
        provider.NotifyCircuitActive("user1", "circuit1");
        provider.NotifyCircuitActive("user1", "circuit2");

        // Close only the first circuit — service should still be alive.
        provider.NotifyCircuitClosed("user1", "circuit1");
        await Task.Delay(TestDisposalDelay * 3);

        Assert.IsFalse(svc.Disposed, "Service should not be disposed while circuit2 is still active.");

        // Close the second circuit — disposal timer should now start.
        provider.NotifyCircuitClosed("user1", "circuit2");
        await Task.Delay(TestDisposalDelay * 3);

        Assert.IsTrue(svc.Disposed);
    }

    [TestMethod]
    public async Task GetService_AfterCircuitClosed_CancelsDisposalTimer()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider(), TestDisposalDelay);

        var svc = (TestService)provider.GetService<ITestService>("user1")!;
        provider.NotifyCircuitActive("user1", "circuit1");
        provider.NotifyCircuitClosed("user1", "circuit1");

        // A new request before the timer expires should cancel disposal.
        provider.GetService<ITestService>("user1");

        await Task.Delay(TestDisposalDelay * 3);

        Assert.IsFalse(svc.Disposed);
    }

    [TestMethod]
    public async Task NotifyCircuitActive_AfterCircuitClosed_CancelsDisposalTimer()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider(), TestDisposalDelay);

        var svc = (TestService)provider.GetService<ITestService>("user1")!;
        provider.NotifyCircuitActive("user1", "circuit1");
        provider.NotifyCircuitClosed("user1", "circuit1");

        // A new circuit coming online before the timer expires should cancel disposal.
        provider.NotifyCircuitActive("user1", "circuit2");

        await Task.Delay(TestDisposalDelay * 3);

        Assert.IsFalse(svc.Disposed);
    }

    [TestMethod]
    public async Task NotifyCircuitClosed_NoMatchingCircuit_StartsFallbackTimer()
    {
        // If the circuit was never registered (e.g. user service not initialized at connect time)
        // closing should still start the disposal timer as a fallback.
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider(), TestDisposalDelay);

        var svc = (TestService)provider.GetService<ITestService>("user1")!;

        // Circuit was never NotifyCircuitActive-d.
        provider.NotifyCircuitClosed("user1", "unknownCircuit");

        await Task.Delay(TestDisposalDelay * 3);

        Assert.IsTrue(svc.Disposed);
    }

    [TestMethod]
    public async Task DisposedService_RemovedFromCache_NewRequestCreatesNewInstance()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        var provider = CreateProvider(services.BuildServiceProvider(), TestDisposalDelay);

        var first = (TestService)provider.GetService<ITestService>("user1")!;
        provider.NotifyCircuitActive("user1", "circuit1");
        provider.NotifyCircuitClosed("user1", "circuit1");

        await Task.Delay(TestDisposalDelay * 3);

        Assert.IsTrue(first.Disposed);

        // A new request should get a fresh instance.
        var second = (TestService)provider.GetService<ITestService>("user1")!;

        Assert.IsNotNull(second);
        Assert.AreNotSame(first, second);
        Assert.IsFalse(second.Disposed);
    }
}
