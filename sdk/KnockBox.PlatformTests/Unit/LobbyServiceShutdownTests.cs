using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Games;
using KnockBox.Services.Logic.Games.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Pins the IHostedService shutdown hook on <see cref="LobbyService"/>: every
/// open lobby's state gets disposed on <c>StopAsync</c>, and the <c>_lobbies</c>
/// dictionary is cleared so a restart doesn't inherit stale registrations.
/// </summary>
[TestClass]
public sealed class LobbyServiceShutdownTests
{
    [TestMethod]
    public async Task StopAsync_DisposesEveryOpenLobbyState()
    {
        var (service, codeService) = BuildServiceWithStubEngine();

        var host = UserFactory.Create("Host", Guid.NewGuid().ToString());
        var result = await service.CreateLobbyAsync(host, "shutdown-test-route");
        Assert.IsTrue(result.TryGetSuccess(out var registration));

        var stubState = (StubGameState)registration.State;
        Assert.IsFalse(stubState.IsDisposed);

        await service.StopAsync(CancellationToken.None);

        Assert.IsTrue(stubState.IsDisposed, "StopAsync must dispose every open lobby's state.");
        Assert.AreEqual(0, service.GetLobbyCountsByRoute().Values.Sum(),
            "Open-lobby dictionary must be empty after shutdown.");
    }

    [TestMethod]
    public async Task CreateLobbyAsync_AfterStopAsync_ReturnsError()
    {
        // A lobby created after StopAsync starts would leak — its state would
        // never hit the snapshot-and-dispose loop. The shutdown flag rejects
        // the request before we spin up an engine state.
        var (service, _) = BuildServiceWithStubEngine();

        await service.StopAsync(CancellationToken.None);

        var host = UserFactory.Create("Host", Guid.NewGuid().ToString());
        var result = await service.CreateLobbyAsync(host, "shutdown-test-route");

        Assert.IsTrue(result.IsFailure, "CreateLobbyAsync must reject requests after shutdown begins.");
    }

    private static (LobbyService service, Mock<ILobbyCodeService> codeService) BuildServiceWithStubEngine()
    {
        var module = new StubModule();
        var engine = new StubEngine();

        var services = new ServiceCollection();
        services.AddKeyedSingleton<AbstractGameEngine>(module.Manifest.RouteIdentifier, engine);
        var sp = services.BuildServiceProvider();

        var codeService = new Mock<ILobbyCodeService>();
        var issuedCode = "ABCDEF";
        codeService.Setup(c => c.IssueLobbyCodeAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ValueResult<string>>(issuedCode));
        codeService.Setup(c => c.ReleaseLobbyCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Result>(Result.Success));

        var availability = new Mock<IGameAvailabilityService>();
        availability.Setup(a => a.IsEnabled(It.IsAny<string>())).Returns(true);

        var lobby = new LobbyService(
            sp,
            codeService.Object,
            availability.Object,
            [module],
            NullLogger<LobbyService>.Instance);

        return (lobby, codeService);
    }

    // ─── Stub plugin primitives ─────────────────────────────────────────────

    private sealed class StubModule : IGameModule
    {
        public IPluginManifest Manifest { get; } = new PluginManifest(
            Name: "Shutdown Test Plugin",
            Description: "Fixture.",
            RouteIdentifier: "shutdown-test-route",
            Version: new Version(1, 0, 0),
            EntryAssembly: "Fixture.Assembly",
            Capabilities: new HashSet<PluginCapability>());
        public void RegisterServices(IPluginRegistration registration) { }
        public RenderFragment GetButtonContent() => _ => { };
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
