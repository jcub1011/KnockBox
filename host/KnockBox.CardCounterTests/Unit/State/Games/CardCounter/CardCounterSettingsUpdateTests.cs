using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.CardCounter.Tests.Unit.State.Games.CardCounter
{
    [TestClass]
    public class CardCounterSettingsUpdateTests
    {
        private CardCounterGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new CardCounterGameState(host, NullLogger<CardCounterGameState>.Instance);
        }

        [TestMethod]
        public void UpdateSettings_ReplacesSettings_AndReturnsSuccess()
        {
            var result = _state.UpdateSettings(s => s with { ActiveOperatorMode = true, DeckSize = 99 });

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(_state.Settings.ActiveOperatorMode);
            Assert.AreEqual(99, _state.Settings.DeckSize);
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

            _state.UpdateSettings(s => s with { ActiveOperatorMode = true });

            Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)), "State change notification was expected.");
        }
    }
}
