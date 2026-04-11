using System;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public abstract class ISessionServiceProviderContractTests<TProvider> where TProvider : ISessionServiceProvider, IDisposable
{
    protected interface ITestService { }
    protected interface IOtherService { }

    protected sealed class TestService : ITestService, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    protected sealed class OtherService : IOtherService { }

    /// <summary>
    /// Creates a configured provider.
    /// </summary>
    protected abstract TProvider CreateProvider(Action<IServiceCollection> configureServices);

    /// <summary>
    /// Simulates the passing of time or forcefully triggers the condition that causes a delayed-disposal 
    /// service to be disposed after all of its lifecycle tokens have been disposed.
    /// </summary>
    protected abstract Task ForceDisposalTimerExpirationAsync();

    [TestMethod]
    public void GetService_ReturnsValidRegistration_WithInstanceAndLifecycleToken()
    {
        using var provider = CreateProvider(services => services.AddTransient<ITestService, TestService>());
        var token = new SessionToken(Guid.NewGuid());

        var result = provider.GetService<ITestService>(token);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value.Service);
        Assert.IsNotNull(result.Value.LifecycleToken);
        Assert.AreEqual(token, result.Value.SessionToken);
    }

    [TestMethod]
    public void GetService_SameSessionToken_ReturnsSameInstance()
    {
        using var provider = CreateProvider(services => services.AddTransient<ITestService, TestService>());
        var token = new SessionToken(Guid.NewGuid());

        var first = provider.GetService<ITestService>(token);
        var second = provider.GetService<ITestService>(token);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreSame(first.Value.Service, second.Value.Service);
    }

    [TestMethod]
    public void GetService_DifferentSessionTokens_ReturnsDistinctInstances()
    {
        using var provider = CreateProvider(services => services.AddTransient<ITestService, TestService>());
        var token1 = new SessionToken(Guid.NewGuid());
        var token2 = new SessionToken(Guid.NewGuid());

        var first = provider.GetService<ITestService>(token1);
        var second = provider.GetService<ITestService>(token2);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreNotSame(first.Value.Service, second.Value.Service);
    }

    [TestMethod]
    public void GetService_DifferentServiceTypes_ReturnsDistinctInstances()
    {
        using var provider = CreateProvider(services =>
        {
            services.AddTransient<ITestService, TestService>();
            services.AddTransient<IOtherService, OtherService>();
        });
        var token = new SessionToken(Guid.NewGuid());

        var first = provider.GetService<ITestService>(token);
        var second = provider.GetService<IOtherService>(token);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreNotSame((object)first.Value.Service, (object)second.Value.Service);
    }

    [TestMethod]
    public void GetService_LifecycleTokenDisposed_DoesNotImmediatelyDisposeService()
    {
        using var provider = CreateProvider(services => services.AddTransient<ITestService, TestService>());
        var token = new SessionToken(Guid.NewGuid());

        var result = provider.GetService<ITestService>(token);
        var service = (TestService)result.Value.Service;

        result.Value.LifecycleToken.Dispose();

        // Verifying delayed disposal contract: immediately after disposing token, service is alive.
        Assert.IsFalse(service.Disposed);
    }

    [TestMethod]
    public async Task GetService_LifecycleTokenDisposed_TimerExpires_DisposesService()
    {
        using var provider = CreateProvider(services => services.AddTransient<ITestService, TestService>());
        var token = new SessionToken(Guid.NewGuid());

        var result = provider.GetService<ITestService>(token);
        var service = (TestService)result.Value.Service;

        result.Value.LifecycleToken.Dispose();
        
        await ForceDisposalTimerExpirationAsync();

        Assert.IsTrue(service.Disposed);
    }

    [TestMethod]
    public async Task GetService_MultipleLifecycleTokens_DisposingOneDoesNotStartTimer()
    {
        using var provider = CreateProvider(services => services.AddTransient<ITestService, TestService>());
        var token = new SessionToken(Guid.NewGuid());

        var result1 = provider.GetService<ITestService>(token);
        var result2 = provider.GetService<ITestService>(token);
        var service = (TestService)result1.Value.Service;

        result1.Value.LifecycleToken.Dispose();
        
        await ForceDisposalTimerExpirationAsync(); // Timer shouldn't do anything because result2 token is active

        Assert.IsFalse(service.Disposed);

        result2.Value.LifecycleToken.Dispose();
        await ForceDisposalTimerExpirationAsync();

        Assert.IsTrue(service.Disposed);
    }

    [TestMethod]
    public async Task GetService_GetsServiceAfterAllTokensDisposed_ResetsTimer()
    {
        using var provider = CreateProvider(services => services.AddTransient<ITestService, TestService>());
        var token = new SessionToken(Guid.NewGuid());

        var result1 = provider.GetService<ITestService>(token);
        var service = (TestService)result1.Value.Service;

        // Dispose first token (starts timer)
        result1.Value.LifecycleToken.Dispose();

        // Get a new token before timer expires (should cancel/reset timer)
        var result2 = provider.GetService<ITestService>(token);

        Assert.AreSame(service, result2.Value.Service);

        await ForceDisposalTimerExpirationAsync();

        // Timer triggered from the first dispose shouldn't have killed the service.
        Assert.IsFalse(service.Disposed);
    }
}