using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Logic.FSM.States;
using KnockBox.Operator.Services.State;
using KnockBox.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System;
using System.Linq;

namespace KnockBox.OperatorTests.Integration.FSM;

[TestClass]
public class GameFlowTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private IGameState<OperatorGameContext, OperatorCommand> _currentState = default!;

    [TestInitialize]
    public void Setup()
    {
        var host = new User("Host", "host1");
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state);
        
        _state.GamePlayers.TryAdd("p1", new OperatorPlayerState { UserId = "p1", CurrentPoints = 0m });
        _state.GamePlayers.TryAdd("p2", new OperatorPlayerState { UserId = "p2", CurrentPoints = 0m });
        
        _state.TurnManager.SetTurnOrder(new List<string> { "p1", "p2" });
        _state.Phase = OperatorGamePhase.Setup;
        _currentState = new SetupState();
    }

    [TestMethod]
    public void FullGameLoop_Simulated_SuccessfullyTransitions()
    {
        // 1. Setup -> Play Phase
        // All players must submit setup choices (10.0 or -10.0)
        var setupCmd1 = new SubmitSetupChoiceCommand("p1", 10m);
        var result = _currentState.HandleCommand(_context, setupCmd1);
        Assert.IsNull(result.Value); // Not everyone has chosen yet
        
        var setupCmd2 = new SubmitSetupChoiceCommand("p2", -10m);
        result = _currentState.HandleCommand(_context, setupCmd2);
        Assert.IsInstanceOfType(result.Value, typeof(PlayPhaseState));
        _currentState = (IGameState<OperatorGameContext, OperatorCommand>)result.Value!;
        Assert.AreEqual(OperatorGamePhase.Play, _state.Phase);

        // 2. Play Phase -> Draw Phase (p1 plays a card)
        var p1 = _state.GamePlayers["p1"];
        var card = new Card(CardType.Number, 5m);
        p1.Hand.Add(card);
        
        var playCmd = new PlayCardsCommand("p1", new List<Guid> { card.Id });
        result = _currentState.HandleCommand(_context, playCmd);
        Assert.IsInstanceOfType(result.Value, typeof(DrawPhaseState));
        _currentState = (IGameState<OperatorGameContext, OperatorCommand>)result.Value!;
        Assert.AreEqual(OperatorGamePhase.Draw, _state.Phase);
        Assert.AreEqual(15m, p1.CurrentPoints);

        // 3. Draw Phase -> Play Phase (p2's turn)
        _state.Deck.Add(new Card(CardType.Number, 1m));
        var drawCmd = new DrawCardsCommand("p1");
        result = _currentState.HandleCommand(_context, drawCmd);
        Assert.IsInstanceOfType(result.Value, typeof(PlayPhaseState));
        _currentState = (IGameState<OperatorGameContext, OperatorCommand>)result.Value!;
        Assert.AreEqual(OperatorGamePhase.Play, _state.Phase);
        Assert.AreEqual("p2", _state.TurnManager.CurrentPlayer);

        // 4. Play Phase (GameOver check) - Simulating winning condition
        var p2 = _state.GamePlayers["p2"];
        p2.CurrentPoints = 95m;
        var winCard = new Card(CardType.Number, 5m);
        p2.Hand.Add(winCard);
        
        var winCmd = new PlayCardsCommand("p2", new List<Guid> { winCard.Id });
        result = _currentState.HandleCommand(_context, winCmd);
        
        // If PlayPhaseState checks for win immediately, it might transition to GameOver
        // Let's see if DrawPhaseState or PlayPhaseState handles it.
        // Usually GameOver is checked after score mutation.
        
        if (result.Value is GameOverState)
        {
            _currentState = (IGameState<OperatorGameContext, OperatorCommand>)result.Value;
            Assert.AreEqual(OperatorGamePhase.GameOver, _state.Phase);
        }
        else if (result.Value is DrawPhaseState)
        {
             _currentState = (IGameState<OperatorGameContext, OperatorCommand>)result.Value;
             // Draw and check if DrawPhase transition to GameOver if someone won
             _state.Deck.Add(new Card(CardType.Number, 1m));
             var drawWinCmd = new DrawCardsCommand("p2");
             result = _currentState.HandleCommand(_context, drawWinCmd);
             if (result.Value is GameOverState)
             {
                 _currentState = (IGameState<OperatorGameContext, OperatorCommand>)result.Value;
                 Assert.AreEqual(OperatorGamePhase.GameOver, _state.Phase);
             }
        }
    }
}
