using KnockBox.DndMapper.Services.Logic;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class DiceAnimationTrackerTests
    {
        [TestMethod]
        public void IsAnimating_ReturnsFalse_ByDefault()
        {
            var tracker = new DiceAnimationTracker();
            Assert.IsFalse(tracker.IsAnimating(Guid.NewGuid()));
        }

        [TestMethod]
        public void MarkAnimating_ThenIsAnimating_ReturnsTrue()
        {
            var tracker = new DiceAnimationTracker();
            var id = Guid.NewGuid();
            tracker.MarkAnimating(id);
            Assert.IsTrue(tracker.IsAnimating(id));
        }

        [TestMethod]
        public void MarkSettled_RemovesFromAnimating()
        {
            var tracker = new DiceAnimationTracker();
            var id = Guid.NewGuid();
            tracker.MarkAnimating(id);
            tracker.MarkSettled(id);
            Assert.IsFalse(tracker.IsAnimating(id));
        }

        [TestMethod]
        public void Changed_Fires_OnAdd()
        {
            var tracker = new DiceAnimationTracker();
            int hits = 0;
            tracker.Changed += () => hits++;
            tracker.MarkAnimating(Guid.NewGuid());
            Assert.AreEqual(1, hits);
        }

        [TestMethod]
        public void Changed_Fires_OnRemove()
        {
            var tracker = new DiceAnimationTracker();
            var id = Guid.NewGuid();
            tracker.MarkAnimating(id);
            int hits = 0;
            tracker.Changed += () => hits++;
            tracker.MarkSettled(id);
            Assert.AreEqual(1, hits);
        }

        [TestMethod]
        public void Changed_DoesNotFire_OnDuplicateMarkAnimating()
        {
            var tracker = new DiceAnimationTracker();
            var id = Guid.NewGuid();
            tracker.MarkAnimating(id);
            int hits = 0;
            tracker.Changed += () => hits++;
            tracker.MarkAnimating(id);
            Assert.AreEqual(0, hits);
        }

        [TestMethod]
        public void Changed_DoesNotFire_OnUnknownMarkSettled()
        {
            var tracker = new DiceAnimationTracker();
            int hits = 0;
            tracker.Changed += () => hits++;
            tracker.MarkSettled(Guid.NewGuid());
            Assert.AreEqual(0, hits);
        }
    }
}
