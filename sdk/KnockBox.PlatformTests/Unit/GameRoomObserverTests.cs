using KnockBox.Core.Services.State.Shared;
using KnockBox.Platform.Games;
using KnockBox.Platform.Services.State.Shared;
using Moq;

namespace KnockBox.PlatformTests.Unit;

[TestClass]
public sealed class GameRoomObserverTests
{
    [TestMethod]
    public void Attach_KnownRoom_ReturnsState()
    {
        var lobbies = new Mock<ILobbyService>();
        var fakeState = StubFactory.MakeState();
        var registration = new KnockBox.Core.Services.Logic.Games.Shared.LobbyRegistration(
            lobbyCode: "CODE",
            lobbyUri: "room/test-route/abc-def",
            gameName: "Test",
            routeIdentifier: "test-route",
            state: fakeState);
        lobbies.Setup(l => l.TryGetByUri("room/test-route/abc-def", out registration!))
            .Returns(true);

        var observer = new GameRoomObserver(lobbies.Object);
        var result = observer.Attach("test-route", "abc-def");

        Assert.IsTrue(result.TryGetSuccess(out var attachment));
        Assert.AreSame(fakeState, attachment.State);
    }

    [TestMethod]
    public void Attach_UnknownRoom_ReturnsFailure()
    {
        var lobbies = new Mock<ILobbyService>();
        KnockBox.Core.Services.Logic.Games.Shared.LobbyRegistration? missing = null;
        lobbies.Setup(l => l.TryGetByUri(It.IsAny<string>(), out missing!))
            .Returns(false);

        var observer = new GameRoomObserver(lobbies.Object);
        var result = observer.Attach("test-route", "nope");

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void Attach_EmptyArgs_Rejected()
    {
        var observer = new GameRoomObserver(Mock.Of<ILobbyService>());
        Assert.IsTrue(observer.Attach("", "abc").IsFailure);
        Assert.IsTrue(observer.Attach("route", "").IsFailure);
    }

    [TestMethod]
    public void Dispose_IsNoop_StateUnaffected()
    {
        var lobbies = new Mock<ILobbyService>();
        var fakeState = StubFactory.MakeState();
        var registration = new KnockBox.Core.Services.Logic.Games.Shared.LobbyRegistration(
            "C", "room/r/x", "n", "r", fakeState);
        lobbies.Setup(l => l.TryGetByUri(It.IsAny<string>(), out registration!))
            .Returns(true);

        var observer = new GameRoomObserver(lobbies.Object);
        var result = observer.Attach("r", "x");
        Assert.IsTrue(result.TryGetSuccess(out var attachment));

        attachment.Lifetime.Dispose();
        // Underlying state is still reachable; dispose was a no-op.
        Assert.IsFalse(fakeState.IsDisposed);
    }

    private static class StubFactory
    {
        public static KnockBox.Core.Services.State.Games.Shared.AbstractGameState MakeState()
        {
            var host = KnockBox.Core.Services.State.Users.UserFactory.Create("H", Guid.NewGuid());
            return new StubState(host);
        }

        private sealed class StubState(KnockBox.Core.Services.State.Users.User host)
            : KnockBox.Core.Services.State.Games.Shared.AbstractGameState(host, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        {
        }
    }
}
