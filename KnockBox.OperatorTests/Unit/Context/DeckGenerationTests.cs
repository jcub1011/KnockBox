using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Services.Logic.RandomGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Linq;

namespace KnockBox.OperatorTests.Unit.Context;

[TestClass]
public class DeckGenerationTests
{
    private Mock<IRandomNumberService> _rngMock = default!;

    [TestInitialize]
    public void Setup()
    {
        _rngMock = new Mock<IRandomNumberService>();
    }

    [TestMethod]
    [DataRow(2, 80)]
    [DataRow(4, 80)]
    [DataRow(5, 160)]
    [DataRow(8, 160)]
    public void GenerateDeck_CreatesCorrectNumberOfCards_BasedOnPlayerCount(int playerCount, int expectedDeckSize)
    {
        var deck = OperatorGameContext.GenerateDeck(playerCount, _rngMock.Object);
        Assert.AreEqual(expectedDeckSize, deck.Count);
    }

    [TestMethod]
    public void GenerateDeck_DistributesCardTypesCorrectly()
    {
        var deck = OperatorGameContext.GenerateDeck(4, _rngMock.Object); // 1 base deck (80 cards)
        
        // Numbers: 48
        Assert.AreEqual(48, deck.Count(c => c.Type == CardType.Number));
        // Operators: 12
        Assert.AreEqual(12, deck.Count(c => c.Type == CardType.Operator));
        // Actions: 20
        Assert.AreEqual(20, deck.Count(c => c.Type == CardType.Action));
    }

    [TestMethod]
    public void GenerateDeck_SpecificCardCounts_AreCorrect()
    {
        var deck = OperatorGameContext.GenerateDeck(4, _rngMock.Object);

        // 0s and 1s have 2 each
        Assert.AreEqual(2, deck.Count(c => c is NumberCard n && n.NumberValue == 0m));
        Assert.AreEqual(2, deck.Count(c => c is NumberCard n && n.NumberValue == 1m));
        
        // 9s have 7 each
        Assert.AreEqual(7, deck.Count(c => c is NumberCard n && n.NumberValue == 9m));
        
        // Add/Subtract have 4 each
        Assert.AreEqual(4, deck.Count(c => c is OperatorCard o && o.OperatorValue == CardOperator.Add));
        Assert.AreEqual(4, deck.Count(c => c is OperatorCard o && o.OperatorValue == CardOperator.Subtract));
        
        // Multiply/Divide have 2 each
        Assert.AreEqual(2, deck.Count(c => c is OperatorCard o && o.OperatorValue == CardOperator.Multiply));
        Assert.AreEqual(2, deck.Count(c => c is OperatorCard o && o.OperatorValue == CardOperator.Divide));

        // Audit and HostileTakeover have 1 each
        Assert.AreEqual(1, deck.Count(c => c is ActionCard a && a.ActionValue == CardAction.Audit));
        Assert.AreEqual(1, deck.Count(c => c is ActionCard a && a.ActionValue == CardAction.HostileTakeover));
    }
}
