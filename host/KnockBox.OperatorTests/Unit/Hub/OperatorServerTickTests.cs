using System;
using System.Linq;
using System.Threading.Tasks;
using KnockBox.Operator.Contracts;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Operator.Tests.Unit.Hub;

/// <summary>
/// Server-clock tests. Operator's FSM was driven by the host browser circuit in the
/// Blazor Server model; in WASM the engine implements <see cref="IServerTickHandler"/> and
/// the platform's LobbyTickService drives it. A Play-phase timeout must auto-advance the
/// turn, and ticking must be a no-op when timers are disabled.
/// </summary>
[TestClass]
public class OperatorServerTickTests
{
    private Mock<IRandomNumberService> _random = default!;
    private OperatorGameEngine _engine = default!;
    private User _host = default!;

    [TestInitialize]
    public void Setup()
    {
        _random = new Mock<IRandomNumberService>();
        _random.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(1);
        _random.Setup(r => r.GetRandomInt(It.IsAny<int>())).Returns(0);
        _engine = new OperatorGameEngine(
            Mock.Of<ILogger<OperatorGameEngine>>(),
            Mock.Of<ILogger<OperatorGameState>>(),
            _random.Object);
        _host = UserFactory.Create("Host", Guid.NewGuid());
    }

    private IGameCommandHandler Handler => _engine;
    private IServerTickHandler Ticker => _engine;

    [TestMethod]
    public async Task Tick_PastPlayDeadline_AutoAdvancesTheTurn()
    {
        var (state, _) = await ReachedPlayPhaseAsync();
        var before = state.TurnManager.CurrentPlayer!.Value;

        // Push the phase entry well past the play timeout, then drive the server clock.
        state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(120);
        Ticker.Tick(state, DateTimeOffset.UtcNow);

        Assert.AreNotEqual(before, state.TurnManager.CurrentPlayer!.Value,
            "A play-phase timeout should auto-play and advance to the next player.");
    }

    [TestMethod]
    public async Task Tick_WithTimersDisabled_IsNoOp()
    {
        var (state, _) = await ReachedPlayPhaseAsync();
        state.UpdateSettings(s => s with { TimersEnabled = false });
        var before = state.TurnManager.CurrentPlayer!.Value;

        state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(120);
        Ticker.Tick(state, DateTimeOffset.UtcNow);

        Assert.AreEqual(before, state.TurnManager.CurrentPlayer!.Value, "Disabled timers must not advance.");
        Assert.AreEqual(OperatorGamePhase.Play, state.Phase);
    }

    private async Task<(OperatorGameState state, Guid[] ids)> ReachedPlayPhaseAsync()
    {
        var state = (OperatorGameState)(await _engine.CreateStateAsync(_host)).Value!;
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        state.RegisterPlayer(UserFactory.Create("P1", p1));
        state.RegisterPlayer(UserFactory.Create("P2", p2));
        await _engine.StartAsync(_host, state);
        await Handler.HandleCommandAsync(
            UserFactory.Create("P1", p1), state, OperatorCommands.SubmitSetupChoice,
            System.Text.Json.JsonSerializer.Serialize(new SetupChoicePayload(false), OperatorContractsJsonContext.Default.SetupChoicePayload));
        await Handler.HandleCommandAsync(
            UserFactory.Create("P2", p2), state, OperatorCommands.SubmitSetupChoice,
            System.Text.Json.JsonSerializer.Serialize(new SetupChoicePayload(true), OperatorContractsJsonContext.Default.SetupChoicePayload));
        Assert.AreEqual(OperatorGamePhase.Play, state.Phase);
        return (state, [p1, p2]);
    }
}
