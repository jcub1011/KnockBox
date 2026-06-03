using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.Logic.FSM.States;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System;
using System.Linq;

namespace KnockBox.Operator.Tests.Integration.FSM;

[TestClass]
public class SetupStateTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private SetupState _setupState = default!;
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
        _state.GamePlayers.TryAdd(_p1Id, new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m });
        _state.GamePlayers.TryAdd(_p2Id, new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 0m });

        _state.TurnManager.SetTurnOrder([_p1Id, _p2Id]);
        _setupState = new SetupState();
        _setupState.OnEnter(_context);
    }

    [TestMethod]
    public void SubmitChoice_InvalidValue_ReturnsError()
    {
        var cmd = new SubmitSetupChoiceCommand(_p1Id, 5m);
        var result = _setupState.HandleCommand(_context, cmd);
        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void SubmitChoice_NonExistentPlayer_ReturnsError()
    {
        var cmd = new SubmitSetupChoiceCommand(Guid.NewGuid(), 10m);
        var result = _setupState.HandleCommand(_context, cmd);
        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void SubmitChoice_PositiveValue_SetsAddOperator()
    {
        var cmd = new SubmitSetupChoiceCommand(_p1Id, 10m);
        _setupState.HandleCommand(_context, cmd);

        Assert.AreEqual(10m, _state.GamePlayers[_p1Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p1Id].ActiveOperator);
    }

    [TestMethod]
    public void SubmitChoice_NegativeValue_SetsSubtractOperator()
    {
        var cmd = new SubmitSetupChoiceCommand(_p1Id, -10m);
        _setupState.HandleCommand(_context, cmd);

        Assert.AreEqual(-10m, _state.GamePlayers[_p1Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Subtract, _state.GamePlayers[_p1Id].ActiveOperator);
    }

    [TestMethod]
    public void SubmitChoice_NotAllPlayersChosen_StaysInSetup()
    {
        var cmd = new SubmitSetupChoiceCommand(_p1Id, 10m);
        var result = _setupState.HandleCommand(_context, cmd);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public void SubmitChoice_AllPlayersChosen_TransitionsToPlay()
    {
        _setupState.HandleCommand(_context, new SubmitSetupChoiceCommand(_p1Id, 10m));
        var result = _setupState.HandleCommand(_context, new SubmitSetupChoiceCommand(_p2Id, -10m));

        Assert.IsInstanceOfType(result.Value, typeof(PlayPhaseState));
        Assert.AreEqual(OperatorGamePhase.Play, _state.Phase);
    }

    [TestMethod]
    public void SubmitChoice_AllPlayersChosen_DealsCards()
    {
        _setupState.HandleCommand(_context, new SubmitSetupChoiceCommand(_p1Id, 10m));
        _setupState.HandleCommand(_context, new SubmitSetupChoiceCommand(_p2Id, -10m));

        Assert.HasCount(5, _state.GamePlayers[_p1Id].Hand);
        Assert.HasCount(5, _state.GamePlayers[_p2Id].Hand);
    }

    [TestMethod]
    public void Tick_Timeout_DefaultsUnchosen()
    {
        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(120);
        var result = _setupState.Tick(_context, DateTimeOffset.UtcNow);

        // Both players should have been defaulted to positive
        Assert.AreEqual(_state.Settings.InitialPointsPositive, _state.GamePlayers[_p1Id].CurrentPoints);
        Assert.AreEqual(_state.Settings.InitialPointsPositive, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.IsInstanceOfType(result.Value, typeof(PlayPhaseState));
    }

    [TestMethod]
    public void Tick_TimersDisabled_DoesNotTimeout()
    {
        _state.UpdateSettings(s => s with { TimersEnabled = false });
        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(120);
        var result = _setupState.Tick(_context, DateTimeOffset.UtcNow);

        Assert.IsNull(result.Value);
    }
}

[TestClass]
public class DrawPhaseStateTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
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
        _state.GamePlayers.TryAdd(_p2Id, new OperatorPlayerState { UserId = _p2Id, CurrentPoints = -10m, ActiveOperator = CardOperator.Subtract });

        _state.TurnManager.SetTurnOrder([_p1Id, _p2Id]);
    }

    [TestMethod]
    public void OnEnter_DrawsUpToMaxDraw()
    {
        // p1 has 3 cards, should draw 2 (min of MaxDrawPerTurn=3, MaxHandSize-3=2)
        _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(1m));
        _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(2m));
        _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(3m));

        for (int i = 0; i < 10; i++)
            _state.Deck.Add(new NumberCard(i));

        var drawPhase = new DrawPhaseState();
        drawPhase.OnEnter(_context);

        Assert.HasCount(5, _state.GamePlayers[_p1Id].Hand);
    }

    [TestMethod]
    public void OnEnter_EmptyDeck_NoPlayableCards_GameOver()
    {
        // Give players only shields (unplayable)
        _state.GamePlayers[_p1Id].Hand.Add(new ShieldCard());
        _state.GamePlayers[_p2Id].Hand.Add(new ShieldCard());
        _state.Deck.Clear();

        var drawPhase = new DrawPhaseState();
        var result = drawPhase.OnEnter(_context);

        Assert.IsInstanceOfType(result.Value, typeof(GameOverState));
        Assert.AreEqual(OperatorGamePhase.GameOver, _state.Phase);
    }

    [TestMethod]
    public void OnEnter_EmptyDeck_PlayableCards_ContinuesGame()
    {
        _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(5m));
        _state.GamePlayers[_p2Id].Hand.Add(new ShieldCard());
        _state.Deck.Clear();

        var drawPhase = new DrawPhaseState();
        var result = drawPhase.OnEnter(_context);

        Assert.IsInstanceOfType(result.Value, typeof(PlayPhaseState));
    }

    [TestMethod]
    public void OnEnter_AdvancesTurn()
    {
        _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(1m));
        _state.Deck.Add(new NumberCard(5m));

        var drawPhase = new DrawPhaseState();
        drawPhase.OnEnter(_context);

        Assert.AreEqual(_p2Id, _state.TurnManager.CurrentPlayer);
    }

    [TestMethod]
    public void HandleCommand_RejectsAllCommands()
    {
        var drawPhase = new DrawPhaseState();
        var cmd = new EndTurnCommand(_p1Id);
        var result = drawPhase.HandleCommand(_context, cmd);
        Assert.IsTrue(result.TryGetFailure(out _));
    }
}

[TestClass]
public class ReactionStateTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private ReactionState _reactionState = default!;
    private Mock<IRandomNumberService> _rngMock = default!;
    private Guid _p1Id = default!;
    private Guid _p2Id = default!;

    [TestInitialize]
    public void Setup()
    {
        _rngMock = new Mock<IRandomNumberService>();
        _rngMock.Setup(r => r.GetRandomInt(It.IsAny<int>())).Returns(0);

        var host = UserFactory.Create("Host", Guid.NewGuid());
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state, _rngMock.Object);

        _p1Id = Guid.NewGuid();
        _p2Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(_p1Id, new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        _state.GamePlayers.TryAdd(_p2Id, new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add });

        _state.TurnManager.SetTurnOrder([_p1Id, _p2Id]);
        _state.ReactionTargetPlayerIds = [_p2Id];

        _reactionState = new ReactionState();
    }

    [TestMethod]
    public void HandleCommand_WrongPlayer_ReturnsError()
    {
        var cmd = new PassReactionCommand(_p1Id); // p1 is not the target
        var result = _reactionState.HandleCommand(_context, cmd);
        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void HandleCommand_ShieldNotInHand_ReturnsError()
    {
        var fakeShieldId = Guid.NewGuid();
        var cmd = new PlayReactionCommand(_p2Id, fakeShieldId);
        var result = _reactionState.HandleCommand(_context, cmd);
        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void Tick_NoReaction_TimesOutQuickly()
    {
        // Set up pending action with no blockable cards for target
        var stealCard = new StealCard();
        var playCmd = new PlayCardsCommand(_p1Id, [stealCard.Id], _p2Id);
        _state.PendingGameActionCommand = stealCard.CreateCommand(_context, playCmd, [stealCard]);
        _state.DiscardPile.Add(stealCard);
        // p2 has no shield — cannot react

        _reactionState.OnEnter(_context);
        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(6);
        var result = _reactionState.Tick(_context, DateTimeOffset.UtcNow);

        // Should auto-resolve (pass)
        Assert.IsNotNull(result.Value);
    }

    [TestMethod]
    public void Tick_CanReact_UsesFullTimeout()
    {
        var stealCard = new StealCard();
        var playCmd = new PlayCardsCommand(_p1Id, [stealCard.Id], _p2Id);
        _state.PendingGameActionCommand = stealCard.CreateCommand(_context, playCmd, [stealCard]);
        _state.DiscardPile.Add(stealCard);
        _state.GamePlayers[_p2Id].Hand.Add(new ShieldCard());

        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(6);
        _reactionState.OnEnter(_context);
        var result = _reactionState.Tick(_context, DateTimeOffset.UtcNow);

        // 6 seconds < 15 second reaction timeout, so should not time out yet
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public void HotPotatoRedirect_CantRedirectToSelf_ReturnsError()
    {
        var hpCard = new HotPotatoCard();
        _state.GamePlayers[_p2Id].Hand.Add(hpCard);
        var numCard = new NumberCard(5m);
        var playCmd = new PlayCardsCommand(_p1Id, [hpCard.Id, numCard.Id], _p2Id);
        _state.PendingGameActionCommand = new HotPotatoCommand(_context, playCmd, [hpCard, numCard]);

        var cmd = new RedirectHotPotatoCommand(_p2Id, hpCard.Id, _p2Id);
        var result = _reactionState.HandleCommand(_context, cmd);
        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void HotPotatoRedirect_NonExistentTarget_ReturnsError()
    {
        var hpCard = new HotPotatoCard();
        _state.GamePlayers[_p2Id].Hand.Add(hpCard);
        var numCard = new NumberCard(5m);
        var playCmd = new PlayCardsCommand(_p1Id, [hpCard.Id, numCard.Id], _p2Id);
        _state.PendingGameActionCommand = new HotPotatoCommand(_context, playCmd, [hpCard, numCard]);

        var cmd = new RedirectHotPotatoCommand(_p2Id, hpCard.Id, Guid.NewGuid());
        var result = _reactionState.HandleCommand(_context, cmd);
        Assert.IsTrue(result.TryGetFailure(out _));
    }
}

[TestClass]
public class PlayPhaseTimeoutTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;
    private PlayPhaseState _playPhase = default!;
    private Mock<IRandomNumberService> _rngMock = default!;
    private Guid _p1Id = default!;
    private Guid _p2Id = default!;

    [TestInitialize]
    public void Setup()
    {
        _rngMock = new Mock<IRandomNumberService>();
        _rngMock.Setup(r => r.GetRandomInt(It.IsAny<int>())).Returns(0);

        var host = UserFactory.Create("Host", Guid.NewGuid());
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state, _rngMock.Object);

        _p1Id = Guid.NewGuid();
        _p2Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(_p1Id, new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        _state.GamePlayers.TryAdd(_p2Id, new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add });

        _state.TurnManager.SetTurnOrder([_p1Id, _p2Id]);
        _playPhase = new PlayPhaseState();
        _playPhase.OnEnter(_context);
    }

    [TestMethod]
    public void Tick_Timeout_AutoPlaysNumberCard()
    {
        var numCard = new NumberCard(5m);
        _state.GamePlayers[_p1Id].Hand.Add(numCard);
        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);

        var result = _playPhase.Tick(_context, DateTimeOffset.UtcNow);

        Assert.IsInstanceOfType(result.Value, typeof(DrawPhaseState));
        Assert.AreEqual(15m, _state.GamePlayers[_p1Id].CurrentPoints);
    }

    [TestMethod]
    public void Tick_Timeout_EmptyHand_DoesNotCrash()
    {
        _state.GamePlayers[_p1Id].Hand.Clear();
        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);

        var result = _playPhase.Tick(_context, DateTimeOffset.UtcNow);

        Assert.IsInstanceOfType(result.Value, typeof(DrawPhaseState));
    }

    [TestMethod]
    public void Tick_Timeout_OnlyShields_Skips()
    {
        _state.GamePlayers[_p1Id].Hand.Add(new ShieldCard());
        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);

        var result = _playPhase.Tick(_context, DateTimeOffset.UtcNow);

        Assert.IsInstanceOfType(result.Value, typeof(DrawPhaseState));
        // Shield should still be in hand (not played)
        Assert.HasCount(1, _state.GamePlayers[_p1Id].Hand);
    }

    [TestMethod]
    public void Tick_TimersDisabled_DoesNotTimeout()
    {
        _state.UpdateSettings(s => s with { TimersEnabled = false });
        _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(5m));
        _state.StateStartTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);

        var result = _playPhase.Tick(_context, DateTimeOffset.UtcNow);

        Assert.IsNull(result.Value);
    }
}
