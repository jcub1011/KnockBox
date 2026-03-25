using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.DrawnToDressTests.Unit.State.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressConfigTests
    {
        // ── Drawing phase defaults ────────────────────────────────────────────

        [TestMethod]
        public void Default_DrawingTimeSec_Is180()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(180, config.DrawingTimeSec);
        }

        [TestMethod]
        public void Default_AllowSketchingDuringOutfitBuilding_IsFalse()
        {
            var config = new DrawnToDressConfig();
            Assert.IsFalse(config.AllowSketchingDuringOutfitBuilding);
        }

        // ── Clothing types defaults ───────────────────────────────────────────

        [TestMethod]
        public void Default_ClothingTypes_HasFiveEntries()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(5, config.ClothingTypes.Count);
        }

        [TestMethod]
        public void Default_ClothingTypes_ContainsExpectedIds()
        {
            var config = new DrawnToDressConfig();
            var ids = config.ClothingTypes.Select(t => t.Id).ToList();

            CollectionAssert.Contains(ids, "hat");
            CollectionAssert.Contains(ids, "top");
            CollectionAssert.Contains(ids, "bottom");
            CollectionAssert.Contains(ids, "shoes");
            CollectionAssert.Contains(ids, "accessory");
        }

        [TestMethod]
        public void Default_AccessoryClothingType_AllowsMultiple()
        {
            var config = new DrawnToDressConfig();
            var accessory = config.ClothingTypes.Single(t => t.Id == "accessory");
            Assert.IsTrue(accessory.AllowMultiple);
        }

        [TestMethod]
        public void Default_NonAccessoryClothingTypes_DoNotAllowMultiple()
        {
            var config = new DrawnToDressConfig();
            var nonAccessories = config.ClothingTypes.Where(t => t.Id != "accessory");

            foreach (var type in nonAccessories)
            {
                Assert.IsFalse(type.AllowMultiple, $"Expected AllowMultiple=false for type '{type.Id}'.");
            }
        }

        // ── Theme defaults ────────────────────────────────────────────────────

        [TestMethod]
        public void Default_ThemeSource_IsRandom()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(ThemeSource.Random, config.ThemeSource);
        }

        [TestMethod]
        public void Default_ThemeAnnouncementTimeSec_Is10()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(10, config.ThemeAnnouncementTimeSec);
        }

        // ── Outfit Building phase defaults ────────────────────────────────────

        [TestMethod]
        public void Default_OutfitBuildingTimeSec_Is90()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(90, config.OutfitBuildingTimeSec);
        }

        [TestMethod]
        public void Default_OutfitCustomizationTimeSec_Is60()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(60, config.OutfitCustomizationTimeSec);
        }

        // ── Pool / reuse defaults ─────────────────────────────────────────────

        [TestMethod]
        public void Default_AllowReuseOwnItems_IsTrue()
        {
            var config = new DrawnToDressConfig();
            Assert.IsTrue(config.AllowReuseOwnItems);
        }

        [TestMethod]
        public void Default_RequireDistinctItemsPerSlot_IsTrue()
        {
            var config = new DrawnToDressConfig();
            Assert.IsTrue(config.RequireDistinctItemsPerSlot);
        }

        // ── Voting defaults ───────────────────────────────────────────────────

        [TestMethod]
        public void Default_VotingCriteria_HasThreeEntries()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(3, config.VotingCriteria.Count);
        }

        [TestMethod]
        public void Default_VotingCriteria_ContainsExpectedIds()
        {
            var config = new DrawnToDressConfig();
            var ids = config.VotingCriteria.Select(c => c.Id).ToList();

            CollectionAssert.Contains(ids, "creativity");
            CollectionAssert.Contains(ids, "theme_match");
            CollectionAssert.Contains(ids, "overall_look");
        }

        [TestMethod]
        public void Default_VotingCriteria_AllHaveWeightOfOne()
        {
            var config = new DrawnToDressConfig();

            foreach (var criterion in config.VotingCriteria)
            {
                Assert.AreEqual(1.0, criterion.Weight, $"Expected Weight=1.0 for criterion '{criterion.Id}'.");
            }
        }

        [TestMethod]
        public void Default_VotingTimeSec_Is60()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(60, config.VotingTimeSec);
        }

        [TestMethod]
        public void Default_ShowCreatorDuringVoting_IsFalse()
        {
            var config = new DrawnToDressConfig();
            Assert.IsFalse(config.ShowCreatorDuringVoting);
        }

        // ── Tournament format defaults ────────────────────────────────────────

        [TestMethod]
        public void Default_VotingRounds_Is3()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(3, config.VotingRounds);
        }

        // ── Bonus points defaults ─────────────────────────────────────────────

        [TestMethod]
        public void Default_BonusPointsForCompleteOutfit_Is1()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(1, config.BonusPointsForCompleteOutfit);
        }

        // ── Host / connectivity defaults ──────────────────────────────────────

        [TestMethod]
        public void Default_HostDisconnectTimeoutSec_Is120()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(120, config.HostDisconnectTimeoutSec);
        }
    }
}
