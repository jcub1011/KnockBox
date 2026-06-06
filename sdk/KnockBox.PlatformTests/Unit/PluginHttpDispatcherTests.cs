using System.Diagnostics.CodeAnalysis;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Games;
using KnockBox.Platform.Plugins;
using KnockBox.PlatformTests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Pins the platform's plugin HTTP dispatcher contract. The dispatcher is the
/// only door between ASP.NET Core routing and an opted-in plugin engine; its
/// 404 / 500 / cancellation paths are part of the platform's stable surface.
/// </summary>
[TestClass]
public sealed class PluginHttpDispatcherTests
{
    private const string Route = "fake-route";

    [TestMethod]
    public async Task DispatchAsync_UnknownRouteIdentifier_Returns404()
    {
        var (dispatcher, _) = Build(engine: null, lobbyByUri: null);

        var result = await dispatcher.DispatchAsync("unknown-route", "anything/sub", new DefaultHttpContext(), CancellationToken.None);

        AssertIsNotFound(result);
    }

    [TestMethod]
    public async Task DispatchAsync_EngineDoesNotImplementHandler_Returns404()
    {
        var (dispatcher, _) = Build(engine: new FakeAbstractGameEngine(), lobbyByUri: null);

        var result = await dispatcher.DispatchAsync(Route, "any-room", new DefaultHttpContext(), CancellationToken.None);

        AssertIsNotFound(result);
    }

    [TestMethod]
    public async Task DispatchAsync_EmptySubPath_Returns404()
    {
        var (dispatcher, _) = Build(engine: new FakeGameEngineHttpHandler(), lobbyByUri: null);

        var result = await dispatcher.DispatchAsync(Route, string.Empty, new DefaultHttpContext(), CancellationToken.None);

        AssertIsNotFound(result);
    }

    [TestMethod]
    public async Task DispatchAsync_UnknownRoomUri_Returns404()
    {
        var (dispatcher, _) = Build(engine: new FakeGameEngineHttpHandler(), lobbyByUri: null);

        var result = await dispatcher.DispatchAsync(Route, "ghost-room/extra", new DefaultHttpContext(), CancellationToken.None);

        AssertIsNotFound(result);
    }

    [TestMethod]
    public async Task DispatchAsync_AnonymousCaller_StillReachesHandler()
    {
        var handler = new FakeGameEngineHttpHandler();
        var registration = MakeRegistration("guidA-guidB");
        var (dispatcher, _) = Build(engine: handler, lobbyByUri: registration);

        var ctx = new DefaultHttpContext(); // no User identity attached
        Assert.IsFalse(ctx.User?.Identity?.IsAuthenticated ?? false, "Test precondition: caller must be anonymous.");

        await dispatcher.DispatchAsync(Route, "guidA-guidB", ctx, CancellationToken.None);

        Assert.IsTrue(handler.WasInvoked, "Anonymous callers must still reach the handler.");
    }

    [TestMethod]
    public async Task DispatchAsync_HappyPath_DelegatesToHandlerWithCorrectArgs()
    {
        var handler = new FakeGameEngineHttpHandler();
        var registration = MakeRegistration("guidA-guidB");
        var (dispatcher, _) = Build(engine: handler, lobbyByUri: registration);

        var ctx = new DefaultHttpContext();
        var returned = await dispatcher.DispatchAsync(Route, "guidA-guidB/images/42", ctx, CancellationToken.None);

        Assert.IsTrue(handler.WasInvoked);
        Assert.AreEqual("guidA-guidB", handler.CapturedRoomUri);
        Assert.AreEqual("images/42", handler.CapturedSubPath);
        Assert.AreSame(registration.State, handler.CapturedState);
        Assert.AreSame(ctx, handler.CapturedContext);
        Assert.AreSame(handler.ResultToReturn, returned);
    }

    [TestMethod]
    public async Task DispatchAsync_SubPathWithMultipleSegments_PassesTrailingPathToHandler()
    {
        var handler = new FakeGameEngineHttpHandler();
        var registration = MakeRegistration("room-id");
        var (dispatcher, _) = Build(engine: handler, lobbyByUri: registration);

        await dispatcher.DispatchAsync(Route, "room-id/images/abc/extra", new DefaultHttpContext(), CancellationToken.None);

        Assert.AreEqual("room-id", handler.CapturedRoomUri);
        Assert.AreEqual("images/abc/extra", handler.CapturedSubPath);
    }

    [TestMethod]
    public async Task DispatchAsync_RoomOnlyNoSubPath_PassesEmptySubPath()
    {
        var handler = new FakeGameEngineHttpHandler();
        var registration = MakeRegistration("solo-room");
        var (dispatcher, _) = Build(engine: handler, lobbyByUri: registration);

        await dispatcher.DispatchAsync(Route, "solo-room", new DefaultHttpContext(), CancellationToken.None);

        Assert.AreEqual("solo-room", handler.CapturedRoomUri);
        Assert.AreEqual(string.Empty, handler.CapturedSubPath);
    }

    [TestMethod]
    public async Task DispatchAsync_HandlerThrowsOperationCanceled_ReturnsClientClosedRequest()
    {
        var handler = new FakeGameEngineHttpHandler { ThrowOnHandle = new OperationCanceledException() };
        var registration = MakeRegistration("guidA-guidB");
        var (dispatcher, _) = Build(engine: handler, lobbyByUri: registration);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await dispatcher.DispatchAsync(Route, "guidA-guidB", new DefaultHttpContext(), cts.Token);

        var status = Assert.IsInstanceOfType<StatusCodeHttpResult>(result);
        Assert.AreEqual(StatusCodes.Status499ClientClosedRequest, status.StatusCode);
    }

    [TestMethod]
    public async Task DispatchAsync_HandlerThrowsGenericException_ReturnsProblem500()
    {
        var handler = new FakeGameEngineHttpHandler { ThrowOnHandle = new InvalidOperationException("boom") };
        var registration = MakeRegistration("guidA-guidB");
        var (dispatcher, _) = Build(engine: handler, lobbyByUri: registration);

        var result = await dispatcher.DispatchAsync(Route, "guidA-guidB", new DefaultHttpContext(), CancellationToken.None);

        var problem = Assert.IsInstanceOfType<ProblemHttpResult>(result);
        Assert.AreEqual(StatusCodes.Status500InternalServerError, problem.StatusCode);
    }

    private static LobbyRegistration MakeRegistration(string roomUri)
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = new FakeAbstractGameEngine.FakeState(host);
        return new LobbyRegistration("CODE0001", $"room/{Route}/{roomUri}", "Fake Game", Route, state);
    }

    private static (PluginHttpDispatcher dispatcher, Mock<ILobbyService> lobbyMock) Build(
        AbstractGameEngine? engine,
        LobbyRegistration? lobbyByUri)
    {
        var services = new ServiceCollection();
        if (engine is not null)
            services.AddKeyedSingleton<AbstractGameEngine>(Route, engine);
        var sp = services.BuildServiceProvider();

        var lobby = new Mock<ILobbyService>();
        lobby
            .Setup(l => l.TryGetByUri(It.IsAny<string>(), out It.Ref<LobbyRegistration?>.IsAny))
            .Returns(new TryGetByUri((string _, [NotNullWhen(true)] out LobbyRegistration? r) =>
            {
                r = lobbyByUri;
                return lobbyByUri is not null;
            }));

        var dispatcher = new PluginHttpDispatcher(sp, lobby.Object, NullLogger<PluginHttpDispatcher>.Instance);
        return (dispatcher, lobby);
    }

    private delegate bool TryGetByUri(string uri, [NotNullWhen(true)] out LobbyRegistration? registration);

    private static void AssertIsNotFound(IResult result)
    {
        var status = result as IStatusCodeHttpResult;
        Assert.IsNotNull(status, $"Expected an IStatusCodeHttpResult; got {result.GetType().FullName}.");
        Assert.AreEqual(StatusCodes.Status404NotFound, status.StatusCode);
    }
}
