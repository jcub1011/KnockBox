using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class FogPolygonBuilderTests
    {
        private static Map Make(int width = 10, int height = 10) => new()
        {
            Id = Guid.NewGuid(),
            Grid = new GridConfig { WidthCells = width, HeightCells = height },
        };

        private static Map Make(int width, int height, params (int cx, int cy)[] fogged)
        {
            var m = Make(width, height);
            foreach (var (cx, cy) in fogged) m.SetFogged(cx, cy, true);
            return m;
        }

        [TestMethod]
        public void BuildSvgPathData_EmptyMask_ReturnsEmptyString()
        {
            var map = Make();
            Assert.AreEqual(string.Empty, FogPolygonBuilder.BuildSvgPathData(map));
        }

        [TestMethod]
        public void Build_EmptyMask_ReturnsEmptyList()
        {
            var map = Make();
            Assert.AreEqual(0, FogPolygonBuilder.Build(map).Count);
        }

        [TestMethod]
        public void Build_SingleCell_Returns4VertexRing()
        {
            var map = Make(10, 10, (2, 3));

            var rings = FogPolygonBuilder.Build(map);

            Assert.AreEqual(1, rings.Count);
            Assert.AreEqual(4, rings[0].Count);
            CollectionAssert.AreEquivalent(
                new[] { (2, 3), (3, 3), (3, 4), (2, 4) },
                rings[0].ToArray());
        }

        [TestMethod]
        public void BuildSvgPathData_SingleCell_EmitsOneClosedRectanglePath()
        {
            var map = Make(10, 10, (2, 3));

            var d = FogPolygonBuilder.BuildSvgPathData(map);

            StringAssert.StartsWith(d, "M ");
            StringAssert.EndsWith(d, " Z");
            Assert.AreEqual(1, CountMoveCommands(d));
            CollectionAssert.AreEquivalent(
                new[] { (2, 3), (3, 3), (3, 4), (2, 4) },
                ParseVertices(d).ToArray());
        }

        [TestMethod]
        public void Build_TwoByTwoBlock_CollapsesToFourVertexSquare()
        {
            var map = Make(10, 10, (1, 1), (2, 1), (1, 2), (2, 2));

            var rings = FogPolygonBuilder.Build(map);

            Assert.AreEqual(1, rings.Count);
            Assert.AreEqual(4, rings[0].Count);
            CollectionAssert.AreEquivalent(
                new[] { (1, 1), (3, 1), (3, 3), (1, 3) },
                rings[0].ToArray());
        }

        [TestMethod]
        public void Build_TwoDisconnectedClusters_ReturnsTwoRings()
        {
            var map = Make(10, 10, (0, 0), (5, 5));

            var rings = FogPolygonBuilder.Build(map);

            Assert.AreEqual(2, rings.Count);
            var allVerts = rings.SelectMany(r => r).ToHashSet();
            Assert.IsTrue(allVerts.SetEquals(new[]
            {
                (0, 0), (1, 0), (1, 1), (0, 1),
                (5, 5), (6, 5), (6, 6), (5, 6),
            }));
        }

        [TestMethod]
        public void Build_RingWithHole_ReturnsOuterAndInnerRings()
        {
            var map = Make(10, 10);
            for (var cy = 1; cy <= 3; cy++)
                for (var cx = 1; cx <= 3; cx++)
                    map.SetFogged(cx, cy, true);
            map.SetFogged(2, 2, false);

            var rings = FogPolygonBuilder.Build(map);

            Assert.AreEqual(2, rings.Count);
            var sizes = rings.Select(r => r.Count).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { 4, 4 }, sizes);

            var allVerts = rings.SelectMany(r => r).ToHashSet();
            Assert.IsTrue(allVerts.SetEquals(new[]
            {
                (1, 1), (4, 1), (4, 4), (1, 4),
                (2, 2), (3, 2), (3, 3), (2, 3),
            }));
        }

        [TestMethod]
        public void Build_DiagonalTouch_ProducesTwoSeparateRings()
        {
            var map = Make(10, 10, (0, 0), (1, 1));

            var rings = FogPolygonBuilder.Build(map);

            Assert.AreEqual(2, rings.Count);
            foreach (var ring in rings) Assert.AreEqual(4, ring.Count);
        }

        [TestMethod]
        public void BuildSvgPathData_FullyFogged_EmitsOuterRectanglePath()
        {
            var map = Make(4, 3);
            for (var cy = 0; cy < 3; cy++)
                for (var cx = 0; cx < 4; cx++)
                    map.SetFogged(cx, cy, true);

            var d = FogPolygonBuilder.BuildSvgPathData(map);

            StringAssert.StartsWith(d, "M ");
            StringAssert.EndsWith(d, " Z");
            Assert.AreEqual(1, CountMoveCommands(d));

            var rings = FogPolygonBuilder.Build(map);
            Assert.AreEqual(1, rings.Count);
            CollectionAssert.AreEquivalent(
                new[] { (0, 0), (4, 0), (4, 3), (0, 3) },
                rings[0].ToArray());
        }

        private static int CountMoveCommands(string pathData)
        {
            var count = 0;
            foreach (var ch in pathData) if (ch == 'M') count++;
            return count;
        }

        private static IEnumerable<(int X, int Y)> ParseVertices(string pathData)
        {
            var tokens = pathData.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] is "M" or "L")
                {
                    var x = int.Parse(tokens[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                    var y = int.Parse(tokens[i + 2], System.Globalization.CultureInfo.InvariantCulture);
                    yield return (x, y);
                    i += 2;
                }
            }
        }
    }
}
