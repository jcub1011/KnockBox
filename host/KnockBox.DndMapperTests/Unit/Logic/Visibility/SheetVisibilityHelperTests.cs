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
        public void CanEdit_HostOnly_OwnerFalse()
        {
            // Under HostOnly the owner cannot edit their own sheet — only the host can.
            var s = Sheet("u1");
            Assert.IsFalse(SheetVisibilityHelper.CanEdit(s, "u1", false, SheetEditPolicy.HostOnly));
        }

        [TestMethod]
        public void CanEdit_HostOnly_OtherPlayerFalse()
        {
            var s = Sheet("u1");
            Assert.IsFalse(SheetVisibilityHelper.CanEdit(s, "u-other", false, SheetEditPolicy.HostOnly));
        }

        [TestMethod]
        public void CanEdit_HostOnly_HostTrue()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u-other", true, SheetEditPolicy.HostOnly));
        }

        [TestMethod]
        public void CanEdit_OwnersAndHost_OwnerOrHost()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u1", false, SheetEditPolicy.OwnersAndHost));
            Assert.IsTrue(SheetVisibilityHelper.CanEdit(s, "u-other", true, SheetEditPolicy.OwnersAndHost));
            Assert.IsFalse(SheetVisibilityHelper.CanEdit(s, "u-other", false, SheetEditPolicy.OwnersAndHost));
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
        public void CanSeeSheet_OwnSheet_AlwaysVisibleToOwner()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(s, "u1", viewerIsHost: false, playersCanSeeOtherSheets: false));
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(s, "u1", viewerIsHost: false, playersCanSeeOtherSheets: true));
        }

        [TestMethod]
        public void CanSeeSheet_OtherPlayerSheet_GatedByToggle()
        {
            var s = Sheet("u1");
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(s, "u-other", viewerIsHost: false, playersCanSeeOtherSheets: true));
            Assert.IsFalse(SheetVisibilityHelper.CanSeeSheet(s, "u-other", viewerIsHost: false, playersCanSeeOtherSheets: false));
        }

        [TestMethod]
        public void CanSeeSheet_Host_AlwaysSeesEverything()
        {
            var s = Sheet("u1");
            var npc = Sheet(null);
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(s, "host", viewerIsHost: true, playersCanSeeOtherSheets: false));
            Assert.IsTrue(SheetVisibilityHelper.CanSeeSheet(npc, "host", viewerIsHost: true, playersCanSeeOtherSheets: false));
        }

        [TestMethod]
        public void CanSeeSheet_NpcSheet_NeverVisibleToPlayers()
        {
            var npc = Sheet(null);
            Assert.IsFalse(SheetVisibilityHelper.CanSeeSheet(npc, "u1", viewerIsHost: false, playersCanSeeOtherSheets: true));
            Assert.IsFalse(SheetVisibilityHelper.CanSeeSheet(npc, "u1", viewerIsHost: false, playersCanSeeOtherSheets: false));
        }
    }
}
