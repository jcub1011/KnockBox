using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class FogVisibilityTests
    {
        private static Map Make(int w = 30, int h = 20) => new()
        {
            Id = Guid.NewGuid(),
            Grid = new GridConfig { WidthCells = w, HeightCells = h },
        };

        private static Token Tok(double x, double y, bool hidden = false) =>
            new() { Id = Guid.NewGuid(), X = x, Y = y, Hidden = hidden };

        private static MapImage Img(double x, double y, double w, double h, bool hidden = false) =>
            new() { Id = Guid.NewGuid(), X = x, Y = y, Width = w, Height = h, Hidden = hidden };

        // ── Token filter ─────────────────────────────────────────────────────

        [TestMethod]
        public void Token_OnFoggedCell_NonHost_Filtered()
        {
            var map = Make();
            map.SetFogged(3, 5, true);
            var t = Tok(3.5, 5.5);

            var result = TokenVisibilityFilter.VisibleTokensFor(new[] { t }, map, isHost: false).ToList();

            Assert.IsEmpty(result);
        }

        [TestMethod]
        public void Token_OnFoggedCell_Host_Visible()
        {
            var map = Make();
            map.SetFogged(3, 5, true);
            var t = Tok(3.5, 5.5);

            var result = TokenVisibilityFilter.VisibleTokensFor(new[] { t }, map, isHost: true).ToList();

            CollectionAssert.AreEqual(new[] { t }, result);
        }

        [TestMethod]
        public void Token_OnRevealedCell_NonHost_Visible()
        {
            var map = Make();
            map.SetFogged(0, 0, true);
            var t = Tok(5.5, 5.5);

            var result = TokenVisibilityFilter.VisibleTokensFor(new[] { t }, map, isHost: false).ToList();

            CollectionAssert.AreEqual(new[] { t }, result);
        }

        [TestMethod]
        public void Token_Hidden_NonHost_FilteredEvenWhenCellRevealed()
        {
            var map = Make();
            var t = Tok(5.5, 5.5, hidden: true);

            var result = TokenVisibilityFilter.VisibleTokensFor(new[] { t }, map, isHost: false).ToList();

            Assert.IsEmpty(result);
        }

        [TestMethod]
        public void Token_ContinuousCoords_BetweenCells_UsesFloorCell()
        {
            var map = Make();
            map.SetFogged(3, 5, true);
            // Token at (3.4, 5.6) is in cell (3, 5) → fogged → filtered.
            var onFogged = Tok(3.4, 5.6);
            // Token at (4.1, 5.5) is in cell (4, 5) → revealed → visible.
            var onRevealed = Tok(4.1, 5.5);

            var result = TokenVisibilityFilter
                .VisibleTokensFor(new[] { onFogged, onRevealed }, map, isHost: false)
                .ToList();

            CollectionAssert.AreEqual(new[] { onRevealed }, result);
        }

        // ── Image filter ─────────────────────────────────────────────────────

        [TestMethod]
        public void Image_AllCornersFogged_NonHost_Filtered()
        {
            var map = Make();
            var img = Img(2, 3, 4, 4); // covers cells (2..5, 3..6)
            map.SetFogged(2, 3, true);
            map.SetFogged(5, 3, true);
            map.SetFogged(2, 6, true);
            map.SetFogged(5, 6, true);

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: false).ToList();

            Assert.IsEmpty(result);
        }

        [TestMethod]
        public void Image_OneCornerRevealed_NonHost_Visible()
        {
            var map = Make();
            var img = Img(2, 3, 4, 4);
            map.SetFogged(2, 3, true);
            map.SetFogged(5, 3, true);
            map.SetFogged(2, 6, true);
            // (5, 6) deliberately revealed.

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: false).ToList();

            CollectionAssert.AreEqual(new[] { img }, result);
        }

        [TestMethod]
        public void Image_Hidden_NonHost_FilteredEvenWithRevealedCorners()
        {
            var map = Make();
            var img = Img(2, 3, 4, 4, hidden: true);

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: false).ToList();

            Assert.IsEmpty(result);
        }

        [TestMethod]
        public void Image_Host_AlwaysVisible_RegardlessOfFog()
        {
            var map = Make();
            var img = Img(2, 3, 4, 4);
            map.SetFogged(2, 3, true);
            map.SetFogged(5, 3, true);
            map.SetFogged(2, 6, true);
            map.SetFogged(5, 6, true);

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: true).ToList();

            CollectionAssert.AreEqual(new[] { img }, result);
        }

        // ── DisplayProjection integration ────────────────────────────────────

        [TestMethod]
        public void DisplayProjection_AppliesFogFilters()
        {
            var state = MakeState();
            var map = Make();
            // Fog a 2×2 block at (2,2)..(3,3).
            map.SetFogged(2, 2, true);
            map.SetFogged(3, 2, true);
            map.SetFogged(2, 3, true);
            map.SetFogged(3, 3, true);
            state.Maps.Add(map);
            state.SetActiveMapId(map.Id);

            var tokenOnFog = Tok(2.5, 2.5);
            var tokenAside = Tok(5.5, 5.5);
            map.Tokens.Add(tokenOnFog);
            map.Tokens.Add(tokenAside);

            // Image fully on the fogged block (corners 2,2 / 3,2 / 2,3 / 3,3).
            var imgFullyFogged = Img(2, 2, 2, 2);
            // Image overlapping only partly (corners 1,1 revealed → stays visible).
            var imgPartial = Img(1, 1, 3, 3);
            map.Images.Add(imgFullyFogged);
            map.Images.Add(imgPartial);

            var projection = DisplayProjection.Build(state);

            CollectionAssert.AreEqual(new[] { tokenAside }, projection.VisibleTokens.ToArray());
            CollectionAssert.AreEqual(new[] { imgPartial }, projection.VisibleImages.ToArray());
        }

        [TestMethod]
        public void DisplayProjection_FogPathDataPopulated()
        {
            var state = MakeState();
            var map = Make();
            map.SetFogged(0, 0, true);
            map.SetFogged(1, 0, true);
            map.SetFogged(0, 1, true);
            state.Maps.Add(map);
            state.SetActiveMapId(map.Id);

            var projection = DisplayProjection.Build(state);

            Assert.AreEqual(FogPolygonBuilder.BuildSvgPathData(map), projection.FogPathData);
            Assert.IsFalse(string.IsNullOrEmpty(projection.FogPathData));
        }

        [TestMethod]
        public void DisplayProjection_NoFog_FogPathDataEmpty()
        {
            var state = MakeState();
            var map = Make();
            state.Maps.Add(map);
            state.SetActiveMapId(map.Id);

            var projection = DisplayProjection.Build(state);

            Assert.IsTrue(string.IsNullOrEmpty(projection.FogPathData));
        }

        private static DndMapperGameState MakeState()
        {
            var host = UserFactory.Create("Host", "host-id");
            return new DndMapperGameState(host, NullLogger<DndMapperGameState>.Instance);
        }
    }
}
