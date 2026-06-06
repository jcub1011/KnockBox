using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.DrawnToDress.Tests.Unit.State.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressSettingsUpdateTests
    {
        private DrawnToDressGameState _state = default!;

        [TestInitialize]
        public async Task Setup()
        {
            var rng = new Mock<IRandomNumberService>();
            var engine = new DrawnToDressGameEngine(
                NullLogger<DrawnToDressGameEngine>.Instance,
                NullLogger<DrawnToDressGameState>.Instance,
                rng.Object);
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var result = await engine.CreateStateAsync(host);
            _state = (DrawnToDressGameState)result.Value!;
        }

        [TestMethod]
        public void UpdateSettings_ReplacesSettings_AndReturnsSuccess()
        {
            // The state-level mutator does not Normalize (that is the lobby's job), so the
            // supplied values are reflected verbatim.
            var result = _state.UpdateSettings(s => s with { EnableTimer = false, DrawingTimeSec = 240 });

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(_state.Settings.EnableTimer);
            Assert.AreEqual(240, _state.Settings.DrawingTimeSec);
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

            _state.UpdateSettings(s => s with { EnableTimer = false });

            Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)), "State change notification was expected.");
        }
    }
}
