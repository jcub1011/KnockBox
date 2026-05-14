using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class SnapToGridHelperTests
    {
        private static GridConfig Grid(bool snap, int w = 30, int h = 20) =>
            new() { WidthCells = w, HeightCells = h, SnapToGrid = snap };

        [TestMethod]
        public void Snap_AlreadyOnCenter_NoChange()
        {
            var (x, y) = SnapToGridHelper.Snap(5.5, 7.5, Grid(true));
            Assert.AreEqual(5.5, x, 1e-9);
            Assert.AreEqual(7.5, y, 1e-9);
        }

        [TestMethod]
        public void Snap_QuarterCellOffset_RoundsToNearestCenter()
        {
            var (x, y) = SnapToGridHelper.Snap(5.7, 7.3, Grid(true));
            Assert.AreEqual(5.5, x, 1e-9);
            Assert.AreEqual(7.5, y, 1e-9);
        }

        [TestMethod]
        public void Snap_OutOfBounds_Clamps()
        {
            var grid = Grid(true, w: 10, h: 10);
            var (lowX, lowY) = SnapToGridHelper.Snap(-3, -4, grid);
            var (highX, highY) = SnapToGridHelper.Snap(99, 99, grid);
            Assert.AreEqual(0.5, lowX, 1e-9);
            Assert.AreEqual(0.5, lowY, 1e-9);
            Assert.AreEqual(9.5, highX, 1e-9);
            Assert.AreEqual(9.5, highY, 1e-9);
        }

        [TestMethod]
        public void NoSnap_ReturnsClampedRaw()
        {
            var grid = Grid(false, w: 10, h: 10);
            var (x, y) = SnapToGridHelper.Snap(3.7, 4.2, grid);
            Assert.AreEqual(3.7, x, 1e-9);
            Assert.AreEqual(4.2, y, 1e-9);
            var (cx, cy) = SnapToGridHelper.Snap(-1, 99, grid);
            Assert.AreEqual(0, cx, 1e-9);
            Assert.AreEqual(10, cy, 1e-9);
        }

        [TestMethod]
        public void SnapCorner_OnIntegerCells_NoChange()
        {
            var (x, y) = SnapToGridHelper.SnapCorner(3, 7, Grid(true));
            Assert.AreEqual(3, x, 1e-9);
            Assert.AreEqual(7, y, 1e-9);
        }

        [TestMethod]
        public void SnapCorner_FractionalRoundsToNearestWholeCell()
        {
            var (x, y) = SnapToGridHelper.SnapCorner(3.3, 7.7, Grid(true));
            Assert.AreEqual(3, x, 1e-9);
            Assert.AreEqual(8, y, 1e-9);
        }

        [TestMethod]
        public void SnapCorner_OutOfBounds_Clamps()
        {
            var grid = Grid(true, w: 10, h: 10);
            var (lowX, lowY) = SnapToGridHelper.SnapCorner(-5, -2, grid);
            var (highX, highY) = SnapToGridHelper.SnapCorner(99, 99, grid);
            Assert.AreEqual(0, lowX, 1e-9);
            Assert.AreEqual(0, lowY, 1e-9);
            Assert.AreEqual(10, highX, 1e-9);
            Assert.AreEqual(10, highY, 1e-9);
        }

        [TestMethod]
        public void SnapCorner_NoSnap_ReturnsClampedRaw()
        {
            var grid = Grid(false, w: 10, h: 10);
            var (x, y) = SnapToGridHelper.SnapCorner(3.7, 4.2, grid);
            Assert.AreEqual(3.7, x, 1e-9);
            Assert.AreEqual(4.2, y, 1e-9);
        }
    }
}
