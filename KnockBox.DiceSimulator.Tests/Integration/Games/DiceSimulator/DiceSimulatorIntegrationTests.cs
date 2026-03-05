using KnockBox.Services.Logic.Games.DiceSimulator;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DiceSimulator;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Linq;
using System.Threading.Tasks;

namespace KnockBoxTests.Integration.Games.DiceSimulator
{
    [TestClass]
    public class DiceSimulatorIntegrationTests
    {
        [TestMethod]
        public async Task DiceSimulator_FullFlow_WorksCorrectly()
        {
            // Arrange
            var randomSvc = new RandomNumberService();
            var engineLogger = Mock.Of<ILogger<DiceSimulatorGameEngine>>();
            var stateLogger = Mock.Of<ILogger<DiceSimulatorGameState>>();
            var engine = new DiceSimulatorGameEngine(randomSvc, engineLogger, stateLogger);
            var host = new User("Host", "host");
            var player1 = new User("P1", "p1");

            // Act: Create State
            var stateResult = await engine.CreateStateAsync(host);
            Assert.IsTrue((bool)stateResult.IsSuccess);
            var state = (DiceSimulatorGameState)stateResult.Value!;

            // State is joinable
            Assert.IsTrue(state.IsJoinable);

            // Start game
            var startResult = await engine.StartAsync(host, state);
            Assert.IsTrue((bool)startResult.IsSuccess);
            Assert.IsFalse(state.IsJoinable);

            // Roll
            var action1 = new DiceRollAction
            {
                DiceCount = 3,
                DiceType = DiceType.D6,
                Mode = RollMode.Normal,
                Modifier = 2
            };
            
            var rollResult = engine.RollDice(player1, state, action1);
            Assert.IsTrue((bool)rollResult.IsSuccess);

            // Check State
            Assert.AreEqual(1, state.RollHistory.Count);
            
            var rollEntry = state.RollHistory.Last();
            Assert.AreEqual("p1", rollEntry.PlayerId); // Correct assertion using ID or Name
            Assert.AreEqual("P1", rollEntry.PlayerName);
            Assert.AreEqual(3, rollEntry.DiceCount);
            Assert.AreEqual(DiceType.D6, rollEntry.DiceType);
            
            // Expected Result range: 3*1 + 2 = 5 to 3*6 + 2 = 20
            Assert.IsTrue(rollEntry.Result >= 5 && rollEntry.Result <= 20);

            var p1Stats = state.PlayerStats["p1"];
            Assert.AreEqual(1, p1Stats.TotalRolls);
            Assert.AreEqual(3, p1Stats.RollCountByDie[DiceType.D6]);
            Assert.AreEqual(rollEntry.Result, p1Stats.HighestResult); 
            Assert.AreEqual(rollEntry.Result, p1Stats.CumulativeTotal);

            // Clear history
            var clearRes = engine.ClearHistory(host, state);
            Assert.IsTrue((bool)clearRes.IsSuccess);
            Assert.AreEqual(0, state.RollHistory.Count);
            Assert.AreEqual(0, state.PlayerStats.Count);
        }
        
        [TestMethod]
        public async Task DiceSimulator_AdvantageDisadvantageFlow_WorksCorrectly()
        {
            var randomSvc = new RandomNumberService();
            var engineLogger = Mock.Of<ILogger<DiceSimulatorGameEngine>>();
            var stateLogger = Mock.Of<ILogger<DiceSimulatorGameState>>();
            var engine = new DiceSimulatorGameEngine(randomSvc, engineLogger, stateLogger);
            var host = new User("Host", "host");

            var stateResult = await engine.CreateStateAsync(host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            
            // Roll Advantage
            var advAction = new DiceRollAction
            {
                DiceCount = 1,
                DiceType = DiceType.D20,
                Mode = RollMode.Advantage
            };
            
            var roll1 = engine.RollDice(host, state, advAction);
            Assert.IsTrue((bool)roll1.IsSuccess);

            var entry1 = state.RollHistory.Last();
            Assert.IsNotNull(entry1.AltRolls);
            int rawTotal1 = entry1.RawRolls.Sum();
            int altTotal1 = entry1.AltRolls.Sum();
            // Should keep max
            Assert.AreEqual(System.Math.Max(rawTotal1, altTotal1), entry1.Result);

            // Roll Disadvantage
            var disAction = new DiceRollAction
            {
                DiceCount = 2, // Try with multi dice
                DiceType = DiceType.D10,
                Mode = RollMode.Disadvantage
            };

            var roll2 = engine.RollDice(host, state, disAction);
            Assert.IsTrue((bool)roll2.IsSuccess);
            
            var entry2 = state.RollHistory.Last();
            Assert.IsNotNull(entry2.AltRolls);
            int rawTotal2 = entry2.RawRolls.Sum();
            int altTotal2 = entry2.AltRolls.Sum();
            // Should keep min
            Assert.AreEqual(System.Math.Min(rawTotal2, altTotal2), entry2.Result);
        }
    }
}
