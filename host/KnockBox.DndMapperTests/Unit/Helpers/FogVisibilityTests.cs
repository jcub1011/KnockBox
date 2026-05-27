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

        private static Map Fog(Map map, params (int cx, int cy)[] cells)
        {
            foreach (var (cx, cy) in cells)
                map = map.WithCellFogged(cx, cy, true);
            return map;
        }

        // ── Token filter ─────────────────────────────────────────────────────

        [TestMethod]
        public void Token_OnFoggedCell_NonHost_Filtered()
        {
            var map = Fog(Make(), (3, 5));
            var t = Tok(3.5, 5.5);

            var result = TokenVisibilityFilter.VisibleTokensFor(new[] { t }, map, isHost: false).ToList();

            Assert.IsEmpty(result);
        }

        [TestMethod]
        public void Token_OnFoggedCell_Host_Visible()
        {
            var map = Fog(Make(), (3, 5));
            var t = Tok(3.5, 5.5);

            var result = TokenVisibilityFilter.VisibleTokensFor(new[] { t }, map, isHost: true).ToList();

            CollectionAssert.AreEqual(new[] { t }, result);
        }

        [TestMethod]
        public void Token_OnRevealedCell_NonHost_Visible()
        {
            var map = Fog(Make(), (0, 0));
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
            var map = Fog(Make(), (3, 5));
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
        public void Image_EveryCellFogged_NonHost_Filtered()
        {
            // Fog every cell the 4×4 image covers.
            var cells = new List<(int, int)>();
            for (int y = 3; y <= 6; y++)
                for (int x = 2; x <= 5; x++)
                    cells.Add((x, y));
            var map = Fog(Make(), cells.ToArray());
            var img = Img(2, 3, 4, 4); // covers cells (2..5, 3..6)

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: false).ToList();

            Assert.IsEmpty(result);
        }

        [TestMethod]
        public void Image_OneCornerRevealed_NonHost_Visible()
        {
            // (5, 6) deliberately revealed.
            var map = Fog(Make(), (2, 3), (5, 3), (2, 6));
            var img = Img(2, 3, 4, 4);

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: false).ToList();

            CollectionAssert.AreEqual(new[] { img }, result);
        }

        [TestMethod]
        public void Image_OnlyCenterCellRevealed_NonHost_StillVisible()
        {
            // Regression: previously the filter checked only the four corners,
            // so a full-coverage image whose interior had revealed cells (but
            // whose corners were still fogged) appeared blank on the display.
            // Now any revealed cell within the AABB keeps the image visible.
            var allFogged = new List<(int, int)>();
            for (int y = 3; y <= 6; y++)
                for (int x = 2; x <= 5; x++)
                    allFogged.Add((x, y));
            var map = Fog(Make(), allFogged.ToArray());
            // Reveal a single interior cell.
            map = map.WithCellFogged(3, 4, false);
            var img = Img(2, 3, 4, 4);

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: false).ToList();

            CollectionAssert.AreEqual(new[] { img }, result);
        }

        [TestMethod]
        public void Image_FullMapSize_WithInteriorRevealed_NonHost_Visible()
        {
            // The user-reported scenario: a background image sized to exactly
            // the map dimensions. The image's bounding box corners always land
            // on the outermost cells, which are typically the last to be
            // revealed. With the old 4-corners-only check the whole image was
            // dropped from the projection (appearing blank).
            var map = Make(w: 10, h: 10);
            var allFogged = new List<(int, int)>();
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                    allFogged.Add((x, y));
            map = Fog(map, allFogged.ToArray());
            // Reveal a cluster of cells near the middle.
            map = map.WithCellFogged(4, 4, false);
            map = map.WithCellFogged(5, 5, false);

            var img = Img(0, 0, 10, 10);

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
            var map = Fog(Make(), (2, 3), (5, 3), (2, 6), (5, 6));
            var img = Img(2, 3, 4, 4);

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: true).ToList();

            CollectionAssert.AreEqual(new[] { img }, result);
        }

        [TestMethod]
        public void Image_Hidden_Host_AlsoFiltered()
        {
            // Hidden is a "do not render" flag for everyone, not a player-only
            // toggle. The host's canvas must exclude the image too — the host
            // still sees the row in HostLayerPanel where the eye toggle is.
            var map = Make();
            var img = Img(2, 3, 4, 4, hidden: true);

            var result = ImageVisibilityFilter.VisibleImagesFor(new[] { img }, map, isHost: true).ToList();

            Assert.IsEmpty(result);
        }

        // ── DisplayProjection integration ────────────────────────────────────

        [TestMethod]
        public void DisplayProjection_AppliesFogFilters()
        {
            var state = MakeState();
            // Fog a 2×2 block at (2,2)..(3,3).
            var tokenOnFog = Tok(2.5, 2.5);
            var tokenAside = Tok(5.5, 5.5);
            // Image fully on the fogged block (corners 2,2 / 3,2 / 2,3 / 3,3).
            var imgFullyFogged = Img(2, 2, 2, 2);
            // Image overlapping only partly (corners 1,1 revealed → stays visible).
            var imgPartial = Img(1, 1, 3, 3);

            var map = Make() with
            {
                Tokens = [tokenOnFog, tokenAside],
                Images = [imgFullyFogged, imgPartial],
            };
            map = Fog(map, (2, 2), (3, 2), (2, 3), (3, 3));
            state.Maps = state.Maps.Add(map);
            state.SetActiveMapId(map.Id);

            var projection = DisplayProjection.Build(state);

            CollectionAssert.AreEqual(new[] { tokenAside }, projection.VisibleTokens.ToArray());
            CollectionAssert.AreEqual(new[] { imgPartial }, projection.VisibleImages.ToArray());
        }

        [TestMethod]
        public void DisplayProjection_FogPathDataPopulated()
        {
            var state = MakeState();
            var map = Fog(Make(), (0, 0), (1, 0), (0, 1));
            state.Maps = state.Maps.Add(map);
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
            state.Maps = state.Maps.Add(map);
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
