using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Logic.FSM.States;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System;
using System.Linq;

namespace KnockBox.Operator.Tests.Integration.FSM;

[TestClass]
public class GameFlowTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private FiniteStateMachine<OperatorGameContext, OperatorCommand> _fsm = default!;
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
        _fsm = new FiniteStateMachine<OperatorGameContext, OperatorCommand>(NullLogger.Instance);
        _context.Fsm = _fsm;

        _p1Id = Guid.NewGuid();
        _p2Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(_p1Id, new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m });
        _state.GamePlayers.TryAdd(_p2Id, new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 0m });

        _state.TurnManager.SetTurnOrder([_p1Id, _p2Id]);
        _state.Phase = OperatorGamePhase.Setup;
        _fsm.TransitionTo(_context, new SetupState());
    }

    [TestMethod]
    public void FullGameLoop_Simulated_SuccessfullyTransitions()
    {
        // 1. Setup -> Play Phase
        var setupCmd1 = new SubmitSetupChoiceCommand(_p1Id, 10m);
        _fsm.HandleCommand(_context, setupCmd1);
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(SetupState)); // Not everyone has chosen yet

        var setupCmd2 = new SubmitSetupChoiceCommand(_p2Id, -10m);
        _fsm.HandleCommand(_context, setupCmd2);
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(PlayPhaseState));
        Assert.AreEqual(OperatorGamePhase.Play, _state.Phase);

        // Verify starting operators match choices
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p1Id].ActiveOperator);
        Assert.AreEqual(CardOperator.Subtract, _state.GamePlayers[_p2Id].ActiveOperator);

        // 2. Play Phase -> (auto Draw) -> Play Phase (p1 plays a number card)
        var p1 = _state.GamePlayers[_p1Id];
        var card = new NumberCard(5m);
        p1.Hand.Add(card);
        // Add a card to deck so draw can give p1 a card
        _state.Deck.Add(new NumberCard(1m));

        var playCmd = new PlayCardsCommand(_p1Id, [card.Id]);
        _fsm.HandleCommand(_context, playCmd);

        var endCmd = new EndTurnCommand(_p1Id);
        _fsm.HandleCommand(_context, endCmd);

        // After play + end turn -> auto-draw -> next player's play phase
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(PlayPhaseState));
        Assert.AreEqual(OperatorGamePhase.Play, _state.Phase);
        Assert.AreEqual(15m, p1.CurrentPoints);
        Assert.AreEqual(_p2Id, _state.TurnManager.CurrentPlayer);

        // 3. Play Phase (p2 plays) -> check GameOver or next turn
        var p2 = _state.GamePlayers[_p2Id];
        var winCard = new NumberCard(5m);
        p2.Hand.Add(winCard);
        // Ensure deck is empty and hands are only shields to trigger game over
        _state.Deck.Clear();
        p1.Hand.Clear();
        p1.Hand.Add(new ShieldCard());
        p2.Hand.Clear();
        p2.Hand.Add(winCard);

        var winCmd = new PlayCardsCommand(_p2Id, [winCard.Id]);
        _fsm.HandleCommand(_context, winCmd);

        // Deck is empty and all remaining cards are shields -> GameOver
        Assert.IsInstanceOfType(_fsm.CurrentState, typeof(GameOverState));
        Assert.AreEqual(OperatorGamePhase.GameOver, _state.Phase);
        Assert.IsNotNull(_state.WinnerPlayerId);
    }
}
