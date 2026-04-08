using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;
using KnockBox.Services.Logic.RandomGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;

namespace KnockBox.OperatorTests.Unit.Context;

[TestClass]
public class ActionCardTests
{
    private OperatorGameContext _context = default!;
    private OperatorGameState _state = default!;
    private Mock<IRandomNumberService> _rngMock = default!;
    private Mock<ILogger<OperatorGameState>> _loggerMock = default!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<OperatorGameState>>();
        var host = new KnockBox.Services.State.Users.User("host", "Host");
        _state = new OperatorGameState(host, _loggerMock.Object);
        _rngMock = new Mock<IRandomNumberService>();
        _context = new OperatorGameContext(_state, _rngMock.Object);
    }

    [TestMethod]
    public void ResolveSurcharge_AddsValueDirectly_IgnoringOperator()
    {
        // Arrange
        var player = new OperatorPlayerState { UserId = "p1", CurrentPoints = 0m, ActiveOperator = CardOperator.Multiply };
        _state.GamePlayers["p1"] = player;

        // Act
        _context.ResolveSurcharge("p1", 5m);

        // Assert
        Assert.AreEqual(5m, player.CurrentPoints);
        Assert.AreEqual(CardOperator.Multiply, player.ActiveOperator);
    }

    [TestMethod]
    public void ResolveBlueShell_ResetsOnlyPlayersAtZero()
    {
        // Arrange
        var p1 = new OperatorPlayerState { UserId = "p1", CurrentPoints = 0m, ActiveOperator = CardOperator.Multiply };
        var p2 = new OperatorPlayerState { UserId = "p2", CurrentPoints = 5m, ActiveOperator = CardOperator.Subtract };
        _state.GamePlayers["p1"] = p1;
        _state.GamePlayers["p2"] = p2;

        // Act
        _context.ResolveBlueShell();

        // Assert
        Assert.AreEqual(10m, p1.CurrentPoints);
        Assert.AreEqual(CardOperator.Add, p1.ActiveOperator);
        
        Assert.AreEqual(5m, p2.CurrentPoints);
        Assert.AreEqual(CardOperator.Subtract, p2.ActiveOperator);
    }

    [TestMethod]
    public void BlueShell_IsPlayable_OnlyIfSomeoneIsAtZero()
    {
        // Arrange
        var p1 = new OperatorPlayerState { UserId = "p1", CurrentPoints = 5m };
        _state.GamePlayers["p1"] = p1;
        var blueShell = new BlueShellCard();

        // Act & Assert
        Assert.IsFalse(blueShell.IsPlayable(_context, p1));

        p1.CurrentPoints = 0m;
        Assert.IsTrue(blueShell.IsPlayable(_context, p1));
    }
}
