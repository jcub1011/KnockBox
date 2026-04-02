using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace KnockBox.OperatorTests.Unit.Logic;

[TestClass]
public class ActionCardResolutionTests
{
    private OperatorGameState _state = default!;
    private OperatorGameContext _context = default!;

    [TestInitialize]
    public void Setup()
    {
        var host = new User("Host", "host1");
        _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        _context = new OperatorGameContext(_state);
        
        _state.GamePlayers.TryAdd("p1", new OperatorPlayerState { UserId = "p1" });
        _state.GamePlayers.TryAdd("p2", new OperatorPlayerState { UserId = "p2" });
    }

    [TestMethod]
    public void ResolveComp_ChangesSign_BasedOnScore()
    {
        _context.GamePlayers["p1"].CurrentPoints = -5m;
        _context.ResolveComp("p1");
        Assert.AreEqual(CardOperator.Add, _context.GamePlayers["p1"].ActiveOperator);

        _context.GamePlayers["p2"].CurrentPoints = 10m;
        _context.ResolveComp("p2");
        Assert.AreEqual(CardOperator.Subtract, _context.GamePlayers["p2"].ActiveOperator);
    }

    [TestMethod]
    public void ResolveMarketCrash_SetsOperatorToDivide_ForUnAuditedPlayers()
    {
        _context.GamePlayers["p1"].ActiveOperator = CardOperator.Add;
        _context.GamePlayers["p2"].ActiveOperator = CardOperator.Subtract;
        _context.GamePlayers["p2"].IsAudited = true;

        _context.ResolveMarketCrash();

        Assert.AreEqual(CardOperator.Divide, _context.GamePlayers["p1"].ActiveOperator);
        Assert.AreEqual(CardOperator.Subtract, _context.GamePlayers["p2"].ActiveOperator); // Audited, shouldn't change
    }

    [TestMethod]
    public void ResolveCookTheBooks_DividesCurrentScoreByPlayedValue()
    {
        _context.GamePlayers["p1"].CurrentPoints = 20m;
        _context.ResolveCookTheBooks("p1", 4m);
        
        Assert.AreEqual(5m, _context.GamePlayers["p1"].CurrentPoints);
    }

    [TestMethod]
    public void ResolveSteal_TakesRandomCardFromTarget()
    {
        var targetCard = new Card(CardType.Number, 5m);
        _context.GamePlayers["p2"].Hand.Add(targetCard);

        _context.ResolveSteal("p1", "p2");

        Assert.AreEqual(1, _context.GamePlayers["p1"].Hand.Count);
        Assert.AreEqual(targetCard.Id, _context.GamePlayers["p1"].Hand[0].Id);
        Assert.AreEqual(0, _context.GamePlayers["p2"].Hand.Count);
    }

    [TestMethod]
    public void ResolveHotPotato_GivesCardToTarget()
    {
        var potatoCard = new Card(CardType.Number, 9m);
        
        _context.ResolveHotPotato("p2", potatoCard);

        Assert.AreEqual(1, _context.GamePlayers["p2"].Hand.Count);
        Assert.AreEqual(potatoCard.Id, _context.GamePlayers["p2"].Hand[0].Id);
    }

    [TestMethod]
    public void ResolveFlashFlood_DrawsTwoCardsForTarget()
    {
        _state.Deck.Add(new Card(CardType.Number, 1m));
        _state.Deck.Add(new Card(CardType.Number, 2m));

        _context.ResolveFlashFlood("p2");

        Assert.AreEqual(2, _context.GamePlayers["p2"].Hand.Count);
        Assert.AreEqual(0, _state.Deck.Count);
    }

    [TestMethod]
    public void ResolveHostileTakeover_SwapsOperators()
    {
        _context.GamePlayers["p1"].ActiveOperator = CardOperator.Add;
        _context.GamePlayers["p2"].ActiveOperator = CardOperator.Multiply;

        _context.ResolveHostileTakeover("p1", "p2");

        Assert.AreEqual(CardOperator.Multiply, _context.GamePlayers["p1"].ActiveOperator);
        Assert.AreEqual(CardOperator.Add, _context.GamePlayers["p2"].ActiveOperator);
    }

    [TestMethod]
    public void ResolveAudit_LocksTargetOperator()
    {
        _context.ResolveAudit("p2");
        Assert.IsTrue(_context.GamePlayers["p2"].IsAudited);
        Assert.IsFalse(_context.GamePlayers["p1"].IsAudited);
    }
}
