using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class DisplayProjectionTests
    {
        private DndMapperGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            var host = UserFactory.Create("HostUser", "host-id");
            _state = new DndMapperGameState(host, NullLogger<DndMapperGameState>.Instance);
        }

        [TestMethod]
        public void Build_NoActiveMap_ReturnsEmpty()
        {
            var projection = DisplayProjection.Build(_state);

            Assert.IsNull(projection.ActiveMap);
            Assert.IsEmpty(projection.VisibleImages);
            Assert.IsEmpty(projection.VisibleTokens);
            Assert.IsEmpty(projection.VisibleRollLog);
            Assert.IsNull(projection.MarkupSvg);
        }

        [TestMethod]
        public void Build_FiltersHiddenTokens()
        {
            var map = AddMap();
            var visible = new Token { Id = Guid.NewGuid(), Name = "V", MapId = map.Id, Hidden = false };
            var hidden = new Token { Id = Guid.NewGuid(), Name = "H", MapId = map.Id, Hidden = true };
            map.Tokens.Add(visible);
            map.Tokens.Add(hidden);

            var projection = DisplayProjection.Build(_state);

            CollectionAssert.AreEqual(new[] { visible }, projection.VisibleTokens.ToArray());
        }

        [TestMethod]
        public void Build_FiltersHiddenImages()
        {
            var map = AddMap();
            var visible = new MapImage { Id = Guid.NewGuid(), LayerOrder = 0, Hidden = false };
            var hidden = new MapImage { Id = Guid.NewGuid(), LayerOrder = 1, Hidden = true };
            map.Images.Add(visible);
            map.Images.Add(hidden);

            var projection = DisplayProjection.Build(_state);

            CollectionAssert.AreEqual(new[] { visible }, projection.VisibleImages.ToArray());
        }

        [TestMethod]
        public void Build_OrdersImagesByLayerOrder()
        {
            var map = AddMap();
            var i2 = new MapImage { Id = Guid.NewGuid(), LayerOrder = 2 };
            var i0 = new MapImage { Id = Guid.NewGuid(), LayerOrder = 0 };
            var i1 = new MapImage { Id = Guid.NewGuid(), LayerOrder = 1 };
            map.Images.Add(i2);
            map.Images.Add(i0);
            map.Images.Add(i1);

            var projection = DisplayProjection.Build(_state);

            CollectionAssert.AreEqual(new[] { i0, i1, i2 }, projection.VisibleImages.ToArray());
        }

        [TestMethod]
        public void Build_RollLogHiddenWhenSettingFalse()
        {
            AddMap();
            _state.Settings.RollsVisibleToPlayers = false;
            for (var i = 0; i < 5; i++)
                _state.RollLog.Add(MakeRoll($"r{i}"));

            var projection = DisplayProjection.Build(_state);

            Assert.IsEmpty(projection.VisibleRollLog);
        }

        [TestMethod]
        public void Build_RollLogVisibleWhenSettingTrue_CapsAtTenAndReverses()
        {
            AddMap();
            _state.Settings.RollsVisibleToPlayers = true;
            var rolls = new List<RollResult>();
            for (var i = 0; i < 20; i++)
            {
                var r = MakeRoll($"r{i}");
                rolls.Add(r);
                _state.RollLog.Add(r);
            }

            var projection = DisplayProjection.Build(_state);

            // Last 10 in reverse order: rolls[19], rolls[18], ..., rolls[10].
            var expected = rolls.Skip(10).Reverse().ToArray();
            CollectionAssert.AreEqual(expected, projection.VisibleRollLog.ToArray());
        }

        private Map AddMap()
        {
            var map = new Map
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                CreatedUtc = DateTime.UtcNow,
                ListOrder = 0,
            };
            _state.Maps.Add(map);
            _state.SetActiveMapId(map.Id);
            return map;
        }

        private static RollResult MakeRoll(string label) => new(
            Guid.NewGuid(),
            "u1",
            ForcedByUserId: null,
            Rolls: [],
            Total: 0,
            Mode: RollMode.Normal,
            FlatModifier: 0,
            AttributeModifier: null,
            Label: label,
            TimestampUtc: DateTime.UtcNow,
            Formula: "1d20");
    }
}
