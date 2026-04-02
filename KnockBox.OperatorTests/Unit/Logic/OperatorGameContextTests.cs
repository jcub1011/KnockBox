using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace KnockBox.OperatorTests.Unit.Logic;

[TestClass]
public class OperatorGameContextTests
{
    [TestMethod]
    public void CalculateNewScore_Rounding_AwayFromZero()
    {
        // 10.05 -> 10.1 (nearest tenth, midpoint away from zero)
        var (score1, _) = OperatorGameContext.CalculateNewScore(10.0m, CardOperator.Add, 0.05m);
        Assert.AreEqual(10.1m, score1);

        // 10.04 -> 10.0
        var (score2, _) = OperatorGameContext.CalculateNewScore(10.0m, CardOperator.Add, 0.04m);
        Assert.AreEqual(10.0m, score2);

        // 10.06 -> 10.1
        var (score3, _) = OperatorGameContext.CalculateNewScore(10.0m, CardOperator.Add, 0.06m);
        Assert.AreEqual(10.1m, score3);
    }

    [TestMethod]
    public void CalculateNewScore_DivideByZero_Rule()
    {
        // Rule: result is 0.0 and operator reverts to +
        var (score, op) = OperatorGameContext.CalculateNewScore(12.3m, CardOperator.Divide, 0m);
        Assert.AreEqual(0.0m, score);
        Assert.AreEqual(CardOperator.Add, op);
    }

    [TestMethod]
    public void CalculateNewScore_DivideByNonZero_Rounding()
    {
        // 10.0 / 3.0 = 3.3333... -> 3.3
        var (score, _) = OperatorGameContext.CalculateNewScore(10.0m, CardOperator.Divide, 3.0m);
        Assert.AreEqual(3.3m, score);
    }

    [TestMethod]
    public void GenerateDeck_PlayerCount_Scaling()
    {
        // 2-4 players: 1 base deck (80 cards)
        var deck2 = OperatorGameContext.GenerateDeck(2);
        Assert.AreEqual(80, deck2.Count);
        
        var deck4 = OperatorGameContext.GenerateDeck(4);
        Assert.AreEqual(80, deck4.Count);

        // 5-8 players: 2 base decks (160 cards)
        var deck5 = OperatorGameContext.GenerateDeck(5);
        Assert.AreEqual(160, deck5.Count);
        
        var deck8 = OperatorGameContext.GenerateDeck(8);
        Assert.AreEqual(160, deck8.Count);
        
        // 9+ players: 3 base decks (240 cards)
        var deck9 = OperatorGameContext.GenerateDeck(9);
        Assert.AreEqual(240, deck9.Count);
    }

    [TestMethod]
    public void GenerateDeck_BaseDeckComposition()
    {
        var deck = OperatorGameContext.GenerateDeck(4);
        
        // Numbers: 40
        Assert.AreEqual(40, deck.Count(c => c.Type == CardType.Number));
        // Operators: 20
        Assert.AreEqual(20, deck.Count(c => c.Type == CardType.Operator));
        // Actions: 20
        Assert.AreEqual(20, deck.Count(c => c.Type == CardType.Action));
        
        // Specific checks
        Assert.AreEqual(2, deck.Count(c => c.Type == CardType.Number && c.NumberValue == 0m));
        Assert.AreEqual(6, deck.Count(c => c.Type == CardType.Number && c.NumberValue == 9m));
        
        Assert.AreEqual(8, deck.Count(c => c.Type == CardType.Operator && c.OperatorValue == CardOperator.Add));
        Assert.AreEqual(2, deck.Count(c => c.Type == CardType.Operator && c.OperatorValue == CardOperator.Divide));
    }
}
