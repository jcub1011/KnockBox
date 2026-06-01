using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Linq;
using System.Threading.Tasks;

namespace KnockBox.Operator.Tests.Integration.FSM;

[TestClass]
public class HostPlaysStartTests
{
    private OperatorGameEngine _engine = default!;
    private User _host = default!;

    [TestInitialize]
    public void Setup()
    {
        _engine = new OperatorGameEngine(
            NullLogger<OperatorGameEngine>.Instance,
            NullLogger<OperatorGameState>.Instance,
            new Mock<IRandomNumberService>().Object);
        _host = UserFactory.Create("Host", "host-id");
    }

    private async Task<OperatorGameState> CreateStateWithPlayersAsync(params string[] playerIds)
    {
        var createResult = await _engine.CreateStateAsync(_host);
        Assert.IsTrue(createResult.TryGetSuccess(out var abstractState));
        var state = (OperatorGameState)abstractState!;

        foreach (var id in playerIds)
        {
            var reg = state.RegisterPlayer(UserFactory.Create(id, id));
            Assert.IsTrue(reg.TryGetSuccess(out _), $"Failed to register player [{id}].");
        }

        return state;
    }

    [TestMethod]
    public async Task StartAsync_HostPlays_SeatsHostAsParticipant()
    {
        var state = await CreateStateWithPlayersAsync("p1");
        state.UpdateSettings(s => s with { HostPlays = true });

        var result = await _engine.StartAsync(_host, state);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(state.GamePlayers.ContainsKey(_host.Id));
        Assert.IsTrue(state.TurnManager.TurnOrder.Contains(_host.Id));
        Assert.AreEqual(state.Players.Length + 1, state.Participants.Length);
        Assert.AreEqual(state.Participants.Length, state.GamePlayers.Count);
    }

    [TestMethod]
    public async Task StartAsync_HostDoesNotPlay_HostIsAbsentFromGameplay()
    {
        var state = await CreateStateWithPlayersAsync("p1", "p2");
        state.UpdateSettings(s => s with { HostPlays = false });

        var result = await _engine.StartAsync(_host, state);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(state.GamePlayers.ContainsKey(_host.Id));
        Assert.IsFalse(state.TurnManager.TurnOrder.Contains(_host.Id));
        Assert.AreEqual(state.Players.Length, state.Participants.Length);
        Assert.AreEqual(state.Players.Length, state.GamePlayers.Count);
    }
}
