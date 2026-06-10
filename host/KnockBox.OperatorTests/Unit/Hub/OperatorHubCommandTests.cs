using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnockBox.Operator.Contracts;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.Projection;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Operator.Tests.Unit.Hub;

/// <summary>
/// Hub command-handler tests: a command routes through the engine, mutates state, and is
/// reflected in the projection. Player commands carry the server-resolved caller id (the
/// hub builds a fresh <see cref="User"/> per command), so host gates must compare by id,
/// and turn gates live in the FSM. Covers host-only gating, turn gating, and dispatch.
/// </summary>
[TestClass]
public class OperatorHubCommandTests
{
    private Mock<IRandomNumberService> _random = default!;
    private OperatorGameEngine _engine = default!;
    private OperatorStateProjector _projector = default!;
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
        _projector = new OperatorStateProjector();
        _host = UserFactory.Create("Host", Guid.NewGuid());
    }

    private IGameCommandHandler Handler => _engine;

    private async Task<OperatorGameState> CreateStateAsync()
        => (OperatorGameState)(await _engine.CreateStateAsync(_host)).Value!;

    private static string Settings(OperatorSettingsView v)
        => JsonSerializer.Serialize(v, OperatorContractsJsonContext.Default.OperatorSettingsView);

    [TestMethod]
    public async Task UpdateSettings_FromFreshHostUserInstance_Succeeds_AndProjectionReflectsIt()
    {
        var state = await CreateStateAsync();

        // A DIFFERENT User object with the host's id — exactly what the hub builds per command.
        var freshHost = UserFactory.Create("Host", _host.Id);
        var result = await Handler.HandleCommandAsync(
            freshHost, state, OperatorCommands.UpdateSettings,
            Settings(new OperatorSettingsView { PlayPhaseSeconds = 45 }));

        Assert.IsTrue(result.IsSuccess, "Host check must compare by id, not reference.");
        Assert.AreEqual(TimeSpan.FromSeconds(45), state.Settings.PlayPhaseTimeout);
        Assert.AreEqual(45, _projector.ProjectFor(state, freshHost.Id).Settings.PlayPhaseSeconds);
    }

    [TestMethod]
    public async Task UpdateSettings_FromNonHost_IsRejected()
    {
        var state = await CreateStateAsync();
        var stranger = UserFactory.Create("Stranger", Guid.NewGuid());

        var result = await Handler.HandleCommandAsync(
            stranger, state, OperatorCommands.UpdateSettings,
            Settings(new OperatorSettingsView { PlayPhaseSeconds = 45 }));

        Assert.IsTrue(result.IsFailure);
        Assert.AreNotEqual(TimeSpan.FromSeconds(45), state.Settings.PlayPhaseTimeout);
    }

    [TestMethod]
    public async Task Start_FromNonHost_IsRejected()
    {
        var state = await CreateStateAsync();
        state.RegisterPlayer(UserFactory.Create("P1", Guid.NewGuid()));
        state.RegisterPlayer(UserFactory.Create("P2", Guid.NewGuid()));
        var stranger = UserFactory.Create("Stranger", Guid.NewGuid());

        var result = await Handler.HandleCommandAsync(
            stranger, state, OperatorCommands.Start,
            JsonSerializer.Serialize(new StartPayload(false), OperatorContractsJsonContext.Default.StartPayload));

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(state.IsJoinable, "A rejected start must leave the lobby joinable.");
    }

    [TestMethod]
    public async Task Start_FromHost_EntersSetup()
    {
        var state = await CreateStateAsync();
        state.RegisterPlayer(UserFactory.Create("P1", Guid.NewGuid()));
        state.RegisterPlayer(UserFactory.Create("P2", Guid.NewGuid()));

        var result = await Handler.HandleCommandAsync(
            UserFactory.Create("Host", _host.Id), state, OperatorCommands.Start,
            JsonSerializer.Serialize(new StartPayload(false), OperatorContractsJsonContext.Default.StartPayload));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(OperatorGamePhase.Setup, state.Phase);
        Assert.IsFalse(state.IsJoinable);
    }

    [TestMethod]
    public async Task SubmitSetupChoice_SetsPlayerStartingScore()
    {
        var (state, p1, _) = await StartedToSetupAsync();

        var result = await Handler.HandleCommandAsync(
            UserFactory.Create("P1", p1), state, OperatorCommands.SubmitSetupChoice,
            JsonSerializer.Serialize(new SetupChoicePayload(false), OperatorContractsJsonContext.Default.SetupChoicePayload));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(state.Settings.InitialPointsPositive, state.GamePlayers[p1].CurrentPoints);
    }

    [TestMethod]
    public async Task PlayCards_ByActivePlayer_Succeeds_ByOtherPlayer_IsRejected()
    {
        var (state, p1, p2) = await ReachedPlayPhaseAsync();

        var activeId = state.TurnManager.CurrentPlayer!.Value;
        var otherId = activeId == p1 ? p2 : p1;
        var numberCardId = state.GamePlayers[activeId].Hand.OfType<NumberCard>().First().Id;

        // The non-active player cannot play, even with one of their own cards.
        var otherCardId = state.GamePlayers[otherId].Hand.First().Id;
        var rejected = await Handler.HandleCommandAsync(
            UserFactory.Create("Other", otherId), state, OperatorCommands.PlayCards,
            JsonSerializer.Serialize(new PlayCardsPayload([otherCardId], null), OperatorContractsJsonContext.Default.PlayCardsPayload));
        Assert.IsTrue(rejected.IsFailure, "Only the active player may play.");

        // The active player plays a number card.
        var ok = await Handler.HandleCommandAsync(
            UserFactory.Create("Active", activeId), state, OperatorCommands.PlayCards,
            JsonSerializer.Serialize(new PlayCardsPayload([numberCardId], null), OperatorContractsJsonContext.Default.PlayCardsPayload));
        Assert.IsTrue(ok.IsSuccess);
        Assert.IsTrue(state.GamePlayers[activeId].HasPlayedCardThisTurn);
    }

    [TestMethod]
    public async Task UnknownCommand_IsRejected()
    {
        var state = await CreateStateAsync();
        var result = await Handler.HandleCommandAsync(_host, state, "not-a-command", null);
        Assert.IsTrue(result.IsFailure);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(OperatorGameState state, Guid p1, Guid p2)> StartedToSetupAsync()
    {
        var state = await CreateStateAsync();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        state.RegisterPlayer(UserFactory.Create("P1", p1));
        state.RegisterPlayer(UserFactory.Create("P2", p2));
        await _engine.StartAsync(_host, state);
        return (state, p1, p2);
    }

    private async Task<(OperatorGameState state, Guid p1, Guid p2)> ReachedPlayPhaseAsync()
    {
        var (state, p1, p2) = await StartedToSetupAsync();
        await Handler.HandleCommandAsync(
            UserFactory.Create("P1", p1), state, OperatorCommands.SubmitSetupChoice,
            JsonSerializer.Serialize(new SetupChoicePayload(false), OperatorContractsJsonContext.Default.SetupChoicePayload));
        await Handler.HandleCommandAsync(
            UserFactory.Create("P2", p2), state, OperatorCommands.SubmitSetupChoice,
            JsonSerializer.Serialize(new SetupChoicePayload(true), OperatorContractsJsonContext.Default.SetupChoicePayload));
        Assert.AreEqual(OperatorGamePhase.Play, state.Phase);
        return (state, p1, p2);
    }
}
