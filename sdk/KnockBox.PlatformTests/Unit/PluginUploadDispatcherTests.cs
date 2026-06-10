using System.Diagnostics.CodeAnalysis;
using System.Text;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Platform;
using KnockBox.Platform.Games;
using KnockBox.Platform.Plugins;
using KnockBox.PlatformTests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Pins the generic plugin upload dispatcher contract: token authentication,
/// room/handler resolution, the size cap, and the success/failure mapping that
/// every plugin upload inherits without re-implementing.
/// </summary>
[TestClass]
public sealed class PluginUploadDispatcherTests
{
    private const string Route = "fake-route";
    private const string Uri = "room/fake-route/guidA-guidB";
    private const string GoodToken = "good-token";
    private static readonly Guid CallerId = Guid.NewGuid();

    [TestMethod]
    public async Task DispatchAsync_MissingToken_Returns401()
    {
        var (dispatcher, _) = Build(new FakeGameUploadHandler(), MakeRegistration());

        var ctx = MakeContext(token: null, body: "data");
        var result = await dispatcher.DispatchAsync(ctx, Uri, "word-pool", "f.csv", CancellationToken.None);

        AssertStatus(result, StatusCodes.Status401Unauthorized);
    }

    [TestMethod]
    public async Task DispatchAsync_UnknownRoom_Returns404()
    {
        var (dispatcher, _) = Build(new FakeGameUploadHandler(), registration: null);

        var result = await dispatcher.DispatchAsync(MakeContext(GoodToken, "data"), Uri, "word-pool", "f.csv", CancellationToken.None);

        AssertStatus(result, StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task DispatchAsync_EngineWithoutUploadHandler_Returns400()
    {
        var (dispatcher, _) = Build(new FakeAbstractGameEngine(), MakeRegistration());

        var result = await dispatcher.DispatchAsync(MakeContext(GoodToken, "data"), Uri, "word-pool", "f.csv", CancellationToken.None);

        AssertStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task DispatchAsync_HappyPath_StreamsBodyAndPassesCallerKindFileName()
    {
        var handler = new FakeGameUploadHandler();
        var registration = MakeRegistration();
        var (dispatcher, _) = Build(handler, registration);

        var result = await dispatcher.DispatchAsync(
            MakeContext(GoodToken, "alpha\nbravo\ncharlie"), Uri, "word-pool", "words.csv", CancellationToken.None);

        AssertStatus(result, StatusCodes.Status200OK);
        Assert.IsTrue(handler.WasInvoked);
        Assert.AreEqual(CallerId, handler.CapturedCaller!.Id);
        Assert.AreEqual("word-pool", handler.CapturedKind);
        Assert.AreEqual("words.csv", handler.CapturedFileName);
        Assert.AreEqual("alpha\nbravo\ncharlie", handler.CapturedContent);
        Assert.AreSame(registration.State, handler.CapturedState);
    }

    [TestMethod]
    public async Task DispatchAsync_HandlerFailure_Returns400WithMessage()
    {
        var handler = new FakeGameUploadHandler
        {
            ResultToReturn = Result.FromError("Only the host can upload a word pool.", "non-host upload"),
        };
        var (dispatcher, _) = Build(handler, MakeRegistration());

        var result = await dispatcher.DispatchAsync(MakeContext(GoodToken, "data"), Uri, "word-pool", "f.csv", CancellationToken.None);

        AssertStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task DispatchAsync_DeclaredLengthOverCap_Returns413()
    {
        var handler = new FakeGameUploadHandler();
        var (dispatcher, _) = Build(handler, MakeRegistration(), maxUploadBytes: 8);

        var ctx = MakeContext(GoodToken, "way more than eight bytes", declareLength: true);
        var result = await dispatcher.DispatchAsync(ctx, Uri, "word-pool", "f.csv", CancellationToken.None);

        AssertStatus(result, StatusCodes.Status413PayloadTooLarge);
        Assert.IsFalse(handler.WasInvoked, "An over-cap declared length must short-circuit before the handler.");
    }

    [TestMethod]
    public async Task DispatchAsync_ChunkedBodyOverCap_Returns413()
    {
        var handler = new FakeGameUploadHandler();
        var (dispatcher, _) = Build(handler, MakeRegistration(), maxUploadBytes: 8);

        // No declared Content-Length → the ByteLimitStream backstop must trip
        // while the handler reads past the cap.
        var ctx = MakeContext(GoodToken, "way more than eight bytes", declareLength: false);
        var result = await dispatcher.DispatchAsync(ctx, Uri, "word-pool", "f.csv", CancellationToken.None);

        AssertStatus(result, StatusCodes.Status413PayloadTooLarge);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DefaultHttpContext MakeContext(string? token, string body, bool declareLength = true)
    {
        var ctx = new DefaultHttpContext();
        if (token is not null)
            ctx.Request.Headers.Authorization = $"Bearer {token}";
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Body = new MemoryStream(bytes);
        if (declareLength)
            ctx.Request.ContentLength = bytes.Length;
        return ctx;
    }

    private static LobbyRegistration MakeRegistration()
    {
        var host = KnockBox.Core.Services.State.Users.UserFactory.Create("Host", Guid.NewGuid());
        var state = new FakeAbstractGameEngine.FakeState(host);
        return new LobbyRegistration("CODE0001", Uri, "Fake Game", Route, state);
    }

    private static (PluginUploadDispatcher dispatcher, Mock<ILobbyService> lobbyMock) Build(
        AbstractGameEngine engine,
        LobbyRegistration? registration,
        long maxUploadBytes = 2 * 1024 * 1024)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<AbstractGameEngine>(Route, engine);
        var sp = services.BuildServiceProvider();

        var lobby = new Mock<ILobbyService>();
        lobby
            .Setup(l => l.TryGetByUri(It.IsAny<string>(), out It.Ref<LobbyRegistration?>.IsAny))
            .Returns(new TryGetByUri((string _, [NotNullWhen(true)] out LobbyRegistration? r) =>
            {
                r = registration;
                return registration is not null;
            }));

        var tokens = new Mock<ISessionIdentityTokenService>();
        tokens
            .Setup(t => t.TryResolve(It.IsAny<string?>(), out It.Ref<Guid>.IsAny))
            .Returns(new TryResolveToken((string? token, out Guid id) =>
            {
                id = token == GoodToken ? CallerId : Guid.Empty;
                return token == GoodToken;
            }));

        var options = new KnockBoxPlatformOptions { MaxUploadBytes = maxUploadBytes };

        var dispatcher = new PluginUploadDispatcher(
            sp, lobby.Object, tokens.Object, options, NullLogger<PluginUploadDispatcher>.Instance);
        return (dispatcher, lobby);
    }

    private delegate bool TryGetByUri(string uri, [NotNullWhen(true)] out LobbyRegistration? registration);
    private delegate bool TryResolveToken(string? token, out Guid userId);

    private static void AssertStatus(IResult result, int expected)
    {
        var status = result as IStatusCodeHttpResult;
        Assert.IsNotNull(status, $"Expected an IStatusCodeHttpResult; got {result.GetType().FullName}.");
        Assert.AreEqual(expected, status.StatusCode);
    }
}
