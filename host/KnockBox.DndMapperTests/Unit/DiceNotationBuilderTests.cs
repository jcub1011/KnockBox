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
        public void Build_GroupsSameSidedDice_AndUsesSingleAt()
        {
            var r = MakeRoll(
                new DieRoll(6, 4, false),
                new DieRoll(6, 3, false),
                new DieRoll(8, 6, false));
            // Single trailing "@" — the library's parseNotation only honors
            // one and treats anything after the first "@" as the global
            // forced-result list across every dice group.
            Assert.AreEqual("2d6+1d8@4,3,6", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_AdvantageIncludesDiscardedDie()
        {
            var r = MakeRoll(
                new DieRoll(20, 18, false),
                new DieRoll(20, 4, true));
            Assert.AreEqual("2d20@18,4", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_PreservesOrderAcrossDifferentSides()
        {
            var r = MakeRoll(
                new DieRoll(20, 11, false),
                new DieRoll(6, 5, false),
                new DieRoll(20, 7, false));
            // Source order is honored when sides change between adjacent
            // dice — d20 then d6 then d20 stays as three separate groups so
            // results map onto the right die.
            Assert.AreEqual("1d20+1d6+1d20@11,5,7", DiceNotationBuilder.Build(r));
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
            var r = MakeRoll(new DieRoll(100, 47, false));
            Assert.AreEqual("1d100+1d10@40,7", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_D100_50_ShowsTensFiftyUnitsZero()
        {
            var r = MakeRoll(new DieRoll(100, 50, false));
            Assert.AreEqual("1d100+1d10@50,0", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_D100_5_ShowsTensZeroUnitsFive()
        {
            var r = MakeRoll(new DieRoll(100, 5, false));
            Assert.AreEqual("1d100+1d10@0,5", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_D100_100_ShowsBothZeros()
        {
            var r = MakeRoll(new DieRoll(100, 100, false));
            Assert.AreEqual("1d100+1d10@0,0", DiceNotationBuilder.Build(r));
        }

        [TestMethod]
        public void Build_MultipleD100_InterleavesTensAndUnitsInOrder()
        {
            // Two d100 rolls -> tens, units, tens, units. Adjacent same-sided
            // dice are merged into runs ("1d100+1d10+1d100+1d10"), so the
            // results [40,7,10,2] map onto the dice in spawn order.
            var r = MakeRoll(
                new DieRoll(100, 47, false),
                new DieRoll(100, 12, false));
            Assert.AreEqual("1d100+1d10+1d100+1d10@40,7,10,2", DiceNotationBuilder.Build(r));
        }
    }
}
