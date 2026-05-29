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

            // GDD §8 defaults.
            Assert.AreEqual(4, settings.GridWidth);
            Assert.AreEqual(4, settings.GridHeight);
            Assert.AreEqual(TimeSpan.FromSeconds(90), settings.RoundTimer);
            Assert.AreEqual(3, settings.TotalRounds);
            Assert.AreEqual(4, settings.MinWordLength);
            Assert.IsTrue(settings.UniqueFindBonusEnabled);
            Assert.AreEqual(1.5, settings.UniqueFindMultiplier);
            Assert.IsTrue(settings.RareLetterBonusEnabled);
            Assert.IsFalse(settings.HostPlaysAlong);
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
            var host = UserFactory.Create("Host", Guid.NewGuid().ToString());
            return new TraceryGameState(host, NullLogger<TraceryGameState>.Instance);
        }
    }
}
