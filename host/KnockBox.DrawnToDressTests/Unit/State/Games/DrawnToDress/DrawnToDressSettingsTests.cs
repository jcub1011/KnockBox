using System.Text.Json;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Tests.Unit.State.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressSettingsTests
    {
        // ── Drawing phase defaults ────────────────────────────────────────────

        [TestMethod]
        public void Default_DrawingTimeSec_Is180()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(180, config.DrawingTimeSec);
        }

        [TestMethod]
        public void Default_AllowSketchingDuringOutfitBuilding_IsFalse()
        {
            var config = new DrawnToDressSettings();
            Assert.IsFalse(config.AllowSketchingDuringOutfitBuilding);
        }

        // ── Clothing types defaults ───────────────────────────────────────────

        [TestMethod]
        public void Default_ClothingTypes_HasFourEntries()
        {
            var config = new DrawnToDressSettings();
            Assert.HasCount(4, config.ClothingTypes);
        }

        [TestMethod]
        public void Default_ClothingTypes_ContainsExpectedIds()
        {
            var config = new DrawnToDressSettings();
            var ids = config.ClothingTypes.Select(t => t.Id).ToList();

            CollectionAssert.Contains(ids, ClothingType.Hat);
            CollectionAssert.Contains(ids, ClothingType.Top);
            CollectionAssert.Contains(ids, ClothingType.Bottom);
            CollectionAssert.Contains(ids, ClothingType.Shoes);
        }

        [TestMethod]
        public void Default_ClothingTypes_DoNotAllowMultiple()
        {
            var config = new DrawnToDressSettings();

            foreach (var type in config.ClothingTypes)
            {
                Assert.IsFalse(type.AllowMultiple, $"Expected AllowMultiple=false for type '{type.Id}'.");
            }
        }

        // ── Theme defaults ────────────────────────────────────────────────────

        [TestMethod]
        public void Default_ThemeSource_IsRandom()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(ThemeSource.Random, config.ThemeSource);
        }

        [TestMethod]
        public void Default_ThemeAnnouncementTimeSec_Is6()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(6, config.ThemeAnnouncementTimeSec);
        }

        // ── Outfit Building phase defaults ────────────────────────────────────

        [TestMethod]
        public void Default_OutfitBuildingTimeSec_Is90()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(90, config.OutfitBuildingTimeSec);
        }

        [TestMethod]
        public void Default_OutfitCustomizationTimeSec_Is75()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(75, config.OutfitCustomizationTimeSec);
        }

        // ── Pool / reuse defaults ─────────────────────────────────────────────

        [TestMethod]
        public void Default_AllowReuseOwnItems_IsTrue()
        {
            var config = new DrawnToDressSettings();
            Assert.IsTrue(config.AllowReuseOwnItems);
        }

        [TestMethod]
        public void Default_RequireDistinctItemsPerSlot_IsTrue()
        {
            var config = new DrawnToDressSettings();
            Assert.IsTrue(config.RequireDistinctItemsPerSlot);
        }

        // ── Outfit 2 defaults ─────────────────────────────────────────────────

        [TestMethod]
        public void Default_CanReuseOutfit1Items_IsFalse()
        {
            var config = new DrawnToDressSettings();
            Assert.IsFalse(config.CanReuseOutfit1Items);
        }

        [TestMethod]
        public void Default_Outfit2DistinctnessThreshold_Is3()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(3, config.Outfit2DistinctnessThreshold);
        }

        // ── Voting defaults ───────────────────────────────────────────────────

        [TestMethod]
        public void Default_VotingCriteria_HasThreeEntries()
        {
            var config = new DrawnToDressSettings();
            Assert.HasCount(3, config.VotingCriteria);
        }

        [TestMethod]
        public void Default_VotingCriteria_ContainsExpectedIds()
        {
            var config = new DrawnToDressSettings();
            var ids = config.VotingCriteria.Select(c => c.Id).ToList();

            CollectionAssert.Contains(ids, "creativity");
            CollectionAssert.Contains(ids, "theme_match");
            CollectionAssert.Contains(ids, "overall_look");
        }

        [TestMethod]
        public void Default_VotingCriteria_AllHaveWeightOfOne()
        {
            var config = new DrawnToDressSettings();

            foreach (var criterion in config.VotingCriteria)
            {
                Assert.AreEqual(1.0, criterion.Weight, $"Expected Weight=1.0 for criterion '{criterion.Id}'.");
            }
        }

        [TestMethod]
        public void Default_VotingTimeSec_Is60()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(60, config.VotingTimeSec);
        }

        [TestMethod]
        public void Default_ShowCreatorDuringVoting_IsFalse()
        {
            var config = new DrawnToDressSettings();
            Assert.IsFalse(config.ShowCreatorDuringVoting);
        }

        // ── Tournament format defaults ────────────────────────────────────────

        [TestMethod]
        public void Default_VotingRounds_Is0_Auto()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(0, config.VotingRounds);
        }

        // ── Bonus points defaults ─────────────────────────────────────────────

        [TestMethod]
        public void Default_BonusPointsForCompleteOutfit_Is1()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(1, config.BonusPointsForCompleteOutfit);
        }

        [TestMethod]
        public void Default_RoundLeaderBonusPoints_Is3()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(3, config.RoundLeaderBonusPoints);
        }

        [TestMethod]
        public void Default_TournamentWinnerBonusPoints_Is10()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(10, config.TournamentWinnerBonusPoints);
        }

        // ── Host / connectivity defaults ──────────────────────────────────────

        [TestMethod]
        public void Default_HostDisconnectTimeoutSec_Is120()
        {
            var config = new DrawnToDressSettings();
            Assert.AreEqual(120, config.HostDisconnectTimeoutSec);
        }

        // ── RecommendedMinimumPlayers ────────────────────────────────────────

        [TestMethod]
        public void RecommendedMinimumPlayers_Is3()
        {
            int actual = DrawnToDressSettings.RecommendedMinimumPlayers;
            Assert.AreEqual(3, actual);
        }

        // ── Normalize: clamps numeric values to minimums ──────────────────────

        [TestMethod]
        public void Normalize_DrawingTimeSec_BelowMinimum_ClampsTo30()
        {
            var config = new DrawnToDressSettings { DrawingTimeSec = 5 };
            config = config.Normalize();
            Assert.AreEqual(30, config.DrawingTimeSec);
        }

        [TestMethod]
        public void Normalize_DrawingTimeSec_AtOrAboveMinimum_Unchanged()
        {
            var config = new DrawnToDressSettings { DrawingTimeSec = 180 };
            config = config.Normalize();
            Assert.AreEqual(180, config.DrawingTimeSec);
        }

        [TestMethod]
        public void Normalize_ThemeAnnouncementTimeSec_BelowMinimum_ClampsTo5()
        {
            var config = new DrawnToDressSettings { ThemeAnnouncementTimeSec = 1 };
            config = config.Normalize();
            Assert.AreEqual(5, config.ThemeAnnouncementTimeSec);
        }

        [TestMethod]
        public void Normalize_OutfitBuildingTimeSec_BelowMinimum_ClampsTo30()
        {
            var config = new DrawnToDressSettings { OutfitBuildingTimeSec = 10 };
            config = config.Normalize();
            Assert.AreEqual(30, config.OutfitBuildingTimeSec);
        }

        [TestMethod]
        public void Normalize_OutfitCustomizationTimeSec_BelowMinimum_ClampsTo15()
        {
            var config = new DrawnToDressSettings { OutfitCustomizationTimeSec = 5 };
            config = config.Normalize();
            Assert.AreEqual(15, config.OutfitCustomizationTimeSec);
        }

        [TestMethod]
        public void Normalize_VotingTimeSec_BelowMinimum_ClampsTo15()
        {
            var config = new DrawnToDressSettings { VotingTimeSec = 5 };
            config = config.Normalize();
            Assert.AreEqual(15, config.VotingTimeSec);
        }

        [TestMethod]
        public void Normalize_VotingRounds_BelowMinimum_ClampsTo0()
        {
            var config = new DrawnToDressSettings { VotingRounds = -1 };
            config = config.Normalize();
            Assert.AreEqual(0, config.VotingRounds);
        }

        [TestMethod]
        public void Normalize_BonusPointsForCompleteOutfit_Negative_ClampsTo0()
        {
            var config = new DrawnToDressSettings { BonusPointsForCompleteOutfit = -5 };
            config = config.Normalize();
            Assert.AreEqual(0, config.BonusPointsForCompleteOutfit);
        }

        [TestMethod]
        public void Normalize_RoundLeaderBonusPoints_Negative_ClampsTo0()
        {
            var config = new DrawnToDressSettings { RoundLeaderBonusPoints = -3 };
            config = config.Normalize();
            Assert.AreEqual(0, config.RoundLeaderBonusPoints);
        }

        [TestMethod]
        public void Normalize_TournamentWinnerBonusPoints_Negative_ClampsTo0()
        {
            var config = new DrawnToDressSettings { TournamentWinnerBonusPoints = -10 };
            config = config.Normalize();
            Assert.AreEqual(0, config.TournamentWinnerBonusPoints);
        }

        [TestMethod]
        public void Normalize_HostDisconnectTimeoutSec_BelowMinimum_ClampsTo30()
        {
            var config = new DrawnToDressSettings { HostDisconnectTimeoutSec = 10 };
            config = config.Normalize();
            Assert.AreEqual(30, config.HostDisconnectTimeoutSec);
        }

        [TestMethod]
        public void Normalize_EmptyClothingTypes_RestoresToOneDefaultType()
        {
            var config = new DrawnToDressSettings { ClothingTypes = [] };
            config = config.Normalize();
            Assert.HasCount(1, config.ClothingTypes);
        }

        [TestMethod]
        public void Normalize_EmptyVotingCriteria_RestoresToOneDefaultCriterion()
        {
            var config = new DrawnToDressSettings { VotingCriteria = [] };
            config = config.Normalize();
            Assert.HasCount(1, config.VotingCriteria);
        }

        [TestMethod]
        public void Normalize_VotingCriterionWithNegativeWeight_ClampsTo0()
        {
            var config = new DrawnToDressSettings
            {
                VotingCriteria =
                [
                    new() { Id = "creativity", DisplayName = "Creativity", Weight = -1.0 }
                ]
            };
            config = config.Normalize();
            Assert.AreEqual(0, config.VotingCriteria[0].Weight);
        }

        [TestMethod]
        public void Normalize_VotingCriterionWithEmptyId_IsRemoved()
        {
            var config = new DrawnToDressSettings
            {
                VotingCriteria =
                [
                    new() { Id = "", DisplayName = "Bad", Weight = 1.0 },
                    new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
                ]
            };
            config = config.Normalize();
            Assert.HasCount(1, config.VotingCriteria);
            Assert.AreEqual("creativity", config.VotingCriteria[0].Id);
        }

        [TestMethod]
        public void Normalize_DefaultConfig_IsIdempotent()
        {
            var config = new DrawnToDressSettings();
            config = config.Normalize();

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

        // ── JSON persistence (localStorage uses JsonSerializerDefaults.Web) ───────

        // Mirrors the options BrowserStorageService uses to persist settings.
        private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

        [TestMethod]
        public void RoundTrip_PreservesScalarAndEnumSettings()
        {
            var original = new DrawnToDressSettings
            {
                ShowMannequin = false,
                EnableTimer = false,
                DrawingTimeSec = 240,
                ThemeSource = ThemeSource.HostPick,
                ThemeAnnouncement = ThemeAnnouncement.AfterDrawing,
                VoteVisibility = VoteVisibilityMode.PercentagesOnly,
                VotingRounds = 4,
                NumOutfitRounds = 2,
                MannequinDimensions = new MannequinSize(1280, 1024),
            };

            var json = JsonSerializer.Serialize(original, WebOptions);
            var restored = JsonSerializer.Deserialize<DrawnToDressSettings>(json, WebOptions)!;

            Assert.AreEqual(original.ShowMannequin, restored.ShowMannequin);
            Assert.AreEqual(original.EnableTimer, restored.EnableTimer);
            Assert.AreEqual(original.DrawingTimeSec, restored.DrawingTimeSec);
            Assert.AreEqual(original.ThemeSource, restored.ThemeSource);
            Assert.AreEqual(original.ThemeAnnouncement, restored.ThemeAnnouncement);
            Assert.AreEqual(original.VoteVisibility, restored.VoteVisibility);
            Assert.AreEqual(original.VotingRounds, restored.VotingRounds);
            Assert.AreEqual(original.NumOutfitRounds, restored.NumOutfitRounds);
            // ValueTuple backing fields don't survive Web-options JSON; the record struct does.
            Assert.AreEqual(original.MannequinDimensions, restored.MannequinDimensions);
        }

        [TestMethod]
        public void RoundTrip_PreservesDefaultMannequinDimensions()
        {
            var original = new DrawnToDressSettings();

            var json = JsonSerializer.Serialize(original, WebOptions);
            var restored = JsonSerializer.Deserialize<DrawnToDressSettings>(json, WebOptions)!;

            // Guards against the regression where MannequinDimensions persisted as (0,0).
            Assert.AreEqual(new MannequinSize(1416, 1416), restored.MannequinDimensions);
        }

        [TestMethod]
        public void RoundTrip_PreservesClothingTypeIds()
        {
            var original = new DrawnToDressSettings();

            var json = JsonSerializer.Serialize(original, WebOptions);
            var restored = JsonSerializer.Deserialize<DrawnToDressSettings>(json, WebOptions)!;

            CollectionAssert.AreEqual(
                original.ClothingTypes.Select(t => t.Id).ToList(),
                restored.ClothingTypes.Select(t => t.Id).ToList());
        }

        [TestMethod]
        public void Serialize_WritesEnumsByName_NotOrdinal()
        {
            var json = JsonSerializer.Serialize(new DrawnToDressSettings(), WebOptions);

            // Enum-by-name persistence guards against silent remaps if enum members are ever
            // reordered. Property names are camelCase under Web options; asserting
            // property:value (not just the value) keeps the ClothingTypeDefinition.Id check
            // honest — "Hat" alone would also match the unrelated DisplayName.
            StringAssert.Contains(json, "\"themeSource\":\"Random\"");
            StringAssert.Contains(json, "\"themeAnnouncement\":\"BeforeDrawing\"");
            StringAssert.Contains(json, "\"voteVisibility\":\"Hidden\"");
            StringAssert.Contains(json, "\"id\":\"Hat\"");      // ClothingTypeDefinition.Id by name
        }
    }
}
