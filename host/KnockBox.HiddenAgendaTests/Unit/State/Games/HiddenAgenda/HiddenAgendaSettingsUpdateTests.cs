using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.HiddenAgenda.Tests.Unit.State.Games.HiddenAgenda
{
    [TestClass]
    public class HiddenAgendaSettingsUpdateTests
    {
        private HiddenAgendaGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new HiddenAgendaGameState(host, NullLogger<HiddenAgendaGameState>.Instance);
        }

        [TestMethod]
        public void UpdateSettings_ReplacesSettings_AndReturnsSuccess()
        {
            var result = _state.UpdateSettings(s => s with { TotalRounds = 6, EnableTimers = true });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(6, _state.Settings.TotalRounds);
            Assert.IsTrue(_state.Settings.EnableTimers);
        }

        [TestMethod]
        public void UpdateSettings_FiresStateChangedNotification()
        {
            // Notification fires outside the Execute lock and may be dispatched
            // asynchronously, so signal + bounded wait rather than asserting synchronously.
            using var signal = new ManualResetEventSlim(false);
            using var sub = _state.StateChangedEventManager.Subscribe(() =>
            {
                signal.Set();
                return ValueTask.CompletedTask;
            });

            _state.UpdateSettings(s => s with { TotalRounds = 5 });

            Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)), "State change notification was expected.");
        }
    }
}
