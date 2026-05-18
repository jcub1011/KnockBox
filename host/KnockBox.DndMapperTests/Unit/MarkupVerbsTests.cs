using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class MarkupVerbsTests
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
        public void Update_HostCaller_SetsMarkup()
        {
            var mapId = SeedMap();
            var r = _engine.UpdateMapMarkupAsync(_state, _host, mapId,
                "<path d=\"M0 0 L1 1\" stroke=\"#000\" stroke-width=\"2\" fill=\"none\" />");
            Assert.IsTrue(r.IsSuccess);
            Assert.IsNotNull(_state.Maps[0].MarkupSvg);
        }

        [TestMethod]
        public void Update_NonHost_Rejected()
        {
            var mapId = SeedMap();
            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.UpdateMapMarkupAsync(_state, player, mapId, "<path d=\"M0 0\" />");
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void Update_EmptyString_ClearsMarkup()
        {
            var mapId = SeedMap();
            _engine.UpdateMapMarkupAsync(_state, _host, mapId, "<path d=\"M0 0\" />");
            Assert.IsNotNull(_state.Maps[0].MarkupSvg);

            _engine.UpdateMapMarkupAsync(_state, _host, mapId, "");
            Assert.IsNull(_state.Maps[0].MarkupSvg);
        }

        [TestMethod]
        public void Update_SanitisesDisallowedContent()
        {
            var mapId = SeedMap();
            // Script element is not allowlisted — sanitiser must strip it.
            _engine.UpdateMapMarkupAsync(_state, _host, mapId,
                "<path d=\"M0 0\" stroke=\"#000\" /><script>alert(1)</script>");
            var svg = _state.Maps[0].MarkupSvg ?? string.Empty;
            Assert.IsFalse(svg.Contains("script", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Clear_RemovesMarkup()
        {
            var mapId = SeedMap();
            _engine.UpdateMapMarkupAsync(_state, _host, mapId, "<path d=\"M0 0\" />");
            var r = _engine.ClearMapMarkupAsync(_state, _host, mapId);
            Assert.IsTrue(r.IsSuccess);
            Assert.IsNull(_state.Maps[0].MarkupSvg);
        }

        [TestMethod]
        public void Update_OnlyAffectsTargetMap()
        {
            var a = SeedMap();
            var b = SeedMap();
            _engine.UpdateMapMarkupAsync(_state, _host, a, "<path d=\"M0 0\" />");
            Assert.IsNotNull(_state.Maps[0].MarkupSvg);
            Assert.IsNull(_state.Maps[1].MarkupSvg);
        }

        [TestMethod]
        public void Update_UnknownMapId_Rejected()
        {
            var r = _engine.UpdateMapMarkupAsync(_state, _host, Guid.NewGuid(), "<path d=\"M0 0\" />");
            Assert.IsTrue(r.IsFailure);
        }
    }
}
