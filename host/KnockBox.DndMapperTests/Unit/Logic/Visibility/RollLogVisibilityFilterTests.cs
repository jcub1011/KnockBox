using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Logic.Visibility
{
    [TestClass]
    public class RollLogVisibilityFilterTests
    {
        private static readonly Guid _u1 = Guid.NewGuid();
        private static readonly Guid _u2 = Guid.NewGuid();
        private static readonly Guid _u3 = Guid.NewGuid();

        private static RollResult Roll(Guid rollerId) =>
            new(
                Guid.NewGuid(),
                rollerId,
                ForcedByUserId: null,
                Rolls: [],
                Total: 0,
                Mode: RollMode.Normal,
                FlatModifier: 0,
                AttributeModifier: null,
                Label: "test",
                TimestampUtc: DateTime.UtcNow,
                Formula: "1d20");

        [TestMethod]
        public void VisibleTo_HostSeesAllRolls()
        {
            var log = new[] { Roll(_u1), Roll(_u2), Roll(_u3) };
            var visible = RollLogVisibilityFilter.VisibleTo(log, _u1, viewerIsHost: true, rollsVisibleToPlayers: false).ToList();
            CollectionAssert.AreEquivalent(log, visible);
        }

        [TestMethod]
        public void VisibleTo_RollsVisibleToPlayersTrue_AllPlayersSeeAll()
        {
            var log = new[] { Roll(_u1), Roll(_u2), Roll(_u3) };
            var visible = RollLogVisibilityFilter.VisibleTo(log, _u2, viewerIsHost: false, rollsVisibleToPlayers: true).ToList();
            CollectionAssert.AreEquivalent(log, visible);
        }

        [TestMethod]
        public void VisibleTo_RollsVisibleToPlayersFalse_PlayerSeesOnlyOwn()
        {
            var mine = Roll(_u1);
            var other = Roll(_u2);
            var visible = RollLogVisibilityFilter.VisibleTo([mine, other], _u1, viewerIsHost: false, rollsVisibleToPlayers: false).ToList();
            CollectionAssert.AreEqual(new[] { mine }, visible);
        }

        [TestMethod]
        public void VisibleTo_EmptyLog_ReturnsEmpty()
        {
            var visible = RollLogVisibilityFilter.VisibleTo([], _u1, viewerIsHost: false, rollsVisibleToPlayers: false).ToList();
            Assert.IsEmpty(visible);
        }

        [TestMethod]
        public void VisibleTo_LogContainsOtherPlayersRolls_FilteredCorrectly()
        {
            var log = new[]
            {
                Roll(_u1),
                Roll(_u2),
                Roll(_u1),
                Roll(_u3),
                Roll(_u1),
            };
            var visible = RollLogVisibilityFilter.VisibleTo(log, _u1, false, false).ToList();
            Assert.HasCount(3, visible);
            Assert.IsTrue(visible.All(r => r.RollerUserId == _u1));
        }
    }
}
