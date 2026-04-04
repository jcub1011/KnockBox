using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Logic.FSM.States;
using KnockBox.Operator.Services.State;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System;

namespace KnockBox.OperatorTests.Integration.FSM;

[TestClass]
public class ActionReactionTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private PlayPhaseState _playPhase = default!;
    private ReactionState _reactionPhase = default!;
    private Mock<IRandomNumberService> _rngMock = default!;

    [TestInitialize]
    public void Setup()
    {
        _rngMock = new Mock<IRandomNumberService>();
        var host = new User("Host", "host1");
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state, _rngMock.Object);
        
        _state.GamePlayers.TryAdd("p1", new OperatorPlayerState { UserId = "p1", CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        _state.GamePlayers.TryAdd("p2", new OperatorPlayerState { UserId = "p2", CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        
        _state.TurnManager.SetTurnOrder(new List<string> { "p1", "p2" });

        _playPhase = new PlayPhaseState();
        _reactionPhase = new ReactionState();
    }

    [TestMethod]
    public void TargetedAction_TransitionsToReactionState()
    {
        var stealCard = new StealCard();
        _state.GamePlayers["p1"].Hand.Add(stealCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { stealCard.Id }, "p2");
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsInstanceOfType(result.Value, typeof(ReactionState));
        Assert.AreEqual(OperatorGamePhase.Reaction, _state.Phase);
        Assert.AreEqual("p2", _state.ReactionTargetPlayerId);
    }

    [TestMethod]
    public void LiabilityTransfer_Passed_RedirectsScoreMutation()
    {
        var liabilityCard = new LiabilityTransferCard();
        var numberCard = new NumberCard(5m);
        
        _state.GamePlayers["p1"].Hand.Add(liabilityCard);
        _state.GamePlayers["p1"].Hand.Add(numberCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { liabilityCard.Id, numberCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand("p2");
        var reactionResult = _reactionPhase.HandleCommand(_context, passCmd);

        Assert.IsInstanceOfType(reactionResult.Value, typeof(DrawPhaseState));
        Assert.AreEqual(10m, _state.GamePlayers["p1"].CurrentPoints);
        Assert.AreEqual(15m, _state.GamePlayers["p2"].CurrentPoints);
    }

    [TestMethod]
    public void TargetedAction_BlockedByShield_NullifiesEffect()
    {
        var stealCard = new StealCard();
        var shieldCard = new ShieldCard();
        
        _state.GamePlayers["p1"].Hand.Add(stealCard);
        _state.GamePlayers["p2"].Hand.Add(shieldCard);
        
        // P2 has a card to steal
        var cardToSteal = new NumberCard(9m);
        _state.GamePlayers["p2"].Hand.Add(cardToSteal);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { stealCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand("p2", shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        // Steal blocked. P1 hand should be empty (Steal played). P2 should still have the cardToSteal.
        Assert.AreEqual(0, _state.GamePlayers["p1"].Hand.Count);
        Assert.AreEqual(1, _state.GamePlayers["p2"].Hand.Count);
        Assert.AreEqual(cardToSteal.Id, _state.GamePlayers["p2"].Hand[0].Id);
    }
}