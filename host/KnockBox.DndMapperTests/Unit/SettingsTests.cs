using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class SettingsTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        [TestMethod]
        public void UpdateSettingsAsync_HostCaller_ReplacesSettings()
        {
            var update = _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings
            {
                TokenMovement = TokenMovementPolicy.HostOnly,
                RollsVisibleToPlayers = false,
                PlayersCanCreateNPCs = true,
            });
            Assert.IsTrue(update.IsSuccess);
            Assert.AreEqual(TokenMovementPolicy.HostOnly, _state.Settings.TokenMovement);
            Assert.IsFalse(_state.Settings.RollsVisibleToPlayers);
            Assert.IsTrue(_state.Settings.PlayersCanCreateNPCs);
        }

        [TestMethod]
        public void UpdateSettingsAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var update = _engine.UpdateSettingsAsync(_state, player, new DndMapperSettings());
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateSettingsAsync_DuringPlayingPhase_AllowedAndBroadcasts()
        {
            // Move into Playing phase
            var c = _engine.CreateMapAsync(_state, _host, "Map");
            Assert.IsTrue(c.TryGetSuccess(out _));
            var start = _engine.StartAsync(_host, _state).GetAwaiter().GetResult();
            Assert.IsTrue(start.IsSuccess);

            // Notify() is fire-and-forget (Task.Run); use a signal + bounded wait.
            using var signal = new ManualResetEventSlim(false);
            using var sub = _state.StateChangedEventManager.Subscribe(() =>
            {
                signal.Set();
                return ValueTask.CompletedTask;
            });

            var update = _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { RollsVisibleToPlayers = false });
            Assert.IsTrue(update.IsSuccess);
            Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)), "State change notification was expected.");
        }

        [TestMethod]
        public void UpdateSettingsAsync_NullSettings_ReturnsError()
        {
            var update = _engine.UpdateSettingsAsync(_state, _host, null!);
            Assert.IsTrue(update.IsFailure);
        }
    }
}
