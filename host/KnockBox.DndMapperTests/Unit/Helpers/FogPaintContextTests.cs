using KnockBox.DndMapper.Services.Logic.Visibility;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class FogPaintContextTests
    {
        [TestMethod]
        public void Defaults_AreOffAndMinBrush()
        {
            var ctx = new FogPaintContext();
            Assert.AreEqual(FogPaintMode.Off, ctx.Mode);
            Assert.AreEqual(FogPaintContext.MinBrush, ctx.BrushRadius);
        }

        [TestMethod]
        public void Set_FiresChangedEvent()
        {
            var ctx = new FogPaintContext();
            var fired = 0;
            ctx.Changed += () => fired++;

            ctx.Set(FogPaintMode.Paint, 2);

            Assert.AreEqual(1, fired);
            Assert.AreEqual(FogPaintMode.Paint, ctx.Mode);
            Assert.AreEqual(2, ctx.BrushRadius);
        }

        [TestMethod]
        public void Set_NoChange_DoesNotFireEvent()
        {
            var ctx = new FogPaintContext();
            ctx.Set(FogPaintMode.Paint, 2);
            var fired = 0;
            ctx.Changed += () => fired++;

            ctx.Set(FogPaintMode.Paint, 2);

            Assert.AreEqual(0, fired);
        }

        [TestMethod]
        public void Set_BrushRadiusBelowMin_ClampsToMin()
        {
            var ctx = new FogPaintContext();
            ctx.Set(FogPaintMode.Paint, 0);
            Assert.AreEqual(FogPaintContext.MinBrush, ctx.BrushRadius);

            ctx.Set(FogPaintMode.Paint, -5);
            Assert.AreEqual(FogPaintContext.MinBrush, ctx.BrushRadius);
        }

        [TestMethod]
        public void Set_BrushRadiusAboveMax_ClampsToMin()
        {
            // Out-of-range values fall back to MinBrush so a UI bug or stale
            // state can't leave the canvas with an unusable brush.
            var ctx = new FogPaintContext();
            ctx.Set(FogPaintMode.Paint, 99);
            Assert.AreEqual(FogPaintContext.MinBrush, ctx.BrushRadius);
        }

        [TestMethod]
        public void Set_BrushRadiusInRange_KeepsValue()
        {
            var ctx = new FogPaintContext();
            ctx.Set(FogPaintMode.Paint, FogPaintContext.MaxBrush);
            Assert.AreEqual(FogPaintContext.MaxBrush, ctx.BrushRadius);
        }
    }
}
