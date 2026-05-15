using KnockBox.DndMapper.Pages.Components;

namespace KnockBox.DndMapperTests.Unit.Components
{
    [TestClass]
    public class MapCanvasResizeTests
    {
        private static MapCanvas.DragState Drag(
            MapCanvas.HandleKind kind,
            double x = 5, double y = 5, double w = 4, double h = 2)
        {
            return new MapCanvas.DragState
            {
                Kind = kind,
                OrigX = x,
                OrigY = y,
                OrigW = w,
                OrigH = h,
                X = x,
                Y = y,
                W = w,
                H = h,
            };
        }

        [TestMethod]
        public void ApplyResize_SECorner_FreeAspect_GrowsBothIndependently()
        {
            var d = Drag(MapCanvas.HandleKind.SE);
            MapCanvas.ApplyResize(d, dx: 3, dy: 1, freeAspect: true);

            // SE grows W by +dx and H by +dy; anchor (NW = origX/origY) stays put.
            Assert.AreEqual(5, d.X, 1e-9);
            Assert.AreEqual(5, d.Y, 1e-9);
            Assert.AreEqual(7, d.W, 1e-9);
            Assert.AreEqual(3, d.H, 1e-9);
        }

        [TestMethod]
        public void ApplyResize_SECorner_AspectLocked_DominantAxisDrivesScale()
        {
            // origW=4 origH=2 → aspect 2:1. dx=4 → scaleW=2; dy=0 → scaleH=1. Width moved further.
            var d = Drag(MapCanvas.HandleKind.SE);
            MapCanvas.ApplyResize(d, dx: 4, dy: 0, freeAspect: false);

            Assert.AreEqual(8, d.W, 1e-9);
            Assert.AreEqual(4, d.H, 1e-9);
            Assert.AreEqual(5, d.X, 1e-9);
            Assert.AreEqual(5, d.Y, 1e-9);
        }

        [TestMethod]
        public void ApplyResize_SECorner_AspectLocked_ShrinkOneAxis_ImageShrinks()
        {
            // Regression for the Math.Max-vs-dominant-axis fix.
            // dx=-2 → scaleW=0.5 (moved 0.5 from 1.0); dy=0 → scaleH=1.0 (moved 0).
            // Old code (Math.Max) would have picked scaleH=1.0 and *kept the image at orig size*
            // (or grown if any other axis was > 1). New code picks the dominant deviation → shrink.
            var d = Drag(MapCanvas.HandleKind.SE);
            MapCanvas.ApplyResize(d, dx: -2, dy: 0, freeAspect: false);

            Assert.AreEqual(2, d.W, 1e-9);
            Assert.AreEqual(1, d.H, 1e-9);
        }

        [TestMethod]
        public void ApplyResize_NWCorner_FreeAspect_RepositionsAnchorSE()
        {
            // NW drag: SE corner (origX+origW, origY+origH) = (9,7) must stay fixed.
            var d = Drag(MapCanvas.HandleKind.NW);
            MapCanvas.ApplyResize(d, dx: -1, dy: -1, freeAspect: true);

            // New W = 4 - (-1) = 5; new H = 2 - (-1) = 3.
            Assert.AreEqual(5, d.W, 1e-9);
            Assert.AreEqual(3, d.H, 1e-9);
            // X = origX + (origW - newW) = 5 + (4-5) = 4; Y = 5 + (2-3) = 4. SE = (4+5, 4+3) = (9,7). ✓
            Assert.AreEqual(4, d.X, 1e-9);
            Assert.AreEqual(4, d.Y, 1e-9);
            Assert.AreEqual(9, d.X + d.W, 1e-9);
            Assert.AreEqual(7, d.Y + d.H, 1e-9);
        }

        [TestMethod]
        public void ApplyResize_NECorner_PinsSWCorner()
        {
            // SW = (origX, origY+origH) = (5, 7) must stay fixed.
            var d = Drag(MapCanvas.HandleKind.NE);
            MapCanvas.ApplyResize(d, dx: 2, dy: -1, freeAspect: true);

            Assert.AreEqual(6, d.W, 1e-9);
            Assert.AreEqual(3, d.H, 1e-9);
            Assert.AreEqual(5, d.X, 1e-9);                // east → X anchors to OrigX
            Assert.AreEqual(4, d.Y, 1e-9);                // not south → Y shifts by (origH - newH)
            Assert.AreEqual(5, d.X, 1e-9);
            Assert.AreEqual(7, d.Y + d.H, 1e-9);
        }

        [TestMethod]
        public void ApplyResize_SWCorner_PinsNECorner()
        {
            // NE = (origX+origW, origY) = (9, 5) must stay fixed.
            var d = Drag(MapCanvas.HandleKind.SW);
            MapCanvas.ApplyResize(d, dx: -1, dy: 2, freeAspect: true);

            Assert.AreEqual(5, d.W, 1e-9);
            Assert.AreEqual(4, d.H, 1e-9);
            Assert.AreEqual(4, d.X, 1e-9);
            Assert.AreEqual(5, d.Y, 1e-9);
            Assert.AreEqual(9, d.X + d.W, 1e-9);
            Assert.AreEqual(5, d.Y, 1e-9);
        }

        [TestMethod]
        public void ApplyResize_ClampsToMinDimension()
        {
            var d = Drag(MapCanvas.HandleKind.SE);
            MapCanvas.ApplyResize(d, dx: -100, dy: -100, freeAspect: true);

            Assert.AreEqual(0.1, d.W, 1e-9);
            Assert.AreEqual(0.1, d.H, 1e-9);
        }
    }
}
