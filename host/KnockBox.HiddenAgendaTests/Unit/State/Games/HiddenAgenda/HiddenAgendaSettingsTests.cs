using System.Text.Json;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.HiddenAgenda.Tests.Unit.State.Games.HiddenAgenda
{
    [TestClass]
    public class HiddenAgendaSettingsTests
    {
        // Mirrors the options BrowserStorageService uses to persist settings.
        private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

        private static HiddenAgendaSettings NonDefaultSettings() => new()
        {
            TotalRounds = 6,
            RoundSetupTimeoutMs = 12000,
            EventCardPhaseTimeoutMs = 9000,
            SpinPhaseTimeoutMs = 8000,
            MovePhaseTimeoutMs = 17000,
            DrawPhaseTimeoutMs = 16000,
            GuessPhaseTimeoutMs = 70000,
            FinalGuessTimeoutMs = 50000,
            RevealTimeoutMs = 13000,
            EnableTimers = true,
            PoolRotation = TaskPoolRotation.Fixed,
        };

        [TestMethod]
        public void RoundTrip_PreservesAllSettings()
        {
            var original = NonDefaultSettings();

            var json = JsonSerializer.Serialize(original, WebOptions);
            var restored = JsonSerializer.Deserialize<HiddenAgendaSettings>(json, WebOptions);

            // Records compare by value, so this asserts every property round-tripped.
            Assert.AreEqual(original, restored);
        }

        [TestMethod]
        public void Serialize_WritesEnumsByName_NotOrdinal()
        {
            var json = JsonSerializer.Serialize(NonDefaultSettings(), WebOptions);

            // Enum-by-name persistence guards against silent remaps if TaskPoolRotation is
            // ever reordered. The numeric ordinal must not leak into the JSON.
            StringAssert.Contains(json, "\"Fixed\"");
        }
    }
}
