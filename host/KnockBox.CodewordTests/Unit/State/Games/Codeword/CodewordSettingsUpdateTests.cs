using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.Codeword.Tests.Unit.State.Games.Codeword
{
    [TestClass]
    public class CodewordSettingsUpdateTests
    {
        private CodewordGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new CodewordGameState(host, NullLogger<CodewordGameState>.Instance);
        }

        [TestMethod]
        public void UpdateSettings_ReplacesSettings_AndReturnsSuccess()
        {
            var result = _state.UpdateSettings(s => s with { TotalGames = 8, EnableTimers = false });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(8, _state.Settings.TotalGames);
            Assert.IsFalse(_state.Settings.EnableTimers);
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

            _state.UpdateSettings(s => s with { TotalGames = 7 });

            Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)), "State change notification was expected.");
        }
    }
}
