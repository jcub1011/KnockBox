using System.Text.Json;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tracery.Tests.Unit
{
    [TestClass]
    public class TracerySettingsTests
    {
        [TestMethod]
        public void Defaults_MatchGameDesignDocument()
        {
            var settings = new TracerySettings();

            // GDD §8 defaults (grid default set to 5x5 in commit 004bd84).
            Assert.AreEqual(5, settings.GridWidth);
            Assert.AreEqual(5, settings.GridHeight);
            Assert.AreEqual(TimeSpan.FromSeconds(90), settings.RoundTimer);
            Assert.AreEqual(3, settings.TotalRounds);
            Assert.AreEqual(4, settings.MinWordLength);
            Assert.IsTrue(settings.UniqueFindBonusEnabled);
            Assert.AreEqual(1.5, settings.UniqueFindMultiplier);
            Assert.IsTrue(settings.RareLetterBonusEnabled);
            Assert.IsFalse(settings.HostPlaysAlong);

            // Search mode defaults: off by default, with sensible list-size / bonus knobs.
            Assert.AreEqual(GameMode.Standard, settings.Mode);
            Assert.AreEqual(10, settings.SearchListSize);
            Assert.AreEqual(10, settings.SearchPlacementBonusUnit);
        }

        [TestMethod]
        public void SearchSettings_RoundTrip_ThroughWebJson()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var original = new TracerySettings { Mode = GameMode.Search, SearchListSize = 7, SearchPlacementBonusUnit = 25 };

            var restored = JsonSerializer.Deserialize<TracerySettings>(
                JsonSerializer.Serialize(original, options), options)!;

            Assert.AreEqual(GameMode.Search, restored.Mode);
            Assert.AreEqual(7, restored.SearchListSize);
            Assert.AreEqual(25, restored.SearchPlacementBonusUnit);
        }

        [TestMethod]
        public void UpdateSettings_ReturnsNewRecord_ViaExecute()
        {
            var state = NewState();
            var original = state.Settings;

            var result = state.UpdateSettings(s => s with { TotalRounds = 7 });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(7, state.Settings.TotalRounds);
            Assert.AreNotSame(original, state.Settings);
            // The record is immutable — the original instance is untouched.
            Assert.AreEqual(3, original.TotalRounds);
        }

        [TestMethod]
        public void UpdateSettings_LeavesStateUnchanged_WhenMutationThrows()
        {
            var state = NewState();
            var before = state.Settings;

            var result = state.UpdateSettings(_ => throw new InvalidOperationException("boom"));

            Assert.IsTrue(result.IsFailure);
            Assert.AreSame(before, state.Settings);
        }

        [TestMethod]
        public void Normalize_ClampsGridDimensions_ToSupportedRange()
        {
            // Over the ceiling on both axes, and under the floor.
            var tooBig = new TracerySettings { GridWidth = 20, GridHeight = 12 }.Normalize();
            Assert.AreEqual(TracerySettings.MaxGridDimension, tooBig.GridWidth);
            Assert.AreEqual(TracerySettings.MaxGridDimension, tooBig.GridHeight);

            var tooSmall = new TracerySettings { GridWidth = 1, GridHeight = 0 }.Normalize();
            Assert.AreEqual(TracerySettings.MinGridDimension, tooSmall.GridWidth);
            Assert.AreEqual(TracerySettings.MinGridDimension, tooSmall.GridHeight);

            // In range → the same instance is returned (no allocation, no change).
            var inRange = new TracerySettings { GridWidth = 5, GridHeight = 6 };
            Assert.AreSame(inRange, inRange.Normalize());
        }

        [TestMethod]
        public void UpdateSettings_EnforcesGridCap_EvenWhenMutationBypassesTheUi()
        {
            var state = NewState();

            // Simulate a restored-localStorage / deserialized value that skipped the panel's clamp.
            var result = state.UpdateSettings(s => s with { GridWidth = 99, GridHeight = 99 });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(TracerySettings.MaxGridDimension, state.Settings.GridWidth);
            Assert.AreEqual(TracerySettings.MaxGridDimension, state.Settings.GridHeight);
        }

        [TestMethod]
        public void ScoringTables_RoundTrip_ThroughWebJson()
        {
            // The room page persists settings to localStorage as Web-defaults JSON, so the
            // length-bonus array and rare-letter map (char keys) must survive a round-trip.
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var original = new TracerySettings();

            var restored = JsonSerializer.Deserialize<TracerySettings>(
                JsonSerializer.Serialize(original, options), options)!;

            CollectionAssert.AreEqual(original.LengthBonusTable, restored.LengthBonusTable);
            CollectionAssert.AreEquivalent(
                original.RareLetterBonusTable.ToArray(), restored.RareLetterBonusTable.ToArray());
            Assert.AreEqual(5, restored.RareLetterBonusTable['Q']);
        }

        private static TraceryGameState NewState()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            return new TraceryGameState(host, NullLogger<TraceryGameState>.Instance);
        }
    }
}
