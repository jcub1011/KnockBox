// -----------------------------------------------------------------------------
// Sample engine tests.
//
// MSTest + Moq are pre-wired. Focus these tests on engine behavior — command
// validation, state mutation, invariants, failure paths. Razor pages need a
// full Blazor circuit to render meaningfully; leave UI coverage to integration
// tests or manual checks in the DevHost.
//
// Because the engine is a plain class with constructor-injected loggers, you
// can exercise it against a real MyGameGameState (no DI container required).
// Use Moq only for the boundaries you need to fake — usually loggers or any
// external services you inject later (random number source, clock, etc.).
// -----------------------------------------------------------------------------

using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace MyGame.Tests;

/// <summary>
/// Tests for <see cref="MyGameGameEngine"/> — the engine's lifecycle hooks and
/// any custom commands you add.
/// </summary>
[TestClass]
public class MyGameGameEngineTests
{
    private MyGameGameEngine _engine = null!;

    [TestInitialize]
    public void Setup()
    {
        // Mock.Of<T>() returns a no-op stub — the simplest way to satisfy
        // logger parameters without standing up a real LoggerFactory.
        _engine = new MyGameGameEngine(
            Mock.Of<ILogger<MyGameGameEngine>>(),
            Mock.Of<ILogger<MyGameGameState>>());
    }

    /// <summary>Happy path: CreateStateAsync returns a successful ValueResult.</summary>
    [TestMethod]
    public async Task CreateStateAsync_ReturnsSuccessfulState()
    {
        var host = new User("TestHost", Guid.CreateVersion7().ToString());

        var result = await _engine.CreateStateAsync(host);

        Assert.IsTrue(result.IsSuccess);
    }

    /// <summary>Fresh lobbies are joinable so other players can enter.</summary>
    [TestMethod]
    public async Task CreateStateAsync_StateIsJoinable()
    {
        var host = new User("TestHost", Guid.CreateVersion7().ToString());

        var result = await _engine.CreateStateAsync(host);

        // TryGetSuccess(out var value) is the canonical ValueResult<T>
        // success-path assertion — it returns true AND unpacks the value.
        Assert.IsTrue(result.TryGetSuccess(out var state));
        Assert.IsTrue(state!.IsJoinable);
    }

    /// <summary>Non-host players cannot start the game — server-side check.</summary>
    [TestMethod]
    public async Task StartAsync_RejectsNonHost()
    {
        var host = new User("Host", Guid.CreateVersion7().ToString());
        var nonHost = new User("Player", Guid.CreateVersion7().ToString());
        var createResult = await _engine.CreateStateAsync(host);
        Assert.IsTrue(createResult.TryGetSuccess(out var state));

        var startResult = await _engine.StartAsync(nonHost, state!);

        Assert.IsTrue(startResult.IsFailure);
    }
}
