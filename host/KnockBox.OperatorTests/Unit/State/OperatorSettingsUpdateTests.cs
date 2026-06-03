using KnockBox.Operator.Models;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.Operator.Tests.Unit.State
{
    [TestClass]
    public class OperatorSettingsUpdateTests
    {
        private OperatorGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        }

        [TestMethod]
        public void UpdateSettings_ReplacesSettings_AndReturnsSuccess()
        {
            var result = _state.UpdateSettings(s => s with { MaxHandSize = 7, TimersEnabled = false });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(7, _state.Settings.MaxHandSize);
            Assert.IsFalse(_state.Settings.TimersEnabled);
        }

        [TestMethod]
        public void Settings_DefaultHostPlays_IsFalse()
        {
            Assert.IsFalse(new OperatorSettings().HostPlays);
            Assert.IsFalse(_state.Settings.HostPlays);
        }

        [TestMethod]
        public void UpdateSettings_HostPlaysTrue_SetsHostIsParticipant()
        {
            var result = _state.UpdateSettings(s => s with { HostPlays = true });

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(_state.Settings.HostPlays);
            Assert.IsTrue(_state.HostIsParticipant);
            // Host appears in Participants (as a synthetic entry) but never in Players.
            Assert.AreEqual(_state.Players.Length + 1, _state.Participants.Length);
        }

        [TestMethod]
        public void UpdateSettings_HostPlaysFalse_ClearsHostIsParticipant()
        {
            _state.UpdateSettings(s => s with { HostPlays = true });

            var result = _state.UpdateSettings(s => s with { HostPlays = false });

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(_state.Settings.HostPlays);
            Assert.IsFalse(_state.HostIsParticipant);
            Assert.AreEqual(_state.Players.Length, _state.Participants.Length);
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

            _state.UpdateSettings(s => s with { MaxHandSize = 6 });

            Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)), "State change notification was expected.");
        }
    }
}
