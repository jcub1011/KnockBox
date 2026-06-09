using KnockBox.Core.Client.Hub;
using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Games;
using KnockBox.Platform.Hubs;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage of the WASM lobby teardown lifecycle: a hub-created lobby must be closed
/// (state disposed, remaining players notified) when the host leaves — immediately on
/// an explicit leave, or after the session-eviction grace when the host's last
/// connection drops — while a reconnect within the grace window keeps it alive.
/// Regressions here are a memory leak: an orphaned <see cref="AbstractGameState"/> +
/// per-lobby projection subscriber for every abandoned lobby.
/// <para>
/// These exercise the real <see cref="SessionServiceProvider"/> eviction →
/// <see cref="GameSessionState"/> disposal → <c>CloseLobbyAsync</c> →
/// <see cref="GameViewCoordinator"/> notification chain, with the exact session
/// wiring <see cref="GameHub.CreateRoom"/> installs (acquire ref on connect; set the
/// close-on-dispose registration; release the create-time ref) and the same
/// teardown <see cref="GameHub.OnDisconnectedAsync"/> drives (release ref → grace).
/// </para>
/// </summary>
[TestClass]
public sealed class HubLobbyLifecycleTests
{
    private const string Route = "lifecycle-test-route";

    [TestMethod]
    public async Task HostDisconnect_AfterGrace_ClosesLobby_DisposesState_NotifiesPlayers()
    {
        var h = Build();
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var token = new SessionToken(host.Id);

        var (uri, state) = HostCreatesRoom(h, token, host, "conn-1");
        Assert.IsTrue(h.LobbyService.TryGetByUri(uri, out _));
        Assert.IsFalse(state.IsDisposed);

        // Host's last connection drops → release the session ref → grace starts.
        Assert.IsTrue(h.Registry.RemoveSession("conn-1", out var lifecycleToken));
        lifecycleToken!.Dispose();

        // Within grace: still open (a refresh could reconnect).
        Assert.IsTrue(h.LobbyService.TryGetByUri(uri, out _));
        Assert.IsFalse(state.IsDisposed);

        // Grace lapses → GameSessionState evicted + disposed → CloseLobbyAsync.
        h.Time.Advance(TimeSpan.FromMinutes(2));
        await h.SessionProvider.WaitForPendingEvictionsAsync();

        Assert.IsFalse(h.LobbyService.TryGetByUri(uri, out _), "Lobby must close after the host's grace lapses.");
        Assert.IsTrue(state.IsDisposed, "Game state must be disposed when the lobby closes (else it leaks).");
        Assert.IsTrue(h.ClosedEvents.Contains(uri), "Remaining players must be notified (lobby-closed) so they are kicked.");
    }

    [TestMethod]
    public async Task HostReconnect_WithinGrace_KeepsLobbyOpen()
    {
        var h = Build();
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var token = new SessionToken(host.Id);

        var (uri, state) = HostCreatesRoom(h, token, host, "conn-1");

        // Drop, then reconnect (new connection, same token) within grace.
        Assert.IsTrue(h.Registry.RemoveSession("conn-1", out var lifecycleToken));
        lifecycleToken!.Dispose();
        h.Time.Advance(TimeSpan.FromSeconds(30));
        h.Registry.AddSession(token.Token, "conn-2", () => AcquireSession(h, token));

        // Past the original grace: the live reconnect keeps the lobby open.
        h.Time.Advance(TimeSpan.FromMinutes(2));
        await h.SessionProvider.WaitForPendingEvictionsAsync();

        Assert.IsTrue(h.LobbyService.TryGetByUri(uri, out _), "A reconnect within grace must keep the lobby open.");
        Assert.IsFalse(state.IsDisposed);
        Assert.IsFalse(h.ClosedEvents.Contains(uri));
    }

    [TestMethod]
    public async Task CloseLobby_ByHost_DisposesStateAndNotifies()
    {
        // The explicit-leave path: GameHub.LeaveRoom calls CloseLobbyAsync for the host.
        var h = Build();
        var host = UserFactory.Create("Host", Guid.NewGuid());

        var create = await h.LobbyService.CreateLobbyAsync(host, Route);
        Assert.IsTrue(create.TryGetSuccess(out var lobby));

        var result = await h.LobbyService.CloseLobbyAsync(host, lobby);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(h.LobbyService.TryGetByUri(lobby.Uri, out _));
        Assert.IsTrue(lobby.State.IsDisposed);
        Assert.IsTrue(h.ClosedEvents.Contains(lobby.Uri), "Players must be notified the lobby closed.");
    }

    [TestMethod]
    public async Task CloseLobby_ByNonHost_IsRejected_StateKept()
    {
        // GameHub.LeaveRoom only closes for the host; CloseLobbyAsync enforces it too.
        var h = Build();
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var stranger = UserFactory.Create("Stranger", Guid.NewGuid());

        var create = await h.LobbyService.CreateLobbyAsync(host, Route);
        Assert.IsTrue(create.TryGetSuccess(out var lobby));

        var result = await h.LobbyService.CloseLobbyAsync(stranger, lobby);

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(h.LobbyService.TryGetByUri(lobby.Uri, out _), "A non-host must not be able to close the lobby.");
        Assert.IsFalse(lobby.State.IsDisposed);
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    /// <summary>Mirrors <see cref="GameHub.OnConnectedAsync"/>'s session acquire.</summary>
    private static IDisposable AcquireSession(Harness h, SessionToken token)
    {
        var reg = h.SessionProvider.GetService<GameSessionState>(token);
        Assert.IsTrue(reg.TryGetSuccess(out var registration));
        return registration.LifecycleToken;
    }

    /// <summary>
    /// Reproduces what <see cref="GameHub.OnConnectedAsync"/> + <see cref="GameHub.CreateRoom"/>
    /// do: acquire the session ref for the connection, create the lobby, then wire the
    /// close-on-dispose registration onto the host's <see cref="GameSessionState"/> and
    /// release the create-time reference.
    /// </summary>
    private static (string Uri, AbstractGameState State) HostCreatesRoom(
        Harness h, SessionToken token, User host, string connectionId)
    {
        h.Registry.AddSession(token.Token, connectionId, () => AcquireSession(h, token));

        var create = h.LobbyService.CreateLobbyAsync(host, Route).GetAwaiter().GetResult();
        Assert.IsTrue(create.TryGetSuccess(out var lobby));

        var sessionResult = h.SessionProvider.GetService<GameSessionState>(token);
        Assert.IsTrue(sessionResult.TryGetSuccess(out var sessionReg));
        var closeAction = new DisposableAction(
            () => _ = h.LobbyService.CloseLobbyAsync(host, lobby, CancellationToken.None));
        Assert.IsTrue(sessionReg.Service.TrySetCurrentSession(new UserRegistration(host, closeAction, lobby)));
        sessionReg.LifecycleToken.Dispose();

        return (lobby.Uri, lobby.State);
    }

    private sealed class Harness
    {
        public required LobbyService LobbyService { get; init; }
        public required GameConnectionRegistry Registry { get; init; }
        public required SessionServiceProvider SessionProvider { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required List<string> ClosedEvents { get; init; }
    }

    private static Harness Build()
    {
        var module = new StubModule();
        var engine = new StubEngine();

        var services = new ServiceCollection();
        services.AddKeyedSingleton<AbstractGameEngine>(Route, engine);
        services.AddTransient<GameSessionState>();
        var sp = services.BuildServiceProvider();

        var codeService = new Mock<ILobbyCodeService>();
        var nextCode = 0;
        codeService.Setup(c => c.IssueLobbyCodeAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ValueResult<string>>($"CODE{Interlocked.Increment(ref nextCode):D4}"));
        codeService.Setup(c => c.ReleaseLobbyCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Result>(Result.Success));

        var availability = new Mock<IGameAvailabilityService>();
        availability.Setup(a => a.IsEnabled(It.IsAny<string>())).Returns(true);

        // Capturing hub context: record which lobby URIs receive a lobby-closed broadcast.
        var closedEvents = new List<string>();
        var clients = new Mock<IHubClients<IGameClient>>();
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<IGameClient>());
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns((string uri) =>
        {
            var groupClient = new Mock<IGameClient>();
            // General first, specific last (Moq: last matching setup wins).
            groupClient.Setup(g => g.ReceiveEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            groupClient.Setup(g => g.ReceiveEvent(It.IsAny<string>(), GameClientEvents.LobbyClosed, It.IsAny<string>()))
                .Callback(() => closedEvents.Add(uri))
                .Returns(Task.CompletedTask);
            return groupClient.Object;
        });
        var hubContext = new Mock<IHubContext<GameHub, IGameClient>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        var registry = new GameConnectionRegistry();
        var coordinator = new GameViewCoordinator(
            hubContext.Object, registry, sp, NullLogger<GameViewCoordinator>.Instance);

        var lobbyService = new LobbyService(
            sp, codeService.Object, availability.Object, [module], coordinator, NullLogger<LobbyService>.Instance);

        var time = new FakeTimeProvider();
        var sessionProvider = new SessionServiceProvider(sp, NullLogger<SessionServiceProvider>.Instance, time);

        return new Harness
        {
            LobbyService = lobbyService,
            Registry = registry,
            SessionProvider = sessionProvider,
            Time = time,
            ClosedEvents = closedEvents,
        };
    }

    private sealed class StubModule : IGameModule
    {
        public IPluginManifest Manifest { get; } = new PluginManifest(
            Name: "Lifecycle Test Plugin",
            Description: "Fixture.",
            RouteIdentifier: Route,
            Version: new Version(1, 0, 0),
            EntryAssembly: "Fixture.Assembly",
            Capabilities: new HashSet<PluginCapability>());

        public void RegisterServices(IPluginRegistration registration) { }
    }

    private sealed class StubEngine : AbstractGameEngine
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            var state = new StubGameState(host);
            state.Execute(() => state.SetJoinable(true));
            return Task.FromResult<ValueResult<AbstractGameState>>(state);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
            => Task.FromResult(Result.Success);
    }

    private sealed class StubGameState(User host) : AbstractGameState(host, NullLogger.Instance);
}
