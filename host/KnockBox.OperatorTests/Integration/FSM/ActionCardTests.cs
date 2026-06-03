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
using System.Linq;

namespace KnockBox.Operator.Tests.Integration.FSM;

[TestClass]
public class ActionCardTests
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
        _reactionPhase = new ReactionState();
    }

    // ── Steal ──

    [TestMethod]
    public void Steal_Passed_StealsRandomCardFromTarget()
    {
        var stealCard = new StealCard();
        var targetCard = new NumberCard(7m);
        _state.GamePlayers[_p1Id].Hand.Add(stealCard);
        _state.GamePlayers[_p2Id].Hand.Add(targetCard);

        var playCmd = new PlayCardsCommand(_p1Id, [stealCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand(_p2Id);
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.HasCount(1, _state.GamePlayers[_p1Id].Hand);
        Assert.AreEqual(targetCard.Id, _state.GamePlayers[_p1Id].Hand[0].Id);
        Assert.IsEmpty(_state.GamePlayers[_p2Id].Hand);
    }

    [TestMethod]
    public void Steal_BlockedByShield_DoesNotSteal()
    {
        var stealCard = new StealCard();
        var shieldCard = new ShieldCard();
        var targetCard = new NumberCard(7m);
        _state.GamePlayers[_p1Id].Hand.Add(stealCard);
        _state.GamePlayers[_p2Id].Hand.Add(shieldCard);
        _state.GamePlayers[_p2Id].Hand.Add(targetCard);

        var playCmd = new PlayCardsCommand(_p1Id, [stealCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand(_p2Id, shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.IsEmpty(_state.GamePlayers[_p1Id].Hand);
        Assert.HasCount(1, _state.GamePlayers[_p2Id].Hand);
        Assert.AreEqual(targetCard.Id, _state.GamePlayers[_p2Id].Hand[0].Id);
    }

    // ── Audit ──

    [TestMethod]
    public void Audit_Passed_LocksTargetOperator()
    {
        var auditCard = new AuditCard();
        _state.GamePlayers[_p1Id].Hand.Add(auditCard);

        var playCmd = new PlayCardsCommand(_p1Id, [auditCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand(_p2Id);
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.IsTrue(_state.GamePlayers[_p2Id].IsAudited);
        Assert.AreEqual(_state.TurnCount + 2, _state.GamePlayers[_p2Id].AuditExpiresTurnCount);
    }

    [TestMethod]
    public void Audit_BlockedByShield_DoesNotLock()
    {
        var auditCard = new AuditCard();
        var shieldCard = new ShieldCard();
        _state.GamePlayers[_p1Id].Hand.Add(auditCard);
        _state.GamePlayers[_p2Id].Hand.Add(shieldCard);

        var playCmd = new PlayCardsCommand(_p1Id, [auditCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand(_p2Id, shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.IsFalse(_state.GamePlayers[_p2Id].IsAudited);
    }

    // ── Hostile Takeover ──

    [TestMethod]
    public void HostileTakeover_Passed_SwapsOperators()
    {
        _state.GamePlayers[_p1Id].ActiveOperator = CardOperator.Add;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var htCard = new HostileTakeoverCard();
        _state.GamePlayers[_p1Id].Hand.Add(htCard);

        var playCmd = new PlayCardsCommand(_p1Id, [htCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand(_p2Id);
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers[_p1Id].ActiveOperator);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p2Id].ActiveOperator);
    }

    [TestMethod]
    public void HostileTakeover_BlockedByShield_DoesNotSwap()
    {
        _state.GamePlayers[_p1Id].ActiveOperator = CardOperator.Add;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var htCard = new HostileTakeoverCard();
        var shieldCard = new ShieldCard();
        _state.GamePlayers[_p1Id].Hand.Add(htCard);
        _state.GamePlayers[_p2Id].Hand.Add(shieldCard);

        var playCmd = new PlayCardsCommand(_p1Id, [htCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand(_p2Id, shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p1Id].ActiveOperator);
        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers[_p2Id].ActiveOperator);
    }

    // ── Hot Potato ──

    [TestMethod]
    public void HotPotato_Passed_AppliesScoreToTarget()
    {
        var hpCard = new HotPotatoCard();
        var numCard = new NumberCard(5m);
        _state.GamePlayers[_p1Id].Hand.Add(hpCard);
        _state.GamePlayers[_p1Id].Hand.Add(numCard);

        var playCmd = new PlayCardsCommand(_p1Id, [hpCard.Id, numCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand(_p2Id);
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(10m, _state.GamePlayers[_p1Id].CurrentPoints);
        Assert.AreEqual(15m, _state.GamePlayers[_p2Id].CurrentPoints);
    }

    [TestMethod]
    public void HotPotato_Redirect_ChangesTarget()
    {
        var p3Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(p3Id, new OperatorPlayerState { UserId = p3Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add });

        var hpCard = new HotPotatoCard();
        var numCard = new NumberCard(5m);
        _state.GamePlayers[_p1Id].Hand.Add(hpCard);
        _state.GamePlayers[_p1Id].Hand.Add(numCard);

        var hpCard2 = new HotPotatoCard();
        _state.GamePlayers[_p2Id].Hand.Add(hpCard2);

        var playCmd = new PlayCardsCommand(_p1Id, [hpCard.Id, numCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var redirectCmd = new RedirectHotPotatoCommand(_p2Id, hpCard2.Id, p3Id);
        _reactionPhase.HandleCommand(_context, redirectCmd);

        // p3 is now the target — pass to resolve
        var passCmd = new PassReactionCommand(p3Id);
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(10m, _state.GamePlayers[_p1Id].CurrentPoints);
        Assert.AreEqual(10m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(15m, _state.GamePlayers[p3Id].CurrentPoints);
    }

    // ── Flash Flood ──

    [TestMethod]
    public void FlashFlood_AffectsAllPlayers()
    {
        var floodCard = new FlashFloodCard();
        _state.GamePlayers[_p1Id].Hand.Add(floodCard);

        // Deck needs at least 4 cards (2 per player)
        for (int i = 0; i < 6; i++)
            _state.Deck.Add(new NumberCard(i));

        var playCmd = new PlayCardsCommand(_p1Id, [floodCard.Id]);
        _playPhase.HandleCommand(_context, playCmd);

        // Transition to ReactionState for global actions
        Assert.AreEqual(OperatorGamePhase.Reaction, _state.Phase);

        // Pass for all targets (except p1)
        foreach (var targetId in _state.ReactionTargetPlayerIds.ToList())
        {
            _reactionPhase.HandleCommand(_context, new PassReactionCommand(targetId));
        }

        // Both players should have received 2 cards
        Assert.HasCount(2, _state.GamePlayers[_p1Id].Hand);
        Assert.HasCount(2, _state.GamePlayers[_p2Id].Hand);
    }

    [TestMethod]
    public void FlashFlood_DoesNotRequireTarget()
    {
        var floodCard = new FlashFloodCard();
        _state.GamePlayers[_p1Id].Hand.Add(floodCard);
        _state.Deck.Add(new NumberCard(1m));
        _state.Deck.Add(new NumberCard(2m));
        _state.Deck.Add(new NumberCard(3m));
        _state.Deck.Add(new NumberCard(4m));

        // Play without a target
        var playCmd = new PlayCardsCommand(_p1Id, [floodCard.Id]);
        var result = _playPhase.HandleCommand(_context, playCmd);

        // Flash Flood is a global action and SHOULD transition to reaction state
        Assert.IsInstanceOfType(result.Value, typeof(ReactionState));
    }

    // ── CookTheBooks ──

    [TestMethod]
    public void CookTheBooks_DividesOwnScore()
    {
        _state.GamePlayers[_p1Id].CurrentPoints = 20m;
        var cookCard = new CookTheBooksCard();
        var numCard = new NumberCard(2m);
        _state.GamePlayers[_p1Id].Hand.Add(cookCard);
        _state.GamePlayers[_p1Id].Hand.Add(numCard);

        var playCmd = new PlayCardsCommand(_p1Id, [cookCard.Id, numCard.Id]);
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(10m, _state.GamePlayers[_p1Id].CurrentPoints);
    }

    // ── Comp ──

    [TestMethod]
    public void Comp_PositiveScore_SetsSubtract()
    {
        _state.GamePlayers[_p1Id].CurrentPoints = 15m;
        _state.GamePlayers[_p1Id].ActiveOperator = CardOperator.Add;
        var compCard = new CompCard();
        _state.GamePlayers[_p1Id].Hand.Add(compCard);

        var playCmd = new PlayCardsCommand(_p1Id, [compCard.Id]);
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(CardOperator.Subtract, _state.GamePlayers[_p1Id].ActiveOperator);
    }

    [TestMethod]
    public void Comp_NegativeScore_SetsAdd()
    {
        _state.GamePlayers[_p1Id].CurrentPoints = -15m;
        _state.GamePlayers[_p1Id].ActiveOperator = CardOperator.Subtract;
        var compCard = new CompCard();
        _state.GamePlayers[_p1Id].Hand.Add(compCard);

        var playCmd = new PlayCardsCommand(_p1Id, [compCard.Id]);
        _playPhase.HandleCommand(_context, playCmd);

        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p1Id].ActiveOperator);
    }

    [TestMethod]
    public void Comp_AuditedPlayer_CannotPlay()
    {
        _state.GamePlayers[_p1Id].IsAudited = true;
        var compCard = new CompCard();
        _state.GamePlayers[_p1Id].Hand.Add(compCard);

        Assert.IsFalse(compCard.IsPlayable(_context, _state.GamePlayers[_p1Id]));
    }

    // ── Market Crash ──

    [TestMethod]
    public void MarketCrash_SetsAllPlayersToDivide()
    {
        _state.GamePlayers[_p1Id].ActiveOperator = CardOperator.Add;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Subtract;
        var crashCard = new MarketCrashCard();
        _state.GamePlayers[_p1Id].Hand.Add(crashCard);

        var playCmd = new PlayCardsCommand(_p1Id, [crashCard.Id]);
        _playPhase.HandleCommand(_context, playCmd);

        // Transition to ReactionState
        Assert.AreEqual(OperatorGamePhase.Reaction, _state.Phase);

        // Pass for all targets (except p1)
        foreach (var targetId in _state.ReactionTargetPlayerIds.ToList())
        {
            _reactionPhase.HandleCommand(_context, new PassReactionCommand(targetId));
        }

        Assert.AreEqual(CardOperator.Divide, _state.GamePlayers[_p1Id].ActiveOperator);
        Assert.AreEqual(CardOperator.Divide, _state.GamePlayers[_p2Id].ActiveOperator);
    }

    [TestMethod]
    public void MarketCrash_DoesNotAffectAuditedPlayers()
    {
        _state.GamePlayers[_p2Id].IsAudited = true;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Add;
        var crashCard = new MarketCrashCard();
        _state.GamePlayers[_p1Id].Hand.Add(crashCard);

        var playCmd = new PlayCardsCommand(_p1Id, [crashCard.Id]);
        _playPhase.HandleCommand(_context, playCmd);

        // Should resolve immediately because only target (p2) is audited
        Assert.AreNotEqual(OperatorGamePhase.Reaction, _state.Phase);

        Assert.AreEqual(CardOperator.Divide, _state.GamePlayers[_p1Id].ActiveOperator);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p2Id].ActiveOperator);
    }

    // ── Error Cases ──

    [TestMethod]
    public void PlayCards_InvalidTarget_ReturnsError()
    {
        var stealCard = new StealCard();
        _state.GamePlayers[_p1Id].Hand.Add(stealCard);

        var playCmd = new PlayCardsCommand(_p1Id, [stealCard.Id], Guid.NewGuid());
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void PlayCards_NotYourTurn_ReturnsError()
    {
        var card = new NumberCard(5m);
        _state.GamePlayers[_p2Id].Hand.Add(card);

        var playCmd = new PlayCardsCommand(_p2Id, [card.Id]);
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void PlayCards_CardNotInHand_ReturnsError()
    {
        var card = new NumberCard(5m);
        // Don't add card to hand

        var playCmd = new PlayCardsCommand(_p1Id, [card.Id]);
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void PlayCards_EmptyCardList_ReturnsError()
    {
        var playCmd = new PlayCardsCommand(_p1Id, []);
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void EndTurn_WithoutPlaying_ReturnsError()
    {
        _state.GamePlayers[_p1Id].HasPlayedCardThisTurn = false;
        var endCmd = new EndTurnCommand(_p1Id);
        var result = _playPhase.HandleCommand(_context, endCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void EndTurn_HandTooLarge_ReturnsError()
    {
        _state.GamePlayers[_p1Id].HasPlayedCardThisTurn = true;
        for (int i = 0; i < 6; i++)
            _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(i));

        var endCmd = new EndTurnCommand(_p1Id);
        var result = _playPhase.HandleCommand(_context, endCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void SkipTurn_WithPlayableCards_ReturnsError()
    {
        _state.GamePlayers[_p1Id].Hand.Add(new NumberCard(5m));
        var skipCmd = new SkipTurnCommand(_p1Id);
        var result = _playPhase.HandleCommand(_context, skipCmd);

        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void SkipTurn_OnlyShieldsInHand_Succeeds()
    {
        _state.GamePlayers[_p1Id].Hand.Add(new ShieldCard());
        var skipCmd = new SkipTurnCommand(_p1Id);
        var result = _playPhase.HandleCommand(_context, skipCmd);

        Assert.IsInstanceOfType(result.Value, typeof(DrawPhaseState));
    }

    // ── Blue Shell ──

    [TestMethod]
    public void BlueShell_SingleZeroPlayer_Pass_ResetsScore()
    {
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var blueShell = new BlueShellCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        var result = _playPhase.HandleCommand(_context, playCmd);

        Assert.IsInstanceOfType(result.Value, typeof(ReactionState));
        Assert.Contains(_p2Id, _state.ReactionTargetPlayerIds);

        var passCmd = new PassReactionCommand(_p2Id);
        _reactionPhase.HandleCommand(_context, passCmd);

        Assert.AreEqual(10m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p2Id].ActiveOperator);
    }

    [TestMethod]
    public void BlueShell_SingleZeroPlayer_Shield_ScoreStaysZero()
    {
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var blueShell = new BlueShellCard();
        var shield = new ShieldCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);
        _state.GamePlayers[_p2Id].Hand.Add(shield);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand(_p2Id, shield.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.AreEqual(0m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers[_p2Id].ActiveOperator);
    }

    [TestMethod]
    public void BlueShell_MultipleZeroPlayers_AllPass_AllReset()
    {
        var p3Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(p3Id, new OperatorPlayerState { UserId = p3Id, CurrentPoints = 0m, ActiveOperator = CardOperator.Subtract });
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var blueShell = new BlueShellCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        _playPhase.HandleCommand(_context, playCmd);

        Assert.HasCount(2, _state.ReactionTargetPlayerIds);

        // Both pass
        _reactionPhase.HandleCommand(_context, new PassReactionCommand(_p2Id));
        // After first pass, should still be in reaction (waiting for p3)
        Assert.HasCount(1, _state.PlayerReactions);

        _reactionPhase.HandleCommand(_context, new PassReactionCommand(p3Id));

        Assert.AreEqual(10m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[_p2Id].ActiveOperator);
        Assert.AreEqual(10m, _state.GamePlayers[p3Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[p3Id].ActiveOperator);
    }

    [TestMethod]
    public void BlueShell_MultipleZeroPlayers_OneBlocks_OthersReset()
    {
        var p3Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(p3Id, new OperatorPlayerState { UserId = p3Id, CurrentPoints = 0m, ActiveOperator = CardOperator.Subtract });
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var blueShell = new BlueShellCard();
        var shield = new ShieldCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);
        _state.GamePlayers[_p2Id].Hand.Add(shield);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        _playPhase.HandleCommand(_context, playCmd);

        // p2 shields, p3 passes
        _reactionPhase.HandleCommand(_context, new PlayReactionCommand(_p2Id, shield.Id));
        _reactionPhase.HandleCommand(_context, new PassReactionCommand(p3Id));

        // p2 blocked — stays at 0
        Assert.AreEqual(0m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers[_p2Id].ActiveOperator);

        // p3 didn't block — reset to 10
        Assert.AreEqual(10m, _state.GamePlayers[p3Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Add, _state.GamePlayers[p3Id].ActiveOperator);
    }

    [TestMethod]
    public void BlueShell_MultipleZeroPlayers_AllBlock_NoneReset()
    {
        var p3Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(p3Id, new OperatorPlayerState { UserId = p3Id, CurrentPoints = 0m, ActiveOperator = CardOperator.Subtract });
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var blueShell = new BlueShellCard();
        var shield1 = new ShieldCard();
        var shield2 = new ShieldCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);
        _state.GamePlayers[_p2Id].Hand.Add(shield1);
        _state.GamePlayers[p3Id].Hand.Add(shield2);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        _playPhase.HandleCommand(_context, playCmd);

        _reactionPhase.HandleCommand(_context, new PlayReactionCommand(_p2Id, shield1.Id));
        _reactionPhase.HandleCommand(_context, new PlayReactionCommand(p3Id, shield2.Id));

        Assert.AreEqual(0m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers[_p2Id].ActiveOperator);
        Assert.AreEqual(0m, _state.GamePlayers[p3Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Subtract, _state.GamePlayers[p3Id].ActiveOperator);
    }

    [TestMethod]
    public void BlueShell_MultiTarget_PlayersCanReactInAnyOrder()
    {
        var p3Id = Guid.NewGuid();
        _state.GamePlayers.TryAdd(p3Id, new OperatorPlayerState { UserId = p3Id, CurrentPoints = 0m, ActiveOperator = CardOperator.Subtract });
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        var blueShell = new BlueShellCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        _playPhase.HandleCommand(_context, playCmd);

        // p3 reacts first (before p2) — order shouldn't matter
        _reactionPhase.HandleCommand(_context, new PassReactionCommand(p3Id));
        Assert.HasCount(1, _state.PlayerReactions);

        _reactionPhase.HandleCommand(_context, new PassReactionCommand(_p2Id));

        Assert.AreEqual(10m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(10m, _state.GamePlayers[p3Id].CurrentPoints);
    }

    [TestMethod]
    public void BlueShell_DoubleReact_Rejected()
    {
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        var blueShell = new BlueShellCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        _playPhase.HandleCommand(_context, playCmd);

        _reactionPhase.HandleCommand(_context, new PassReactionCommand(_p2Id));

        // p2 tries to react again
        var result = _reactionPhase.HandleCommand(_context, new PassReactionCommand(_p2Id));
        Assert.IsTrue(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void BlueShell_NonTargetedPlayer_CannotReact()
    {
        _state.GamePlayers[_p2Id].CurrentPoints = 0m;
        _state.GamePlayers[_p1Id].CurrentPoints = 10m; // p1 not at 0
        var blueShell = new BlueShellCard();
        _state.GamePlayers[_p1Id].Hand.Add(blueShell);

        var playCmd = new PlayCardsCommand(_p1Id, [blueShell.Id], null);
        _playPhase.HandleCommand(_context, playCmd);

        // p1 is not targeted (not at 0)
        var result = _reactionPhase.HandleCommand(_context, new PassReactionCommand(_p1Id));
        Assert.IsTrue(result.TryGetFailure(out _));
    }

    // ── Surcharge ──

    [TestMethod]
    public void Surcharge_Passed_AddsDirectlyToTargetScore()
    {
        _state.GamePlayers[_p2Id].CurrentPoints = 5m;
        _state.GamePlayers[_p2Id].ActiveOperator = CardOperator.Multiply;
        var surchargeCard = new SurchargeCard();
        var numCard = new NumberCard(3m);
        _state.GamePlayers[_p1Id].Hand.Add(surchargeCard);
        _state.GamePlayers[_p1Id].Hand.Add(numCard);

        var playCmd = new PlayCardsCommand(_p1Id, [surchargeCard.Id, numCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var passCmd = new PassReactionCommand(_p2Id);
        _reactionPhase.HandleCommand(_context, passCmd);

        // Surcharge adds directly, ignoring operator
        Assert.AreEqual(8m, _state.GamePlayers[_p2Id].CurrentPoints);
        Assert.AreEqual(CardOperator.Multiply, _state.GamePlayers[_p2Id].ActiveOperator);
    }

    [TestMethod]
    public void Surcharge_BlockedByShield_DoesNotAdd()
    {
        _state.GamePlayers[_p2Id].CurrentPoints = 5m;
        var surchargeCard = new SurchargeCard();
        var numCard = new NumberCard(3m);
        var shieldCard = new ShieldCard();
        _state.GamePlayers[_p1Id].Hand.Add(surchargeCard);
        _state.GamePlayers[_p1Id].Hand.Add(numCard);
        _state.GamePlayers[_p2Id].Hand.Add(shieldCard);

        var playCmd = new PlayCardsCommand(_p1Id, [surchargeCard.Id, numCard.Id], _p2Id);
        _playPhase.HandleCommand(_context, playCmd);

        var reactCmd = new PlayReactionCommand(_p2Id, shieldCard.Id);
        _reactionPhase.HandleCommand(_context, reactCmd);

        Assert.AreEqual(5m, _state.GamePlayers[_p2Id].CurrentPoints);
    }
}
