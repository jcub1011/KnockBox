using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class DiceNotationBuilderTests
    {
        private static RollResult MakeRoll(params DieRoll[] dice) => new(
            Guid.NewGuid(), "u", null, dice, 0, RollMode.Normal, 0, null, "", DateTime.UtcNow, "");

        [TestMethod]
        public void Build_SingleD20()
        {
            var r = MakeRoll(new DieRoll(20, 15, false));
            Assert.AreEqual("1d20@15", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_GroupsSameSidedDice()
        {
            var r = MakeRoll(
                new DieRoll(6, 4, false),
                new DieRoll(6, 3, false),
                new DieRoll(8, 6, false));
            Assert.AreEqual("2d6@4,3+1d8@6", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_AdvantageIncludesDiscardedDie()
        {
            // Engine rolls 2d20 for advantage, keeps the higher. Discarded flag is
            // metadata — the visual roll still needs to show BOTH dice.
            var r = MakeRoll(
                new DieRoll(20, 18, false),
                new DieRoll(20, 4, true));
            Assert.AreEqual("2d20@18,4", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_PreservesOrderAcrossGroups()
        {
            var r = MakeRoll(
                new DieRoll(20, 11, false),
                new DieRoll(6, 5, false),
                new DieRoll(20, 7, false));
            // d20 group appears first (its first occurrence), then d6.
            Assert.AreEqual("2d20@11,7+1d6@5", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_EmptyRolls_ReturnsEmpty()
        {
            var r = MakeRoll();
            Assert.AreEqual(string.Empty, DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_D100_47_ExpandsToPercentilePair()
        {
            // dice-box-threejs renders d100 as a tens-only die; pair it with a
            // d10 units die so any 1–100 value can be shown.
            var r = MakeRoll(new DieRoll(100, 47, false));
            Assert.AreEqual("1d100@40+1d10@7", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_D100_50_ShowsTensFiftyUnitsZero()
        {
            var r = MakeRoll(new DieRoll(100, 50, false));
            Assert.AreEqual("1d100@50+1d10@0", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_D100_5_ShowsTensZeroUnitsFive()
        {
            var r = MakeRoll(new DieRoll(100, 5, false));
            Assert.AreEqual("1d100@0+1d10@5", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_D100_100_ShowsBothZeros()
        {
            // "00" + "0" is the conventional percentile read for 100.
            var r = MakeRoll(new DieRoll(100, 100, false));
            Assert.AreEqual("1d100@0+1d10@0", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_MultipleD100_GroupsTensAndUnitsTogether()
        {
            var r = MakeRoll(
                new DieRoll(100, 47, false),
                new DieRoll(100, 12, false));
            Assert.AreEqual("2d100@40,10+2d10@7,2", DiceNotationBuilder.Build(r));
        }
    }
}
