using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit.Logic.Games
{
    [TestClass]
    public class UpdateImageShareTokenTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private Guid _mapId;
        private MapImage _image = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "M").TryGetSuccess(out _mapId));

            _image = new MapImage
            {
                Id = Guid.NewGuid(),
                ContentType = "image/png",
                ShareToken = Guid.NewGuid(),
                Width = 5,
                Height = 5,
                Opacity = 1.0,
                ByteSize = 100,
            };
            Assert.IsTrue(_engine.AddImageAsync(_state, _host, _mapId, _image).IsSuccess);
        }

        [TestMethod]
        public void UpdateImageShareTokenAsync_HostCaller_UpdatesToken()
        {
            var newToken = Guid.NewGuid();
            var result = _engine.UpdateImageShareTokenAsync(_state, _host, _mapId, _image.Id, newToken);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(newToken, _state.Maps[0].Images.Single().ShareToken);
        }

        [TestMethod]
        public void UpdateImageShareTokenAsync_NullToken_ClearsToken()
        {
            var result = _engine.UpdateImageShareTokenAsync(_state, _host, _mapId, _image.Id, null);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(_state.Maps[0].Images.Single().ShareToken);
        }

        [TestMethod]
        public void UpdateImageShareTokenAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var originalToken = _image.ShareToken;

            var result = _engine.UpdateImageShareTokenAsync(_state, player, _mapId, _image.Id, Guid.NewGuid());

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(originalToken, _state.Maps[0].Images.Single().ShareToken);
        }

        [TestMethod]
        public void UpdateImageShareTokenAsync_UnknownImage_ReturnsError()
        {
            var result = _engine.UpdateImageShareTokenAsync(_state, _host, _mapId, Guid.NewGuid(), Guid.NewGuid());
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void UpdateImageShareTokenAsync_UnknownMap_ReturnsError()
        {
            var result = _engine.UpdateImageShareTokenAsync(_state, _host, Guid.NewGuid(), _image.Id, Guid.NewGuid());
            Assert.IsTrue(result.IsFailure);
        }
    }
}
