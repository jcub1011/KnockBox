using KnockBox.Core.Client.Hub;
using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Games;
using KnockBox.Platform.Hubs;
using KnockBox.Services.Logic.Games.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Covers the URI-indexed lookup added in M02. The dispatcher resolves rooms
/// from request paths via <see cref="ILobbyService.TryGetByUri"/>, so the index
/// must stay in lockstep with <c>_lobbies</c> across create/close/shutdown.
/// </summary>
[TestClass]
public sealed class LobbyServiceTests
{
    private const string Route = "lobby-test-route";

    [TestMethod]
    public async Task TryGetByUri_AfterCreateLobby_ReturnsRegistration()
    {
        var service = Build();

        var host = UserFactory.Create("Host", Guid.NewGuid());
        var result = await service.CreateLobbyAsync(host, Route);
        Assert.IsTrue(result.TryGetSuccess(out var created));

        Assert.IsTrue(service.TryGetByUri(created.Uri, out var lookup));
        Assert.AreSame(created, lookup);
    }

    [TestMethod]
    public async Task TryGetByUri_AfterCloseLobby_ReturnsFalse()
    {
        var service = Build();

        var host = UserFactory.Create("Host", Guid.NewGuid());
        var createResult = await service.CreateLobbyAsync(host, Route);
        Assert.IsTrue(createResult.TryGetSuccess(out var created));

        var close = await service.CloseLobbyAsync(host, created);
        Assert.IsTrue(close.IsSuccess);

        Assert.IsFalse(service.TryGetByUri(created.Uri, out var lookup));
        Assert.IsNull(lookup);
    }

    [TestMethod]
    public async Task TryGetByUri_DuringShutdown_StaleLookupReturnsFalse()
    {
        var service = Build();

        var host = UserFactory.Create("Host", Guid.NewGuid());
        var createResult = await service.CreateLobbyAsync(host, Route);
        Assert.IsTrue(createResult.TryGetSuccess(out var created));

        await service.StopAsync(CancellationToken.None);

        Assert.IsFalse(service.TryGetByUri(created.Uri, out _));
    }

    [TestMethod]
    public void TryGetByUri_UnknownUri_ReturnsFalse()
    {
        var service = Build();

        Assert.IsFalse(service.TryGetByUri("room/lobby-test-route/00000000-0000-0000-0000-000000000000-00000000-0000-0000-0000-000000000000", out var lookup));
        Assert.IsNull(lookup);
    }

    [TestMethod]
    public void TryGetByUri_EmptyUri_ReturnsFalse()
    {
        var service = Build();

        Assert.IsFalse(service.TryGetByUri(string.Empty, out var lookup));
        Assert.IsNull(lookup);
    }

    [TestMethod]
    public async Task CreateLobby_InstallsProjectionSubscriber_RemovedOnClose()
    {
        var (service, coordinator) = BuildWithCoordinator();

        var host = UserFactory.Create("Host", Guid.NewGuid());
        var createResult = await service.CreateLobbyAsync(host, Route);
        Assert.IsTrue(createResult.TryGetSuccess(out var created));

        // The subscriber is installed at creation — before any hub join — and
        // lives until the lobby closes.
        Assert.IsTrue(coordinator.HasSubscription(created.Uri),
            "CreateLobbyAsync must install the per-lobby projection subscriber.");

        var close = await service.CloseLobbyAsync(host, created);
        Assert.IsTrue(close.IsSuccess);

        Assert.IsFalse(coordinator.HasSubscription(created.Uri),
            "CloseLobbyAsync must remove the per-lobby projection subscriber.");
    }

    private static LobbyService Build() => BuildWithCoordinator().Service;

    private static (LobbyService Service, GameViewCoordinator Coordinator) BuildWithCoordinator()
    {
        var module = new StubModule();
        var engine = new StubEngine();

        var services = new ServiceCollection();
        services.AddKeyedSingleton<AbstractGameEngine>(module.Manifest.RouteIdentifier, engine);
        var sp = services.BuildServiceProvider();

        var codeService = new Mock<ILobbyCodeService>();
        var nextCode = 0;
        codeService.Setup(c => c.IssueLobbyCodeAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var code = $"CODE{Interlocked.Increment(ref nextCode):D4}";
                return ValueTask.FromResult<ValueResult<string>>(code);
            });
        codeService.Setup(c => c.ReleaseLobbyCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Result>(Result.Success));

        var availability = new Mock<IGameAvailabilityService>();
        availability.Setup(a => a.IsEnabled(It.IsAny<string>())).Returns(true);

        var coordinator = BuildCoordinator(sp);
        var service = new LobbyService(
            sp,
            codeService.Object,
            availability.Object,
            [module],
            coordinator,
            NullLogger<LobbyService>.Instance);
        return (service, coordinator);
    }

    private static GameViewCoordinator BuildCoordinator(IServiceProvider sp)
        => new(
            new Mock<IHubContext<GameHub, IGameClient>>().Object,
            new GameConnectionRegistry(),
            sp,
            NullLogger<GameViewCoordinator>.Instance);

    private sealed class StubModule : IGameModule
    {
        public IPluginManifest Manifest { get; } = new PluginManifest(
            Name: "Lobby Test Plugin",
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

    private sealed class StubGameState(User host) : AbstractGameState(host, NullLogger.Instance)
    {
    }
}
