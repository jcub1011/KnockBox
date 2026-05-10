using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class TokenVisibilityFilterTests
    {
        private static Token Make(bool hidden) =>
            new() { Id = Guid.NewGuid(), Hidden = hidden };

        [TestMethod]
        public void NonHost_ExcludesHidden()
        {
            var visible = Make(hidden: false);
            var hidden = Make(hidden: true);

            var result = TokenVisibilityFilter.VisibleTokensFor([visible, hidden], isHost: false).ToList();

            CollectionAssert.AreEqual(new[] { visible }, result);
        }

        [TestMethod]
        public void Host_IncludesHidden()
        {
            var visible = Make(hidden: false);
            var hidden = Make(hidden: true);

            var result = TokenVisibilityFilter.VisibleTokensFor([visible, hidden], isHost: true).ToList();

            CollectionAssert.AreEquivalent(new[] { visible, hidden }, result);
        }

        [TestMethod]
        public void NonHidden_AlwaysIncluded()
        {
            var t1 = Make(false);
            var t2 = Make(false);
            var asHost = TokenVisibilityFilter.VisibleTokensFor([t1, t2], true).ToList();
            var asPlayer = TokenVisibilityFilter.VisibleTokensFor([t1, t2], false).ToList();
            CollectionAssert.AreEquivalent(new[] { t1, t2 }, asHost);
            CollectionAssert.AreEquivalent(new[] { t1, t2 }, asPlayer);
        }
    }
}
