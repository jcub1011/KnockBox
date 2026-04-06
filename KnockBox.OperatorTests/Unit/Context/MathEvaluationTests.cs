using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.OperatorTests.Unit.Context;

[TestClass]
public class MathEvaluationTests
{
    [TestMethod]
    [DataRow(CardOperator.Add, 5.0, 3.0, 8.0)]
    [DataRow(CardOperator.Subtract, 5.0, 3.0, 2.0)]
    [DataRow(CardOperator.Multiply, 5.0, 3.0, 15.0)]
    [DataRow(CardOperator.Divide, 6.0, 3.0, 2.0)]
    public void CalculateNewScore_BasicMath_ReturnsCorrectScore(CardOperator op, double currentScore, double value, double expectedScore)
    {
        var (newScore, newOp) = OperatorGameContext.CalculateNewScore((decimal)currentScore, op, (decimal)value);
        Assert.AreEqual((decimal)expectedScore, newScore);
        Assert.AreEqual(op, newOp);
    }

    [TestMethod]
    [DataRow(10.0, 0.05, 10.1)] // 10.05 -> 10.1 (nearest tenth, midpoint away from zero)
    [DataRow(10.0, 0.04, 10.0)] // 10.04 -> 10.0
    [DataRow(10.0, 0.06, 10.1)] // 10.06 -> 10.1
    [DataRow(-10.0, -0.05, -10.1)] // -10.05 -> -10.1
    [DataRow(-10.0, -0.04, -10.0)] // -10.04 -> -10.0
    public void CalculateNewScore_Rounding_AwayFromZero(double currentScore, double value, double expectedScore)
    {
        var (newScore, _) = OperatorGameContext.CalculateNewScore((decimal)currentScore, CardOperator.Add, (decimal)value);
        Assert.AreEqual((decimal)expectedScore, newScore);
    }

    [TestMethod]
    [DataRow(CardOperator.Divide, 10.0, 3.0, 3.3)] // 3.3333... -> 3.3
    [DataRow(CardOperator.Divide, -10.0, 3.0, -3.3)] // -3.3333... -> -3.3
    [DataRow(CardOperator.Divide, 1.25, 1.0, 1.3)] // 1.25 -> 1.3
    [DataRow(CardOperator.Divide, -1.25, 1.0, -1.3)] // -1.25 -> -1.3
    public void CalculateNewScore_RoundingAndNegatives_ReturnsCorrectScore(CardOperator op, double currentScore, double value, double expectedScore)
    {
        var (newScore, newOp) = OperatorGameContext.CalculateNewScore((decimal)currentScore, op, (decimal)value);
        Assert.AreEqual((decimal)expectedScore, newScore);
        Assert.AreEqual(op, newOp);
    }

    [TestMethod]
    public void CalculateNewScore_DivideByZero_ReturnsZeroAndAddOperator()
    {
        var (newScore, newOp) = OperatorGameContext.CalculateNewScore(12.3m, CardOperator.Divide, 0m);
        Assert.AreEqual(0.0m, newScore);
        Assert.AreEqual(CardOperator.Add, newOp);
    }
}
