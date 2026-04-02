using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Logic.FSM.States;
using KnockBox.Operator.Services.State;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System;
using System.Linq;

namespace KnockBox.OperatorTests.Unit.Logic;

[TestClass]
public class FsmActionResolutionTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private PlayPhaseState _playPhase = default!;
    private ReactionState _reactionPhase = default!;

    [TestInitialize]
    public void Setup()
    {
        var host = new User("Host", "host1");
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state);
        
        _state.GamePlayers.TryAdd("p1", new OperatorPlayerState { UserId = "p1", CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        _state.GamePlayers.TryAdd("p2", new OperatorPlayerState { UserId = "p2", CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        
        _state.TurnManager.SetTurnOrder(new List<string> { "p1", "p2" });

        _playPhase = new PlayPhaseState();
        _reactionPhase = new ReactionState();
    }

    [TestMethod]
    public void LiabilityTransfer_Passed_RedirectsScoreMutation()
    {
        var liabilityCard = new Card(CardType.Action, ActionValue: CardAction.LiabilityTransfer);
        var numberCard = new Card(CardType.Number, NumberValue: 5m);
        
        _state.GamePlayers["p1"].Hand.Add(liabilityCard);
        _state.GamePlayers["p1"].Hand.Add(numberCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { liabilityCard.Id, numberCard.Id }, "p2");
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsInstanceOfType(result.Value, typeof(ReactionState));
        Assert.AreEqual(OperatorGamePhase.Reaction, _state.Phase);
        Assert.AreEqual("p2", _state.ReactionTargetPlayerId);

        // Before reaction, scores should be unchanged
        Assert.AreEqual(10m, _state.GamePlayers["p1"].CurrentPoints);
        Assert.AreEqual(10m, _state.GamePlayers["p2"].CurrentPoints);

        var passCmd = new PassReactionCommand("p2");
        var reactionResult = _reactionPhase.HandleCommand(_context, passCmd);

        Assert.IsInstanceOfType(reactionResult.Value, typeof(DrawPhaseState));
        Assert.AreEqual(OperatorGamePhase.Draw, _state.Phase);

        // P1 score unchanged, P2 took the hit (+5 because operator was Add)
        Assert.AreEqual(10m, _state.GamePlayers["p1"].CurrentPoints);
        Assert.AreEqual(15m, _state.GamePlayers["p2"].CurrentPoints);
    }

    [TestMethod]
    public void TargetedAction_BlockedByShield_NoEffectApplied()
    {
        var stealCard = new Card(CardType.Action, ActionValue: CardAction.Steal);
        var shieldCard = new Card(CardType.Action, ActionValue: CardAction.Shield);
        var numberCard = new Card(CardType.Number, NumberValue: 5m); // play a number alongside Steal
        
        _state.GamePlayers["p1"].Hand.Add(stealCard);
        _state.GamePlayers["p1"].Hand.Add(numberCard);
        
        _state.GamePlayers["p2"].Hand.Add(shieldCard);
        _state.GamePlayers["p2"].Hand.Add(new Card(CardType.Number, 9m)); // Something to steal

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { stealCard.Id, numberCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand("p2", shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        // Steal was blocked, so P1's hand should be empty (Steal and Number played)
        Assert.AreEqual(0, _state.GamePlayers["p1"].Hand.Count);
        // P2 discarded Shield, kept the 9m card
        Assert.AreEqual(1, _state.GamePlayers["p2"].Hand.Count);

        // The number card should still resolve to P1 since Steal was blocked, not LiabilityTransfer!
        // So P1 score should be 15
        Assert.AreEqual(15m, _state.GamePlayers["p1"].CurrentPoints);
    }

    [TestMethod]
    public void Audit_LocksOperator_ForNextTurn()
    {
        var auditCard = new Card(CardType.Action, ActionValue: CardAction.Audit);
        
        _state.GamePlayers["p1"].Hand.Add(auditCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { auditCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand("p2");
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.IsTrue(_state.GamePlayers["p2"].IsAudited);

        // Now it's P2's turn
        _state.TurnManager.NextTurn(); // P2 is current player
        
        var changeOpCard = new Card(CardType.Operator, OperatorValue: CardOperator.Multiply);
        _state.GamePlayers["p2"].Hand.Add(changeOpCard);

        var p2PlayCmd = new PlayCardsCommand("p2", new List<Guid> { changeOpCard.Id });
        _playPhase.HandleCommand(_context, p2PlayCmd);

        // Since audited, operator shouldn't change
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers["p2"].ActiveOperator);
    }
}
