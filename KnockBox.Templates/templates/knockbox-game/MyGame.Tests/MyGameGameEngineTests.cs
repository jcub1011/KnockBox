using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace MyGame.Tests;

[TestClass]
public class MyGameGameEngineTests
{
    private MyGameGameEngine _engine = null!;

    [TestInitialize]
    public void Setup()
    {
        _engine = new MyGameGameEngine(
            Mock.Of<ILogger<MyGameGameEngine>>(),
            Mock.Of<ILogger<MyGameGameState>>());
    }

    [TestMethod]
    public async Task CreateStateAsync_ReturnsSuccessfulState()
    {
        var host = new User("TestHost", Guid.CreateVersion7().ToString());

        var result = await _engine.CreateStateAsync(host);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task CreateStateAsync_StateIsJoinable()
    {
        var host = new User("TestHost", Guid.CreateVersion7().ToString());

        var result = await _engine.CreateStateAsync(host);

        Assert.IsTrue(result.TryGetSuccess(out var state));
        Assert.IsTrue(state!.IsJoinable);
    }

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
