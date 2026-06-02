using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.Operator.Tests.Unit.Logic;

[TestClass]
public class OperatorGameEngineReturnToLobbyTests
{
    private OperatorGameEngine _engine = default!;
    private User _host = default!;

    [TestInitialize]
    public void Setup()
    {
        _host = UserFactory.Create("Host", "host1");
        _engine = new OperatorGameEngine(
            NullLogger<OperatorGameEngine>.Instance,
            NullLogger<OperatorGameState>.Instance,
            new Mock<IRandomNumberService>().Object);
    }

    private async Task<OperatorGameState> CreateStartedGameAsync()
    {
        var result = await _engine.CreateStateAsync(_host);
        var state = (OperatorGameState)result.Value!;
        state.RegisterPlayer(UserFactory.Create("P1", "p1"));
        state.RegisterPlayer(UserFactory.Create("P2", "p2"));
        await _engine.StartAsync(_host, state);
        return state;
    }

    [TestMethod]
    public async Task ReturnToLobby_NonHost_ReturnsError()
    {
        var state = await CreateStartedGameAsync();
        state.Phase = OperatorGamePhase.GameOver;
        var nonHost = UserFactory.Create("NotHost", "nothost-id");

        var result = _engine.ReturnToLobby(nonHost, state);

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public async Task ReturnToLobby_BeforeGameOver_ReturnsError()
    {
        var state = await CreateStartedGameAsync();
        // A started game is in Setup/Play, not GameOver — the replay path is rejected.

        var result = _engine.ReturnToLobby(_host, state);

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public async Task ReturnToLobby_AfterGameOver_ReturnsToJoinableSetup()
    {
        var state = await CreateStartedGameAsync();
        state.Phase = OperatorGamePhase.GameOver;

        var result = _engine.ReturnToLobby(_host, state);

        Assert.IsTrue((bool)result.IsSuccess);
        Assert.AreEqual(OperatorGamePhase.Setup, state.Phase);
        Assert.IsTrue(state.IsJoinable);
        Assert.IsEmpty(state.GamePlayers);
        Assert.IsEmpty(state.TurnManager.TurnOrder);
        Assert.IsNull(state.WinnerPlayerId);
    }
}
