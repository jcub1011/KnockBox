using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class LifecycleTests
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
        public void StartAsyncCore_FlipsPhaseToPlaying()
        {
            var start = _engine.StartAsync(_host, _state).GetAwaiter().GetResult();
            Assert.IsTrue(start.IsSuccess);
            Assert.AreEqual(DndMapperPhase.Playing, _state.Phase);
        }

        [TestMethod]
        public void StartAsyncCore_SetsActiveMapIdToFirstByListOrderIfUnset()
        {
            var c1 = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c1.TryGetSuccess(out var a));
            var c2 = _engine.CreateMapAsync(_state, _host, "B");
            Assert.IsTrue(c2.TryGetSuccess(out _));

            var start = _engine.StartAsync(_host, _state).GetAwaiter().GetResult();
            Assert.IsTrue(start.IsSuccess);
            Assert.AreEqual(a, _state.ActiveMapId);
        }

        [TestMethod]
        public void StartAsyncCore_SpawnsPlayerTokensForRegisteredPlayers()
        {
            var c = _engine.CreateMapAsync(_state, _host, "Map");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            EngineTestFactory.RegisterPlayer(_state, "P1");
            EngineTestFactory.RegisterPlayer(_state, "P2");

            var start = _engine.StartAsync(_host, _state).GetAwaiter().GetResult();
            Assert.IsTrue(start.IsSuccess);

            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.AreEqual(2, map.Tokens.Count(t => t.Type == TokenType.PlayerToken));
        }

        [TestMethod]
        public void StartAsyncCore_NoMaps_ActiveMapIdRemainsNull_NoTokensSpawned()
        {
            EngineTestFactory.RegisterPlayer(_state);
            var start = _engine.StartAsync(_host, _state).GetAwaiter().GetResult();
            Assert.IsTrue(start.IsSuccess);
            Assert.IsNull(_state.ActiveMapId);
            Assert.AreEqual(0, _state.Maps.Count);
        }

        [TestMethod]
        public void EndSessionAsync_HostCaller_DisposesState()
        {
            bool disposed = false;
            _state.OnStateDisposed += () => disposed = true;
            var end = _engine.EndSessionAsync(_state, _host);
            Assert.IsTrue(end.IsSuccess);
            Assert.IsTrue(disposed);
        }

        [TestMethod]
        public void EndSessionAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var end = _engine.EndSessionAsync(_state, player);
            Assert.IsTrue(end.IsFailure);
        }

        [TestMethod]
        public void EndSessionAsync_AlreadyDisposed_ReturnsError()
        {
            var first = _engine.EndSessionAsync(_state, _host);
            Assert.IsTrue(first.IsSuccess);

            var second = _engine.EndSessionAsync(_state, _host);
            Assert.IsTrue(second.IsFailure);
        }
    }
}
