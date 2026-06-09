using KnockBox.Core.Client.Hub;
using KnockBox.Core.Plugins;
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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage of the server-owned game clock (<see cref="LobbyTickService"/>): it walks
/// every open lobby and drives time-based FSM transitions for engines that implement
/// <see cref="IServerTickHandler"/>, replacing the old per-host browser-circuit tick.
/// A regression here means timed games (auto-draw on turn timeout, etc.) stall in WASM
/// because nothing advances their state.
/// </summary>
[TestClass]
public sealed class LobbyTickServiceTests
{
    private const string Route = "tick-test-route";

    [TestMethod]
    public async Task TickOnce_BeforeDeadline_DoesNotAdvance_AfterDeadline_Advances()
    {
        var h = Build();
        var host = UserFactory.Create("Host", Guid.NewGuid());

        var create = await h.LobbyService.CreateLobbyAsync(host, Route);
        Assert.IsTrue(create.TryGetSuccess(out var lobby));
        var state = (TickStubGameState)lobby.State;

        var deadline = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        state.Deadline = deadline;

        // Before the deadline: no transition.
        h.TickService.TickOnce(deadline - TimeSpan.FromSeconds(1));
        Assert.IsFalse(state.Advanced, "State must not advance before its deadline.");

        // At/after the deadline: the engine's Tick advances the state.
        h.TickService.TickOnce(deadline + TimeSpan.FromSeconds(1));
        Assert.IsTrue(state.Advanced, "State must advance once its deadline lapses.");
    }

    [TestMethod]
    public async Task TickOnce_SkipsDisposedLobby_WithoutThrowing()
    {
        var h = Build();
        var host = UserFactory.Create("Host", Guid.NewGuid());

        var create = await h.LobbyService.CreateLobbyAsync(host, Route);
        Assert.IsTrue(create.TryGetSuccess(out var lobby));
        var state = (TickStubGameState)lobby.State;
        state.Deadline = DateTimeOffset.MinValue; // would advance if ticked

        lobby.State.Dispose();

        // A disposed lobby is skipped — no throw, no advance.
        h.TickService.TickOnce(DateTimeOffset.MaxValue);
        Assert.IsFalse(state.Advanced, "A disposed lobby must not be ticked.");
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public required LobbyService LobbyService { get; init; }
        public required LobbyTickService TickService { get; init; }
    }

    private static Harness Build()
    {
        var module = new StubModule();
        var engine = new TickStubEngine();

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

        var clients = new Mock<IHubClients<IGameClient>>();
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<IGameClient>());
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IGameClient>());
        var hubContext = new Mock<IHubContext<GameHub, IGameClient>>();
        hubContext.SetupGet(c => c.Clients).Returns(clients.Object);

        var registry = new GameConnectionRegistry();
        var coordinator = new GameViewCoordinator(
            hubContext.Object, registry, sp, NullLogger<GameViewCoordinator>.Instance);

        var lobbyService = new LobbyService(
            sp, codeService.Object, availability.Object, [module], coordinator, NullLogger<LobbyService>.Instance);

        var tickService = new LobbyTickService(
            lobbyService, Mock.Of<ITickService>(), sp, NullLogger<LobbyTickService>.Instance);

        return new Harness { LobbyService = lobbyService, TickService = tickService };
    }

    private sealed class StubModule : IGameModule
    {
        public IPluginManifest Manifest { get; } = new PluginManifest(
            Name: "Tick Test Plugin",
            Description: "Fixture.",
            RouteIdentifier: Route,
            Version: new Version(1, 0, 0),
            EntryAssembly: "Fixture.Assembly",
            Capabilities: new HashSet<PluginCapability>());

        public void RegisterServices(IPluginRegistration registration) { }
    }

    private sealed class TickStubEngine : AbstractGameEngine, IServerTickHandler
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            var state = new TickStubGameState(host);
            state.Execute(() => state.SetJoinable(true));
            return Task.FromResult<ValueResult<AbstractGameState>>(state);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
            => Task.FromResult(Result.Success);

        public void Tick(AbstractGameState state, DateTimeOffset now)
        {
            if (state is not TickStubGameState s) return;
            if (s.Advanced || now < s.Deadline) return;
            s.Execute(() => s.MarkAdvanced());
        }
    }

    private sealed class TickStubGameState(User host) : AbstractGameState(host, NullLogger.Instance)
    {
        public DateTimeOffset Deadline { get; set; }
        public bool Advanced { get; private set; }
        public void MarkAdvanced() => Advanced = true;
    }
}
