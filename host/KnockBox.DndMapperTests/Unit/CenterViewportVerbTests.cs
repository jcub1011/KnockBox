using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class CenterViewportVerbTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        private Guid SeedMap()
        {
            var r = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(r.TryGetSuccess(out var id));
            return id;
        }

        [TestMethod]
        public void Host_RecordsPendingRequest()
        {
            var mapId = SeedMap();
            var r = _engine.RequestCenterViewportAsync(_state, _host, mapId, 4, 7);
            Assert.IsTrue(r.IsSuccess);
            Assert.IsNotNull(_state.PendingCenterRequest);
            Assert.AreEqual(4, _state.PendingCenterRequest!.X);
            Assert.AreEqual(7, _state.PendingCenterRequest.Y);
        }

        [TestMethod]
        public void NonHost_Rejected()
        {
            var mapId = SeedMap();
            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.RequestCenterViewportAsync(_state, player, mapId, 0, 0);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void SuccessiveIdenticalCalls_ProduceDistinctNonces()
        {
            var mapId = SeedMap();
            _engine.RequestCenterViewportAsync(_state, _host, mapId, 4, 7);
            var firstNonce = _state.PendingCenterRequest!.Nonce;

            _engine.RequestCenterViewportAsync(_state, _host, mapId, 4, 7);
            var secondNonce = _state.PendingCenterRequest!.Nonce;

            Assert.AreNotEqual(firstNonce, secondNonce);
        }

        [TestMethod]
        public void UnknownMap_Rejected()
        {
            var r = _engine.RequestCenterViewportAsync(_state, _host, Guid.NewGuid(), 0, 0);
            Assert.IsTrue(r.IsFailure);
        }
    }
}
