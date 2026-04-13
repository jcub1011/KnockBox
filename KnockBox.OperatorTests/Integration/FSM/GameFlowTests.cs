using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Logic.FSM.States;
using KnockBox.Operator.Services.State;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System;
using System.Linq;

namespace KnockBox.OperatorTests.Integration.FSM;

[TestClass]
public class GameFlowTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private FiniteStateMachine<OperatorGameContext, OperatorCommand> _fsm = default!;
    private Mock<IRandomNumberService> _rngMock = default!;

    [TestInitialize]
    public void Setup()
    {
        _rngMock = new Mock<IRandomNumberService>();
        var host = new User("Host", "host1");
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state, _rngMock.Object);
        _fsm = new FiniteStateMachine<OperatorGameContext, OperatorCommand>(NullLogger.Instance);
        _context.Fsm = _fsm;

        _state.GamePlayers.TryAdd("p1", new OperatorPlayerState { UserId = "p1", CurrentPoints = 0m });
        _state.GamePlayers.TryAdd("p2", new OperatorPlayerState { UserId = "p2", CurrentPoints = 0m });

        _state.TurnManager.SetTurnOrder(new List<string> { "p1", "p2" });
        _state.Phase = OperatorGamePhase.Setup;
        _fsm.TransitionTo(_context, new SetupState());
    }

    [TestMethod]
    public void FullGameLoop_Simulated_SuccessfullyTransitions()
    {
        // 1. Setup -> Play Phase
        var setupCmd1 = new SubmitSetupChoiceCommand("p1", 10m);
        _fsm.HandleCommand(_context, setupCmd1);
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(SetupState)); // Not everyone has chosen yet

        var setupCmd2 = new SubmitSetupChoiceCommand("p2", -10m);
        _fsm.HandleCommand(_context, setupCmd2);
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(PlayPhaseState));
        Assert.AreEqual(OperatorGamePhase.Play, _state.Phase);

        // Verify starting operators match choices
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers["p1"].ActiveOperator);
        Assert.AreEqual(CardOperator.Subtract, _state.GamePlayers["p2"].ActiveOperator);

        // 2. Play Phase -> (auto Draw) -> Play Phase (p1 plays a number card)
        var p1 = _state.GamePlayers["p1"];
        var card = new NumberCard(5m);
        p1.Hand.Add(card);
        // Add a card to deck so draw can give p1 a card
        _state.Deck.Add(new NumberCard(1m));

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { card.Id });
        _fsm.HandleCommand(_context, playCmd);
        
        var endCmd = new EndTurnCommand("p1");
        _fsm.HandleCommand(_context, endCmd);
        
        // After play + end turn -> auto-draw -> next player's play phase
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(PlayPhaseState));
        Assert.AreEqual(OperatorGamePhase.Play, _state.Phase);
        Assert.AreEqual(15m, p1.CurrentPoints);
        Assert.AreEqual("p2", _state.TurnManager.CurrentPlayer);

        // 3. Play Phase (p2 plays) -> check GameOver or next turn
        var p2 = _state.GamePlayers["p2"];
        var winCard = new NumberCard(5m);
        p2.Hand.Add(winCard);
        // Ensure deck is empty and hands are only shields to trigger game over
        _state.Deck.Clear();
        p1.Hand.Clear();
        p1.Hand.Add(new ShieldCard());
        p2.Hand.Clear();
        p2.Hand.Add(winCard);

        var winCmd = new PlayCardsCommand("p2", new List<Guid> { winCard.Id });
        _fsm.HandleCommand(_context, winCmd);

        // Deck is empty and all remaining cards are shields -> GameOver
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(GameOverState));
        Assert.AreEqual(OperatorGamePhase.GameOver, _state.Phase);
        Assert.IsNotNull(_state.WinnerPlayerId);
    }
}
