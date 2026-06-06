using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Logic.FSM.States;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System;

namespace KnockBox.Operator.Tests.Integration.FSM;

[TestClass]
public class ActionReactionTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private PlayPhaseState _playPhase = default!;
    private ReactionState _reactionPhase = default!;
    private Mock<IRandomNumberService> _rngMock = default!;
    private Guid _p1Id = default!;
    private Guid _p2Id = default!;

    [TestInitialize]
    public void Setup()
    {
        _rngMock = new Mock<IRandomNumberService>();
        var host = UserFactory.Create("Host", Guid.NewGuid());
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state, _rngMock.Object);

        _p1Id = Guid.NewGuid();
        _p2Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(_p1Id, new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        _state.GamePlayers.TryAdd(_p2Id, new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add });

        _state.TurnManager.SetTurnOrder([_p1Id, _p2Id]);

        _playPhase = new PlayPhaseState();
        _reactionPhase = new ReactionState();
    }

    [TestMethod]
    public void TargetedAction_TransitionsToReactionState()
    {
        var stealCard = new StealCard();
        _state.GamePlayers[_p1Id].Hand.Add(stealCard);
        _state.GamePlayers[_p2Id].Hand.Add(new NumberCard(5m)); // P2 needs a card for Steal to be playable

        var playCmd = new PlayCardsCommand(_p1Id, [stealCard.Id], _p2Id);
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsInstanceOfType(result.Value, typeof(ReactionState));
        Assert.AreEqual(OperatorGamePhase.Reaction, _state.Phase);
        Assert.Contains(_p2Id, _state.ReactionTargetPlayerIds);
    }

    [TestMethod]
    public void HotPotato_Passed_RedirectsScoreMutation()
    {
        var hpCard = new HotPotatoCard();
        var numberCard = new NumberCard(5m);

        _state.GamePlayers[_p1Id].Hand.Add(hpCard);
        _state.GamePlayers[_p1Id].Hand.Add(numberCard);

        var playCmd = new PlayCardsCommand(_p1Id, [hpCard.Id, numberCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand(_p2Id);
        var reactionResult = _reactionPhase.HandleCommand(_context, passCmd);

        Assert.IsInstanceOfType(reactionResult.Value, typeof(DrawPhaseState));
        Assert.AreEqual(10m, _state.GamePlayers[_p1Id].CurrentPoints);
        Assert.AreEqual(15m, _state.GamePlayers[_p2Id].CurrentPoints);
    }

    [TestMethod]
    public void TargetedAction_BlockedByShield_NullifiesEffect()
    {
        var stealCard = new StealCard();
        var shieldCard = new ShieldCard();

        _state.GamePlayers[_p1Id].Hand.Add(stealCard);
        _state.GamePlayers[_p2Id].Hand.Add(shieldCard);

        // P2 has a card to steal
        var cardToSteal = new NumberCard(9m);
        _state.GamePlayers[_p2Id].Hand.Add(cardToSteal);

        var playCmd = new PlayCardsCommand(_p1Id, [stealCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand(_p2Id, shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        // Steal blocked. P1 hand should be empty (Steal played). P2 should still have the cardToSteal.
        Assert.IsEmpty(_state.GamePlayers[_p1Id].Hand);
        Assert.HasCount(1, _state.GamePlayers[_p2Id].Hand);
        Assert.AreEqual(cardToSteal.Id, _state.GamePlayers[_p2Id].Hand[0].Id);
    }
}