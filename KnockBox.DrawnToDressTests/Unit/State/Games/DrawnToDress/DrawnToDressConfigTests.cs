using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Tests.Unit.State.Games.DrawnToDress
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
            Assert.HasCount(4, config.ClothingTypes);
        }

        [TestMethod]
        public void Default_ClothingTypes_ContainsExpectedIds()
        {
            var config = new DrawnToDressConfig();
            var ids = config.ClothingTypes.Select(t => t.Id).ToList();

            CollectionAssert.Contains(ids, ClothingType.Hat);
            CollectionAssert.Contains(ids, ClothingType.Top);
            CollectionAssert.Contains(ids, ClothingType.Bottom);
            CollectionAssert.Contains(ids, ClothingType.Shoes);
        }

        [TestMethod]
        public void Default_ClothingTypes_DoNotAllowMultiple()
        {
            var config = new DrawnToDressConfig();

            foreach (var type in config.ClothingTypes)
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
        public void Default_ThemeAnnouncementTimeSec_Is6()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(6, config.ThemeAnnouncementTimeSec);
        }

        // ── Outfit Building phase defaults ────────────────────────────────────

        [TestMethod]
        public void Default_OutfitBuildingTimeSec_Is90()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(90, config.OutfitBuildingTimeSec);
        }

        [TestMethod]
        public void Default_OutfitCustomizationTimeSec_Is75()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(75, config.OutfitCustomizationTimeSec);
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

        // ── Outfit 2 defaults ─────────────────────────────────────────────────

        [TestMethod]
        public void Default_CanReuseOutfit1Items_IsFalse()
        {
            var config = new DrawnToDressConfig();
            Assert.IsFalse(config.CanReuseOutfit1Items);
        }

        [TestMethod]
        public void Default_Outfit2DistinctnessThreshold_Is3()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(3, config.Outfit2DistinctnessThreshold);
        }

        // ── Voting defaults ───────────────────────────────────────────────────

        [TestMethod]
        public void Default_VotingCriteria_HasThreeEntries()
        {
            var config = new DrawnToDressConfig();
            Assert.HasCount(3, config.VotingCriteria);
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
        public void Default_VotingRounds_Is0_Auto()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(0, config.VotingRounds);
        }

        // ── Bonus points defaults ─────────────────────────────────────────────

        [TestMethod]
        public void Default_BonusPointsForCompleteOutfit_Is1()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(1, config.BonusPointsForCompleteOutfit);
        }

        [TestMethod]
        public void Default_RoundLeaderBonusPoints_Is3()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(3, config.RoundLeaderBonusPoints);
        }

        [TestMethod]
        public void Default_TournamentWinnerBonusPoints_Is10()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(10, config.TournamentWinnerBonusPoints);
        }

        // ── Host / connectivity defaults ──────────────────────────────────────

        [TestMethod]
        public void Default_HostDisconnectTimeoutSec_Is120()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(120, config.HostDisconnectTimeoutSec);
        }

        // ── RecommendedMinimumPlayers ────────────────────────────────────────

        [TestMethod]
        public void RecommendedMinimumPlayers_Is3()
        {
            int actual = DrawnToDressConfig.RecommendedMinimumPlayers;
            Assert.AreEqual(3, actual);
        }

        // ── Normalize: clamps numeric values to minimums ──────────────────────

        [TestMethod]
        public void Normalize_DrawingTimeSec_BelowMinimum_ClampsTo30()
        {
            var config = new DrawnToDressConfig { DrawingTimeSec = 5 };
            config.Normalize();
            Assert.AreEqual(30, config.DrawingTimeSec);
        }

        [TestMethod]
        public void Normalize_DrawingTimeSec_AtOrAboveMinimum_Unchanged()
        {
            var config = new DrawnToDressConfig { DrawingTimeSec = 180 };
            config.Normalize();
            Assert.AreEqual(180, config.DrawingTimeSec);
        }

        [TestMethod]
        public void Normalize_ThemeAnnouncementTimeSec_BelowMinimum_ClampsTo5()
        {
            var config = new DrawnToDressConfig { ThemeAnnouncementTimeSec = 1 };
            config.Normalize();
            Assert.AreEqual(5, config.ThemeAnnouncementTimeSec);
        }

        [TestMethod]
        public void Normalize_OutfitBuildingTimeSec_BelowMinimum_ClampsTo30()
        {
            var config = new DrawnToDressConfig { OutfitBuildingTimeSec = 10 };
            config.Normalize();
            Assert.AreEqual(30, config.OutfitBuildingTimeSec);
        }

        [TestMethod]
        public void Normalize_OutfitCustomizationTimeSec_BelowMinimum_ClampsTo15()
        {
            var config = new DrawnToDressConfig { OutfitCustomizationTimeSec = 5 };
            config.Normalize();
            Assert.AreEqual(15, config.OutfitCustomizationTimeSec);
        }

        [TestMethod]
        public void Normalize_VotingTimeSec_BelowMinimum_ClampsTo15()
        {
            var config = new DrawnToDressConfig { VotingTimeSec = 5 };
            config.Normalize();
            Assert.AreEqual(15, config.VotingTimeSec);
        }

        [TestMethod]
        public void Normalize_VotingRounds_BelowMinimum_ClampsTo0()
        {
            var config = new DrawnToDressConfig { VotingRounds = -1 };
            config.Normalize();
            Assert.AreEqual(0, config.VotingRounds);
        }

        [TestMethod]
        public void Normalize_BonusPointsForCompleteOutfit_Negative_ClampsTo0()
        {
            var config = new DrawnToDressConfig { BonusPointsForCompleteOutfit = -5 };
            config.Normalize();
            Assert.AreEqual(0, config.BonusPointsForCompleteOutfit);
        }

        [TestMethod]
        public void Normalize_RoundLeaderBonusPoints_Negative_ClampsTo0()
        {
            var config = new DrawnToDressConfig { RoundLeaderBonusPoints = -3 };
            config.Normalize();
            Assert.AreEqual(0, config.RoundLeaderBonusPoints);
        }

        [TestMethod]
        public void Normalize_TournamentWinnerBonusPoints_Negative_ClampsTo0()
        {
            var config = new DrawnToDressConfig { TournamentWinnerBonusPoints = -10 };
            config.Normalize();
            Assert.AreEqual(0, config.TournamentWinnerBonusPoints);
        }

        [TestMethod]
        public void Normalize_HostDisconnectTimeoutSec_BelowMinimum_ClampsTo30()
        {
            var config = new DrawnToDressConfig { HostDisconnectTimeoutSec = 10 };
            config.Normalize();
            Assert.AreEqual(30, config.HostDisconnectTimeoutSec);
        }

        [TestMethod]
        public void Normalize_EmptyClothingTypes_RestoresToOneDefaultType()
        {
            var config = new DrawnToDressConfig { ClothingTypes = [] };
            config.Normalize();
            Assert.HasCount(1, config.ClothingTypes);
        }

        [TestMethod]
        public void Normalize_EmptyVotingCriteria_RestoresToOneDefaultCriterion()
        {
            var config = new DrawnToDressConfig { VotingCriteria = [] };
            config.Normalize();
            Assert.HasCount(1, config.VotingCriteria);
        }

        [TestMethod]
        public void Normalize_VotingCriterionWithNegativeWeight_ClampsTo0()
        {
            var config = new DrawnToDressConfig
            {
                VotingCriteria =
                [
                    new() { Id = "creativity", DisplayName = "Creativity", Weight = -1.0 }
                ]
            };
            config.Normalize();
            Assert.AreEqual(0, config.VotingCriteria[0].Weight);
        }

        [TestMethod]
        public void Normalize_VotingCriterionWithEmptyId_IsRemoved()
        {
            var config = new DrawnToDressConfig
            {
                VotingCriteria =
                [
                    new() { Id = "", DisplayName = "Bad", Weight = 1.0 },
                    new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
                ]
            };
            config.Normalize();
            Assert.HasCount(1, config.VotingCriteria);
            Assert.AreEqual("creativity", config.VotingCriteria[0].Id);
        }

        [TestMethod]
        public void Normalize_DefaultConfig_IsIdempotent()
        {
            var config = new DrawnToDressConfig();
            config.Normalize();

            Assert.AreEqual(180, config.DrawingTimeSec);
            Assert.AreEqual(6, config.ThemeAnnouncementTimeSec);
            Assert.AreEqual(90, config.OutfitBuildingTimeSec);
            Assert.AreEqual(75, config.OutfitCustomizationTimeSec);
            Assert.AreEqual(60, config.VotingTimeSec);
            Assert.AreEqual(0, config.VotingRounds);
            Assert.AreEqual(1, config.BonusPointsForCompleteOutfit);
            Assert.AreEqual(120, config.HostDisconnectTimeoutSec);
            Assert.HasCount(4, config.ClothingTypes);
            Assert.HasCount(3, config.VotingCriteria);
        }
    }
}
