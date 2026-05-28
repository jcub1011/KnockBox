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
            var host = UserFactory.Create("Host", "host-id");
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
