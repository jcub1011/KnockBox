using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Logic.Visibility
{
    [TestClass]
    public class SheetVisibilityHelperTests
    {
        private static CharacterSheet Sheet(string? owner) =>
            new() { Id = Guid.NewGuid(), OwnerUserId = owner, CharacterName = "X" };

        [TestMethod]
        public void CanSeeNotesAndHp_OwnerTrue()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanSeeNotesAndHp(s, "u1", viewerIsHost: false));
        }

        [TestMethod]
        public void CanSeeNotesAndHp_HostTrue()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanSeeNotesAndHp(s, "u-other", viewerIsHost: true));
        }

        [TestMethod]
        public void CanSeeNotesAndHp_OtherPlayerFalse()
        {
            var s = Sheet("u1");
            Assert.IsFalse(SheetVisibilityHelper.CanSeeNotesAndHp(s, "u-other", viewerIsHost: false));
        }

        [TestMethod]
        public void CanEdit_OwnersOnly_OwnerTrue()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u1", false, SheetEditPolicy.OwnersOnly));
        }

        [TestMethod]
        public void CanEdit_OwnersOnly_OtherPlayerFalse()
        {
            var s = Sheet("u1");
            Assert.IsFalse(SheetVisibilityHelper.CanEdit(s, "u-other", false, SheetEditPolicy.OwnersOnly));
        }

        [TestMethod]
        public void CanEdit_OwnersOnly_HostTrue()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u-other", true, SheetEditPolicy.OwnersOnly));
        }

        [TestMethod]
        public void CanEdit_OwnersAndHost_BehavesLikeOwnersOnly()
        {
            // Documented identical behavior: host is always exempt regardless of policy,
            // so OwnersOnly and OwnersAndHost produce the same outcome for every (sheet, viewer, host) tuple.
            var s = Sheet("u1");
            string[] viewers = ["u1", "u-other"];
            bool[] hosts = [false, true];
            foreach (var v in viewers)
            foreach (var h in hosts)
            {
                var only = SheetVisibilityHelper.CanEdit(s, v, h, SheetEditPolicy.OwnersOnly);
                var andHost = SheetVisibilityHelper.CanEdit(s, v, h, SheetEditPolicy.OwnersAndHost);
                Assert.AreEqual(only, andHost,
                    $"Mismatch for viewer={v} host={h}: OwnersOnly={only}, OwnersAndHost={andHost}");
            }
        }

        [TestMethod]
        public void CanEdit_Anyone_AllParticipantsTrue()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u1", false, SheetEditPolicy.Anyone));
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u-other", false, SheetEditPolicy.Anyone));
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u-other", true, SheetEditPolicy.Anyone));
        }

        [TestMethod]
        public void CanSeeSheet_OwnedSheet_VisibleToEveryone()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(s, viewerIsHost: false));
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(s, viewerIsHost: true));
        }

        [TestMethod]
        public void CanSeeSheet_NpcSheet_HostOnly()
        {
            // Null owner = host-created NPC/monster sheet — players must not see it.
            var npc = Sheet(null);
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(npc, viewerIsHost: true));
            Assert.IsFalse(SheetVisibilityHelper.CanSeeSheet(npc, viewerIsHost: false));
        }
    }
}
