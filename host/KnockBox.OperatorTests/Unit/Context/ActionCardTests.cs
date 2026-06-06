using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.RandomGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;

namespace KnockBox.Operator.Tests.Unit.Context;

[TestClass]
public class ActionCardTests
{
    private OperatorGameContext _context = default!;
    private OperatorGameState _state = default!;
    private Mock<IRandomNumberService> _rngMock = default!;
    private Mock<ILogger<OperatorGameState>> _loggerMock = default!;
    private Guid _p1Id = default!;
    private Guid _p2Id = default!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<OperatorGameState>>();
        var host = KnockBox.Core.Services.State.Users.UserFactory.Create("host", Guid.NewGuid());
        _state = new OperatorGameState(host, _loggerMock.Object);
        _rngMock = new Mock<IRandomNumberService>();
        _context = new OperatorGameContext(_state, _rngMock.Object);
        _p1Id = Guid.NewGuid();
        _p2Id = Guid.NewGuid();
    }

    private CardPlayContext MakePlayContext(
        OperatorPlayerState thisPlayer,
        Guid? targetPlayerId = null,
        decimal combinedNumberValue = 0m,
        List<NumberCard>? pairedNumbers = null,
        bool actionBlocked = false)
    {
        return new CardPlayContext(
            GameContext: _context,
            ThisPlayer: thisPlayer,
            TargetPlayerId: targetPlayerId,
            CombinedNumberValue: combinedNumberValue,
            PairedNumbers: pairedNumbers ?? [],
            ActionBlocked: actionBlocked
        );
    }

    // --- Resolve tests (existing) ---

    [TestMethod]
    public void ResolveSurcharge_AddsValueDirectly_IgnoringOperator()
    {
        // Arrange
        var player = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m, ActiveOperator = CardOperator.Multiply };
        _state.GamePlayers[_p1Id] = player;

        // Act
        SurchargeCard.Resolve(_context, _p1Id, 5m);

        // Assert
        Assert.AreEqual(5m, player.CurrentPoints);
        Assert.AreEqual(CardOperator.Multiply, player.ActiveOperator);
    }

    [TestMethod]
    public void ResolveBlueShell_ResetsOnlyPlayersAtZero()
    {
        // Arrange
        var p1 = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m, ActiveOperator = CardOperator.Multiply };
        var p2 = new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 5m, ActiveOperator = CardOperator.Subtract };
        _state.GamePlayers[_p1Id] = p1;
        _state.GamePlayers[_p2Id] = p2;

        // Act
        BlueShellCard.Resolve(_context);

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
        var p1 = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 5m };
        _state.GamePlayers[_p1Id] = p1;
        var blueShell = new BlueShellCard();

        // Act & Assert
        Assert.IsFalse(blueShell.IsPlayable(_context, p1));

        p1.CurrentPoints = 0m;
        Assert.IsTrue(blueShell.IsPlayable(_context, p1));
    }

    // --- Play method tests ---

    [TestMethod]
    public void CompCard_Play_ResolvesEvenWhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = player;
        var card = new CompCard();
        var ctx = MakePlayContext(player, actionBlocked: true);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsFalse(playResult.ConsumedNumbers);
        Assert.AreEqual(CardOperator.Subtract, player.ActiveOperator);
    }

    [TestMethod]
    public void CompCard_Play_DoesNotChangeOperatorAtZero()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = player;
        var card = new CompCard();
        var ctx = MakePlayContext(player);

        card.Play(ctx);

        Assert.AreEqual(CardOperator.Add, player.ActiveOperator);
    }

    [TestMethod]
    public void MarketCrashCard_Play_ResolvesEvenWhenBlocked()
    {
        var p1 = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = p1;
        var card = new MarketCrashCard();
        var ctx = MakePlayContext(p1, actionBlocked: true);

        card.Play(ctx);

        Assert.AreEqual(CardOperator.Divide, p1.ActiveOperator);
    }

    [TestMethod]
    public void SurchargeCard_Play_ConsumesNumbers_WhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m };
        var target = new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 0m };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new SurchargeCard();
        var numbers = new List<NumberCard> { new(5m) };
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, combinedNumberValue: 5m, pairedNumbers: numbers, actionBlocked: true);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.ConsumedNumbers);
        Assert.AreEqual(0m, target.CurrentPoints);
    }

    [TestMethod]
    public void SurchargeCard_Play_AppliesValue_WhenNotBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m };
        var target = new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 0m };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new SurchargeCard();
        var numbers = new List<NumberCard> { new(5m) };
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, combinedNumberValue: 5m, pairedNumbers: numbers);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.ConsumedNumbers);
        Assert.AreEqual(5m, target.CurrentPoints);
    }

    [TestMethod]
    public void CookTheBooksCard_Play_ConsumesNumbers_WhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 20m };
        _state.GamePlayers[_p1Id] = player;
        var card = new CookTheBooksCard();
        var numbers = new List<NumberCard> { new(5m) };
        var ctx = MakePlayContext(player, combinedNumberValue: 5m, pairedNumbers: numbers, actionBlocked: true);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.ConsumedNumbers);
        Assert.AreEqual(20m, player.CurrentPoints);
    }

    [TestMethod]
    public void CookTheBooksCard_Play_DividesScore_WhenNotBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 20m };
        _state.GamePlayers[_p1Id] = player;
        var card = new CookTheBooksCard();
        var numbers = new List<NumberCard> { new(5m) };
        var ctx = MakePlayContext(player, combinedNumberValue: 5m, pairedNumbers: numbers);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.ConsumedNumbers);
        Assert.AreEqual(4m, player.CurrentPoints);
    }

    [TestMethod]
    public void LiabilityTransferCard_Play_ConsumesNumbers_WhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id };
        var target = new OperatorPlayerState { UserId = _p2Id };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new LiabilityTransferCard();
        var numbers = new List<NumberCard> { new(3m) };
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, pairedNumbers: numbers, actionBlocked: true);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.ConsumedNumbers);
        Assert.IsEmpty(target.Hand);
    }

    [TestMethod]
    public void LiabilityTransferCard_Play_TransfersCards_WhenNotBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id };
        var target = new OperatorPlayerState { UserId = _p2Id };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new LiabilityTransferCard();
        var numberCard = new NumberCard(3m);
        _state.DiscardPile.Add(numberCard);
        var numbers = new List<NumberCard> { numberCard };
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, pairedNumbers: numbers);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.ConsumedNumbers);
        Assert.HasCount(1, target.Hand);
        Assert.IsEmpty(_state.DiscardPile);
    }

    [TestMethod]
    public void HotPotatoCard_Play_ConsumesNumbers_WhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id };
        var target = new OperatorPlayerState { UserId = _p2Id, CurrentPoints = 10m, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new HotPotatoCard();
        var numbers = new List<NumberCard> { new(5m) };
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, combinedNumberValue: 5m, pairedNumbers: numbers, actionBlocked: true);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.ConsumedNumbers);
        Assert.AreEqual(10m, target.CurrentPoints);
    }

    [TestMethod]
    public void StealCard_Play_DoesNothing_WhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id };
        var target = new OperatorPlayerState { UserId = _p2Id, Hand = [new NumberCard(1m)] };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new StealCard();
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, actionBlocked: true);

        card.Play(ctx);

        Assert.IsEmpty(player.Hand);
        Assert.HasCount(1, target.Hand);
    }

    [TestMethod]
    public void StealCard_Play_StealsRandomCard_WhenNotBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id };
        var target = new OperatorPlayerState { UserId = _p2Id, Hand = [new NumberCard(1m), new NumberCard(2m)] };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        _rngMock.Setup(r => r.GetRandomInt(2)).Returns(0);
        var card = new StealCard();
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id);

        card.Play(ctx);

        Assert.HasCount(1, player.Hand);
        Assert.HasCount(1, target.Hand);
        Assert.IsTrue(target.IsBeingStolenFrom);
    }

    [TestMethod]
    public void FlashFloodCard_Play_DoesNothing_WhenBlocked()
    {
        var p1 = new OperatorPlayerState { UserId = _p1Id };
        _state.GamePlayers[_p1Id] = p1;
        _state.Deck.AddRange(new Card[] { new NumberCard(1m), new NumberCard(2m), new NumberCard(3m) });
        var card = new FlashFloodCard();
        var ctx = MakePlayContext(p1, actionBlocked: true);

        card.Play(ctx);

        Assert.IsEmpty(p1.Hand);
    }

    [TestMethod]
    public void AuditCard_Play_AuditsTarget_WhenNotBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id };
        var target = new OperatorPlayerState { UserId = _p2Id };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new AuditCard();
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id);

        card.Play(ctx);

        Assert.IsTrue(target.IsAudited);
    }

    [TestMethod]
    public void AuditCard_Play_DoesNotAudit_WhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id };
        var target = new OperatorPlayerState { UserId = _p2Id };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new AuditCard();
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, actionBlocked: true);

        card.Play(ctx);

        Assert.IsFalse(target.IsAudited);
    }

    [TestMethod]
    public void HostileTakeoverCard_Play_SwapsOperators_WhenNotBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Add };
        var target = new OperatorPlayerState { UserId = _p2Id, ActiveOperator = CardOperator.Multiply };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new HostileTakeoverCard();
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id);

        card.Play(ctx);

        Assert.AreEqual(CardOperator.Multiply, player.ActiveOperator);
        Assert.AreEqual(CardOperator.Add, target.ActiveOperator);
    }

    [TestMethod]
    public void HostileTakeoverCard_Play_DoesNotSwap_WhenBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Add };
        var target = new OperatorPlayerState { UserId = _p2Id, ActiveOperator = CardOperator.Multiply };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new HostileTakeoverCard();
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, actionBlocked: true);

        card.Play(ctx);

        Assert.AreEqual(CardOperator.Add, player.ActiveOperator);
        Assert.AreEqual(CardOperator.Multiply, target.ActiveOperator);
    }

    [TestMethod]
    public void BlueShellCard_Play_DoesNothing_WhenBlocked()
    {
        var p1 = new OperatorPlayerState { UserId = _p1Id, CurrentPoints = 0m };
        _state.GamePlayers[_p1Id] = p1;
        var card = new BlueShellCard();
        var ctx = MakePlayContext(p1, actionBlocked: true);

        card.Play(ctx);

        Assert.AreEqual(0m, p1.CurrentPoints);
    }

    // --- OperatorCard Play tests ---

    [TestMethod]
    public void OperatorCard_Play_SetsOperator_WhenNotBlocked()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Add };
        var target = new OperatorPlayerState { UserId = _p2Id, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new OperatorCard(CardOperator.Multiply);
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsFalse(playResult.Toggled);
        Assert.AreEqual(CardOperator.Multiply, target.ActiveOperator);
    }

    [TestMethod]
    public void OperatorCard_Play_TogglesOperator_WhenSameOperator()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Add };
        var target = new OperatorPlayerState { UserId = _p2Id, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new OperatorCard(CardOperator.Add);
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id);

        var result = card.Play(ctx);

        Assert.IsTrue(result.TryGetSuccess(out var playResult));
        Assert.IsTrue(playResult.Toggled);
        Assert.AreEqual(_p2Id, playResult.OperatorTargetId);
        Assert.AreEqual(CardOperator.Subtract, target.ActiveOperator);
    }

    [TestMethod]
    public void OperatorCard_Play_Blocked_WhenTargetingOther()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Add };
        var target = new OperatorPlayerState { UserId = _p2Id, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = player;
        _state.GamePlayers[_p2Id] = target;
        var card = new OperatorCard(CardOperator.Multiply);
        var ctx = MakePlayContext(player, targetPlayerId: _p2Id, actionBlocked: true);

        card.Play(ctx);

        Assert.AreEqual(CardOperator.Add, target.ActiveOperator);
    }

    [TestMethod]
    public void OperatorCard_Play_ResolvesOnSelf_WhenBlockedButTargetingSelf()
    {
        var player = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Add };
        _state.GamePlayers[_p1Id] = player;
        var card = new OperatorCard(CardOperator.Multiply);
        var ctx = MakePlayContext(player, targetPlayerId: _p1Id, actionBlocked: true);

        card.Play(ctx);

        Assert.AreEqual(CardOperator.Multiply, player.ActiveOperator);
    }

    [TestMethod]
    public void OperatorCard_Play_ResetsDivideUses_WhenChangingFromDivide()
    {
        var target = new OperatorPlayerState { UserId = _p1Id, ActiveOperator = CardOperator.Divide, DivideUses = 3 };
        _state.GamePlayers[_p1Id] = target;
        var card = new OperatorCard(CardOperator.Add);
        var ctx = MakePlayContext(target, targetPlayerId: _p1Id);

        card.Play(ctx);

        Assert.AreEqual(0, target.DivideUses);
        Assert.AreEqual(CardOperator.Add, target.ActiveOperator);
    }
}
