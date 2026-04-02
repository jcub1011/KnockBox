using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace KnockBox.OperatorTests.Unit.Context;

[TestClass]
public class DeckGenerationTests
{
    [TestMethod]
    [DataRow(2, 80)]
    [DataRow(4, 80)]
    [DataRow(5, 160)]
    [DataRow(8, 160)]
    public void GenerateDeck_CreatesCorrectNumberOfCards_BasedOnPlayerCount(int playerCount, int expectedDeckSize)
    {
        var deck = OperatorGameContext.GenerateDeck(playerCount);
        Assert.AreEqual(expectedDeckSize, deck.Count);
    }

    [TestMethod]
    public void GenerateDeck_DistributesCardTypesCorrectly()
    {
        var deck = OperatorGameContext.GenerateDeck(4); // 1 base deck (80 cards)
        
        // Numbers: 40
        Assert.AreEqual(40, deck.Count(c => c.Type == CardType.Number));
        // Operators: 20
        Assert.AreEqual(20, deck.Count(c => c.Type == CardType.Operator));
        // Actions: 20
        Assert.AreEqual(20, deck.Count(c => c.Type == CardType.Action));
    }

    [TestMethod]
    public void GenerateDeck_SpecificCardCounts_AreCorrect()
    {
        var deck = OperatorGameContext.GenerateDeck(4);
        
        // 0s and 1s have 2 each
        Assert.AreEqual(2, deck.Count(c => c.Type == CardType.Number && c.NumberValue == 0m));
        Assert.AreEqual(2, deck.Count(c => c.Type == CardType.Number && c.NumberValue == 1m));
        
        // 9s have 6 each
        Assert.AreEqual(6, deck.Count(c => c.Type == CardType.Number && c.NumberValue == 9m));
        
        // Add/Subtract have 8 each
        Assert.AreEqual(8, deck.Count(c => c.Type == CardType.Operator && c.OperatorValue == CardOperator.Add));
        Assert.AreEqual(8, deck.Count(c => c.Type == CardType.Operator && c.OperatorValue == CardOperator.Subtract));
        
        // Multiply/Divide have 2 each
        Assert.AreEqual(2, deck.Count(c => c.Type == CardType.Operator && c.OperatorValue == CardOperator.Multiply));
        Assert.AreEqual(2, deck.Count(c => c.Type == CardType.Operator && c.OperatorValue == CardOperator.Divide));

        // Audit and HostileTakeover have 1 each
        Assert.AreEqual(1, deck.Count(c => c.Type == CardType.Action && c.ActionValue == CardAction.Audit));
        Assert.AreEqual(1, deck.Count(c => c.Type == CardType.Action && c.ActionValue == CardAction.HostileTakeover));
    }
}
