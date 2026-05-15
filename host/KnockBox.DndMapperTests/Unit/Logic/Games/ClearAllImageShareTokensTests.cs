using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit.Logic.Games
{
    [TestClass]
    public class ClearAllImageShareTokensTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        private MapImage AddImage(Guid mapId)
        {
            var image = new MapImage
            {
                Id = Guid.NewGuid(),
                ContentType = "image/png",
                ShareToken = Guid.NewGuid(),
                Width = 5,
                Height = 5,
                Opacity = 1.0,
                ByteSize = 100,
            };
            Assert.IsTrue(_engine.AddImageAsync(_state, _host, mapId, image).IsSuccess);
            return image;
        }

        [TestMethod]
        public void ClearAllImageShareTokensAsync_NullsEveryToken_AcrossAllMaps()
        {
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "A").TryGetSuccess(out var mapA));
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "B").TryGetSuccess(out var mapB));
            AddImage(mapA);
            AddImage(mapA);
            AddImage(mapB);

            var result = _engine.ClearAllImageShareTokensAsync(_state, _host);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(_state.Maps.SelectMany(m => m.Images).All(i => i.ShareToken is null));
        }

        [TestMethod]
        public void ClearAllImageShareTokensAsync_NoImages_StillSucceeds()
        {
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "Empty").IsSuccess);
            var result = _engine.ClearAllImageShareTokensAsync(_state, _host);
            Assert.IsTrue(result.IsSuccess);
        }

        [TestMethod]
        public void ClearAllImageShareTokensAsync_NonHostCaller_ReturnsError()
        {
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "A").TryGetSuccess(out var mapId));
            var image = AddImage(mapId);
            var originalToken = image.ShareToken;
            var player = EngineTestFactory.RegisterPlayer(_state);

            var result = _engine.ClearAllImageShareTokensAsync(_state, player);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(originalToken, _state.Maps[0].Images.Single().ShareToken);
        }
    }
}
