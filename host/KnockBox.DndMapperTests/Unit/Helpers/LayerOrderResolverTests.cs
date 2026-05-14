using KnockBox.DndMapper.Helpers;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class LayerOrderResolverTests
    {
        [TestMethod]
        public void IntMaxValue_ResolvesToTopIndex()
            => Assert.AreEqual(4, LayerOrderResolver.Resolve(int.MaxValue, currentIndex: 1, imageCount: 5));

        [TestMethod]
        public void IntMinValue_ResolvesToBottomIndex()
            => Assert.AreEqual(0, LayerOrderResolver.Resolve(int.MinValue, currentIndex: 3, imageCount: 5));

        [TestMethod]
        public void PositiveDelta_ShiftsByDelta()
            => Assert.AreEqual(3, LayerOrderResolver.Resolve(+1, currentIndex: 2, imageCount: 5));

        [TestMethod]
        public void NegativeDelta_ShiftsByDelta()
            => Assert.AreEqual(1, LayerOrderResolver.Resolve(-1, currentIndex: 2, imageCount: 5));

        [TestMethod]
        public void DeltaDownAtBottom_ReturnsNullNoOp()
            => Assert.IsNull(LayerOrderResolver.Resolve(-1, currentIndex: 0, imageCount: 5));

        [TestMethod]
        public void DeltaUpAtTop_ReturnsNullNoOp()
            => Assert.IsNull(LayerOrderResolver.Resolve(+1, currentIndex: 4, imageCount: 5));

        [TestMethod]
        public void ToTop_AlreadyOnTop_ReturnsNullNoOp()
            => Assert.IsNull(LayerOrderResolver.Resolve(int.MaxValue, currentIndex: 4, imageCount: 5));

        [TestMethod]
        public void ToBottom_AlreadyOnBottom_ReturnsNullNoOp()
            => Assert.IsNull(LayerOrderResolver.Resolve(int.MinValue, currentIndex: 0, imageCount: 5));

        [TestMethod]
        public void EmptyList_ReturnsNull()
            => Assert.IsNull(LayerOrderResolver.Resolve(+1, currentIndex: 0, imageCount: 0));

        [TestMethod]
        public void SingleImage_AnyDelta_ReturnsNull()
        {
            Assert.IsNull(LayerOrderResolver.Resolve(+5, currentIndex: 0, imageCount: 1));
            Assert.IsNull(LayerOrderResolver.Resolve(-5, currentIndex: 0, imageCount: 1));
            Assert.IsNull(LayerOrderResolver.Resolve(int.MaxValue, currentIndex: 0, imageCount: 1));
            Assert.IsNull(LayerOrderResolver.Resolve(int.MinValue, currentIndex: 0, imageCount: 1));
        }

        [TestMethod]
        public void LargePositiveDelta_ClampsToTop()
            => Assert.AreEqual(4, LayerOrderResolver.Resolve(+999, currentIndex: 2, imageCount: 5));

        [TestMethod]
        public void LargeNegativeDelta_ClampsToBottom()
            => Assert.AreEqual(0, LayerOrderResolver.Resolve(-999, currentIndex: 2, imageCount: 5));
    }
}
