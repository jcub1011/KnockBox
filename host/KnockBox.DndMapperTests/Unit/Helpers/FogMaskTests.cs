using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class FogMaskTests
    {
        private static Map Make(int width = 100, int height = 100) =>
            new()
            {
                Id = Guid.NewGuid(),
                Grid = new GridConfig { WidthCells = width, HeightCells = height },
            };

        [TestMethod]
        public void IsFogged_EmptyMask_ReturnsFalse()
        {
            var map = Make();
            Assert.IsFalse(map.IsFogged(0, 0));
            Assert.IsFalse(map.IsFogged(50, 50));
            Assert.IsFalse(map.IsFogged(99, 99));
            Assert.IsEmpty(map.FogMask);
        }

        [TestMethod]
        public void SetFogged_AllocatesOnFirstCall()
        {
            var map = Make();
            Assert.IsEmpty(map.FogMask);

            map = map.WithCellFogged(0, 0, true);

            Assert.IsTrue(map.FogMask.Length > 0);
            Assert.IsTrue(map.IsFogged(0, 0));
        }

        [TestMethod]
        public void SetFogged_OutOfBounds_NoOp()
        {
            var map = Make(10, 10);
            map = map.WithCellFogged(-1, 0, true);
            map = map.WithCellFogged(0, -1, true);
            map = map.WithCellFogged(10, 0, true);
            map = map.WithCellFogged(0, 10, true);

            Assert.IsEmpty(map.FogMask);
        }

        [TestMethod]
        public void SetFogged_Roundtrip()
        {
            var map = Make();
            map = map.WithCellFogged(3, 4, true);
            Assert.IsTrue(map.IsFogged(3, 4));

            map = map.WithCellFogged(3, 4, false);
            Assert.IsFalse(map.IsFogged(3, 4));
        }

        [TestMethod]
        public void SetFogged_MultipleCells_NoCrosstalk()
        {
            var map = Make(100, 100);
            map = map.WithCellFogged(0, 0, true);
            map = map.WithCellFogged(99, 99, true);

            Assert.IsTrue(map.IsFogged(0, 0));
            Assert.IsTrue(map.IsFogged(99, 99));
            Assert.IsFalse(map.IsFogged(0, 1));
            Assert.IsFalse(map.IsFogged(1, 0));
            Assert.IsFalse(map.IsFogged(98, 99));
            Assert.IsFalse(map.IsFogged(99, 98));
        }
    }
}
