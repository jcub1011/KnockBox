using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class TurnOrderSorterTests
    {
        private static CombatantEntry Make(string name, int? roll, Guid? owner = null)
            => new() { Id = Guid.NewGuid(), Name = name, InitiativeRoll = roll, OwnerUserId = owner };

        [TestMethod]
        public void Sort_DescendingByRoll()
        {
            var sorted = TurnOrderSorter.Sort([Make("A", 5), Make("B", 18), Make("C", 12)]);
            CollectionAssert.AreEqual(new[] { "B", "C", "A" }, sorted.Select(e => e.Name).ToArray());
        }

        [TestMethod]
        public void Sort_TiePlayerBeforeNpc()
        {
            var sorted = TurnOrderSorter.Sort([
                Make("Npc", 10, owner: null),
                Make("Player", 10, owner: Guid.NewGuid()),
            ]);
            Assert.AreEqual("Player", sorted[0].Name);
            Assert.AreEqual("Npc", sorted[1].Name);
        }

        [TestMethod]
        public void Sort_TieAlphabeticalWithinGroup()
        {
            var sorted = TurnOrderSorter.Sort([
                Make("Zelda", 10, Guid.NewGuid()),
                Make("Alice", 10, Guid.NewGuid()),
                Make("Mira", 10, Guid.NewGuid()),
            ]);
            CollectionAssert.AreEqual(new[] { "Alice", "Mira", "Zelda" }, sorted.Select(e => e.Name).ToArray());
        }

        [TestMethod]
        public void Insert_BeforeLowerRoll()
        {
            var ordered = TurnOrderSorter.Sort([Make("A", 18), Make("B", 5)]);
            var newcomer = Make("C", 12);
            int idx = TurnOrderSorter.FindInsertionIndex(ordered, newcomer);
            Assert.AreEqual(1, idx);
        }
    }
}
