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
using System.Linq;

namespace KnockBox.OperatorTests.Integration.FSM;

[TestClass]
public class ActionCardTests
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
        _rngMock.Setup(r => r.GetRandomInt(It.IsAny<int>())).Returns(0);

        var host = new User("Host", "host1");
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state, _rngMock.Object);

        _state.GamePlayers.TryAdd("p1", new OperatorPlayerState { UserId = "p1", CurrentPoints = 10m, ActiveOperator = CardOperator.Add });
        _state.GamePlayers.TryAdd("p2", new OperatorPlayerState { UserId = "p2", CurrentPoints = 10m, ActiveOperator = CardOperator.Add });

        _state.TurnManager.SetTurnOrder(new List<string> { "p1", "p2" });

        _playPhase = new PlayPhaseState();
        _reactionPhase = new ReactionState();
    }

    // ── Steal ──

    [TestMethod]
    public void Steal_Passed_StealsRandomCardFromTarget()
    {
        var stealCard = new StealCard();
        var targetCard = new NumberCard(7m);
        _state.GamePlayers["p1"].Hand.Add(stealCard);
        _state.GamePlayers["p2"].Hand.Add(targetCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { stealCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand("p2");
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(1, _state.GamePlayers["p1"].Hand.Count);
        Assert.AreEqual(targetCard.Id, _state.GamePlayers["p1"].Hand[0].Id);
        Assert.AreEqual(0, _state.GamePlayers["p2"].Hand.Count);
    }

    [TestMethod]
    public void Steal_BlockedByShield_DoesNotSteal()
    {
        var stealCard = new StealCard();
        var shieldCard = new ShieldCard();
        var targetCard = new NumberCard(7m);
        _state.GamePlayers["p1"].Hand.Add(stealCard);
        _state.GamePlayers["p2"].Hand.Add(shieldCard);
        _state.GamePlayers["p2"].Hand.Add(targetCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { stealCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand("p2", shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.AreEqual(0, _state.GamePlayers["p1"].Hand.Count);
        Assert.AreEqual(1, _state.GamePlayers["p2"].Hand.Count);
        Assert.AreEqual(targetCard.Id, _state.GamePlayers["p2"].Hand[0].Id);
    }

    // ── Audit ──

    [TestMethod]
    public void Audit_Passed_LocksTargetOperator()
    {
        var auditCard = new AuditCard();
        _state.GamePlayers["p1"].Hand.Add(auditCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { auditCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand("p2");
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.IsTrue(_state.GamePlayers["p2"].IsAudited);
        Assert.AreEqual(_state.TurnCount + 2, _state.GamePlayers["p2"].AuditExpiresTurnCount);
    }

    [TestMethod]
    public void Audit_BlockedByShield_DoesNotLock()
    {
        var auditCard = new AuditCard();
        var shieldCard = new ShieldCard();
        _state.GamePlayers["p1"].Hand.Add(auditCard);
        _state.GamePlayers["p2"].Hand.Add(shieldCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { auditCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand("p2", shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.IsFalse(_state.GamePlayers["p2"].IsAudited);
    }

    // ── Hostile Takeover ──

    [TestMethod]
    public void HostileTakeover_Passed_SwapsOperators()
    {
        _state.GamePlayers["p1"].ActiveOperator = CardOperator.Add;
        _state.GamePlayers["p2"].ActiveOperator = CardOperator.Multiply;
        var htCard = new HostileTakeoverCard();
        _state.GamePlayers["p1"].Hand.Add(htCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { htCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand("p2");
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers["p1"].ActiveOperator);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers["p2"].ActiveOperator);
    }

    [TestMethod]
    public void HostileTakeover_BlockedByShield_DoesNotSwap()
    {
        _state.GamePlayers["p1"].ActiveOperator = CardOperator.Add;
        _state.GamePlayers["p2"].ActiveOperator = CardOperator.Multiply;
        var htCard = new HostileTakeoverCard();
        var shieldCard = new ShieldCard();
        _state.GamePlayers["p1"].Hand.Add(htCard);
        _state.GamePlayers["p2"].Hand.Add(shieldCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { htCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand("p2", shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.AreEqual(CardOperator.Add, _state.GamePlayers["p1"].ActiveOperator);
        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers["p2"].ActiveOperator);
    }

    // ── Hot Potato ──

    [TestMethod]
    public void HotPotato_Passed_AppliesScoreToTarget()
    {
        var hpCard = new HotPotatoCard();
        var numCard = new NumberCard(5m);
        _state.GamePlayers["p1"].Hand.Add(hpCard);
        _state.GamePlayers["p1"].Hand.Add(numCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { hpCard.Id, numCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand("p2");
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(10m, _state.GamePlayers["p1"].CurrentPoints);
        Assert.AreEqual(15m, _state.GamePlayers["p2"].CurrentPoints);
    }

    [TestMethod]
    public void HotPotato_Redirect_ChangesTarget()
    {
        _state.GamePlayers.TryAdd("p3", new OperatorPlayerState { UserId = "p3", CurrentPoints = 10m, ActiveOperator = CardOperator.Add });

        var hpCard = new HotPotatoCard();
        var numCard = new NumberCard(5m);
        _state.GamePlayers["p1"].Hand.Add(hpCard);
        _state.GamePlayers["p1"].Hand.Add(numCard);

        var hpCard2 = new HotPotatoCard();
        _state.GamePlayers["p2"].Hand.Add(hpCard2);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { hpCard.Id, numCard.Id }, "p2");
        _playPhase.HandleCommand(_context, playCmd);

        var redirectCmd = new RedirectHotPotatoCommand("p2", hpCard2.Id, "p3");
        _reactionPhase.HandleCommand(_context, redirectCmd);

        // p3 is now the target — pass to resolve
        var passCmd = new PassReactionCommand("p3");
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(10m, _state.GamePlayers["p1"].CurrentPoints);
        Assert.AreEqual(10m, _state.GamePlayers["p2"].CurrentPoints);
        Assert.AreEqual(15m, _state.GamePlayers["p3"].CurrentPoints);
    }

    // ── Flash Flood ──

    [TestMethod]
    public void FlashFlood_AffectsAllPlayers()
    {
        var floodCard = new FlashFloodCard();
        _state.GamePlayers["p1"].Hand.Add(floodCard);

        // Deck needs at least 4 cards (2 per player)
        for (int i = 0; i < 6; i++)
            _state.Deck.Add(new NumberCard(i));

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { floodCard.Id });
        _playPhase.HandleCommand(_context, playCmd);

        // Both players should have received 2 cards
        Assert.AreEqual(2, _state.GamePlayers["p1"].Hand.Count);
        Assert.AreEqual(2, _state.GamePlayers["p2"].Hand.Count);
    }

    [TestMethod]
    public void FlashFlood_DoesNotRequireTarget()
    {
        var floodCard = new FlashFloodCard();
        _state.GamePlayers["p1"].Hand.Add(floodCard);
        _state.Deck.Add(new NumberCard(1m));
        _state.Deck.Add(new NumberCard(2m));
        _state.Deck.Add(new NumberCard(3m));
        _state.Deck.Add(new NumberCard(4m));

        // Play without a target
        var playCmd = new PlayCardsCommand("p1", new List<Guid> { floodCard.Id });
        var result = _playPhase.HandleCommand(_context, playCmd);

        // Should not transition to reaction state
        Assert.IsNotInstanceOfType(result.Value, typeof(ReactionState));
    }

    // ── CookTheBooks ──

    [TestMethod]
    public void CookTheBooks_DividesOwnScore()
    {
        _state.GamePlayers["p1"].CurrentPoints = 20m;
        var cookCard = new CookTheBooksCard();
        var numCard = new NumberCard(2m);
        _state.GamePlayers["p1"].Hand.Add(cookCard);
        _state.GamePlayers["p1"].Hand.Add(numCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { cookCard.Id, numCard.Id });
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(10m, _state.GamePlayers["p1"].CurrentPoints);
    }

    // ── Comp ──

    [TestMethod]
    public void Comp_PositiveScore_SetsSubtract()
    {
        _state.GamePlayers["p1"].CurrentPoints = 15m;
        _state.GamePlayers["p1"].ActiveOperator = CardOperator.Add;
        var compCard = new CompCard();
        _state.GamePlayers["p1"].Hand.Add(compCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { compCard.Id });
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(CardOperator.Subtract, _state.GamePlayers["p1"].ActiveOperator);
    }

    [TestMethod]
    public void Comp_NegativeScore_SetsAdd()
    {
        _state.GamePlayers["p1"].CurrentPoints = -15m;
        _state.GamePlayers["p1"].ActiveOperator = CardOperator.Subtract;
        var compCard = new CompCard();
        _state.GamePlayers["p1"].Hand.Add(compCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { compCard.Id });
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(CardOperator.Add, _state.GamePlayers["p1"].ActiveOperator);
    }

    [TestMethod]
    public void Comp_AuditedPlayer_CannotPlay()
    {
        _state.GamePlayers["p1"].IsAudited = true;
        var compCard = new CompCard();
        _state.GamePlayers["p1"].Hand.Add(compCard);

        Assert.IsFalse(compCard.IsPlayable(_context, _state.GamePlayers["p1"]));
    }

    // ── Market Crash ──

    [TestMethod]
    public void MarketCrash_SetsAllPlayersToDivide()
    {
        _state.GamePlayers["p1"].ActiveOperator = CardOperator.Add;
        _state.GamePlayers["p2"].ActiveOperator = CardOperator.Subtract;
        var crashCard = new MarketCrashCard();
        _state.GamePlayers["p1"].Hand.Add(crashCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { crashCard.Id });
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(CardOperator.Divide, _state.GamePlayers["p1"].ActiveOperator);
        Assert.AreEqual(CardOperator.Divide, _state.GamePlayers["p2"].ActiveOperator);
    }

    [TestMethod]
    public void MarketCrash_DoesNotAffectAuditedPlayers()
    {
        _state.GamePlayers["p2"].IsAudited = true;
        _state.GamePlayers["p2"].ActiveOperator = CardOperator.Add;
        var crashCard = new MarketCrashCard();
        _state.GamePlayers["p1"].Hand.Add(crashCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { crashCard.Id });
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(CardOperator.Divide, _state.GamePlayers["p1"].ActiveOperator);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers["p2"].ActiveOperator);
    }

    // ── Error Cases ──

    [TestMethod]
    public void PlayCards_InvalidTarget_ReturnsError()
    {
        var stealCard = new StealCard();
        _state.GamePlayers["p1"].Hand.Add(stealCard);

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { stealCard.Id }, "nonexistent");
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void PlayCards_NotYourTurn_ReturnsError()
    {
        var card = new NumberCard(5m);
        _state.GamePlayers["p2"].Hand.Add(card);

        var playCmd = new PlayCardsCommand("p2", new List<Guid> { card.Id });
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void PlayCards_CardNotInHand_ReturnsError()
    {
        var card = new NumberCard(5m);
        // Don't add card to hand

        var playCmd = new PlayCardsCommand("p1", new List<Guid> { card.Id });
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void PlayCards_EmptyCardList_ReturnsError()
    {
        var playCmd = new PlayCardsCommand("p1", new List<Guid>());
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void EndTurn_WithoutPlaying_ReturnsError()
    {
        _state.GamePlayers["p1"].HasPlayedCardThisTurn = false;
        var endCmd = new EndTurnCommand("p1");
        var result = _playPhase.HandleCommand(_context, endCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void EndTurn_HandTooLarge_ReturnsError()
    {
        _state.GamePlayers["p1"].HasPlayedCardThisTurn = true;
        for (int i = 0; i < 6; i++)
            _state.GamePlayers["p1"].Hand.Add(new NumberCard(i));

        var endCmd = new EndTurnCommand("p1");
        var result = _playPhase.HandleCommand(_context, endCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void SkipTurn_WithPlayableCards_ReturnsError()
    {
        _state.GamePlayers["p1"].Hand.Add(new NumberCard(5m));
        var skipCmd = new SkipTurnCommand("p1");
        var result = _playPhase.HandleCommand(_context, skipCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void SkipTurn_OnlyShieldsInHand_Succeeds()
    {
        _state.GamePlayers["p1"].Hand.Add(new ShieldCard());
        var skipCmd = new SkipTurnCommand("p1");
        var result = _playPhase.HandleCommand(_context, skipCmd);

        Assert.IsInstanceOfType(result.Value, typeof(DrawPhaseState));
    }
}
