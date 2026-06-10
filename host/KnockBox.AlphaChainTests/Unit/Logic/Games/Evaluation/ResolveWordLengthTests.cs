using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.Evaluation
{
    /// <summary>
    /// The perceived-letter-count system. Each <see cref="ILetterCountModifier"/> owns its own length
    /// math (Forgery doubles, folding in any magnification applied to it) and stacks by asking the
    /// <see cref="ModifierCapabilityExtensions.ResolveWordLength"/> helper for the count before it. The
    /// helper returns the resolution of the most recent modifier placed <i>strictly before</i> the
    /// querying card — never the card itself — so a modifier's own method can call back into the helper
    /// without recursing forever.
    /// </summary>
    [TestClass]
    public class ResolveWordLengthTests
    {
        private static readonly ModifierCardFactory Factory = new();

        private static IModifierCard Card(ModifierId id) => Factory.CreateCard(default, id);
        private static IModifierCard Forgery() => Card(ModifierId.Forgery);
        private static IModifierCard Vanilla() => Card(ModifierId.Vanilla);
        private static IModifierCard Glass() => Card(ModifierId.MagnifyingGlass);

        // Mirrors production: WithBay builds the EffectMagnifier from the same ordered bay, so a
        // Magnifying Glass's magnification flows into the cards it targets exactly as it would at scoring time.
        private static EngineEvaluationContext Ctx(string word, IReadOnlyList<IModifierCard> bay)
            => new EngineEvaluationContext(word, Array.Empty<char>(), new[] { new AlphaChainPlayerState { UserId = Guid.NewGuid() } })
            {
                PlayerIndex = 0,
            }.WithBay(bay);

        [TestMethod]
        public void NoModifier_PerceivesTheRealWordLength()
        {
            var vanilla = Vanilla();
            // [Vanilla] "cat": nothing inflates the count → the real 3.
            Assert.AreEqual(3, vanilla.ResolveWordLength(Ctx("cat", [vanilla])));
        }

        [TestMethod]
        public void ForgeryBefore_DoublesThePerceivedLength()
        {
            var vanilla = Vanilla();
            // [Forgery, Vanilla] "cat": Vanilla perceives the Forgery-doubled 6.
            Assert.AreEqual(6, vanilla.ResolveWordLength(Ctx("cat", [Forgery(), vanilla])));
        }

        [TestMethod]
        public void CardPlacedBeforeForgery_IsUnaffected()
        {
            var vanilla = Vanilla();
            // [Vanilla, Forgery] "cat": Vanilla sits before the Forgery, so it still perceives the real 3.
            Assert.AreEqual(3, vanilla.ResolveWordLength(Ctx("cat", [vanilla, Forgery()])));
        }

        [TestMethod]
        public void StackedForgeries_CompoundMultiplicatively()
        {
            var vanilla = Vanilla();
            // [Forgery, Forgery, Vanilla] "cat": 3 → 6 → 12. The second Forgery stacks on the first by
            // calling the helper for the count before it (6), then doubling.
            Assert.AreEqual(12, vanilla.ResolveWordLength(Ctx("cat", [Forgery(), Forgery(), vanilla])));
        }

        [TestMethod]
        public void HelperReturnsTheMostRecentModifier_NotAllOfThemFolded()
        {
            // A non-stacking modifier would simply return its own resolution; the helper delegates to the
            // most recent one and lets that card decide. With Forgery stacking, [Forgery, Forgery] before a
            // querying card resolves through the chain, landing on the latest (×4), not a one-level ×2.
            var vanilla = Vanilla();
            Assert.AreEqual(12, vanilla.ResolveWordLength(Ctx("cat", [Forgery(), Forgery(), vanilla])));
        }

        [TestMethod]
        public void Magnification_OnForgery_ScalesItsPerceivedLength()
        {
            var vanilla = Vanilla();
            // [Glass, Forgery, Vanilla] "cat": the glass turns Forgery's ×2 into ×3 → 3 × 3 = 9.
            Assert.AreEqual(9, vanilla.ResolveWordLength(Ctx("cat", [Glass(), Forgery(), vanilla])));
        }

        [TestMethod]
        public void StackedGlasses_CompoundOntoForgerysPerceivedLength()
        {
            var vanilla = Vanilla();
            // [Glass, Glass, Forgery, Vanilla] "cat": the glasses compound to ×2.25 on Forgery → its factor
            // is 2 × 2.25 = 4.5 → round(3 × 4.5) = 14 (half-up).
            Assert.AreEqual(14, vanilla.ResolveWordLength(Ctx("cat", [Glass(), Glass(), Forgery(), vanilla])));
        }

        [TestMethod]
        public void Helper_NeverPerceivesTheQueryingCardItself()
        {
            var forgery = Forgery();
            // [Forgery] "cat": asking the helper what Forgery itself perceives excludes Forgery → the real 3.
            // (If the helper delegated to the card itself this would recurse forever.)
            Assert.AreEqual(3, forgery.ResolveWordLength(Ctx("cat", [forgery])));
        }

        [TestMethod]
        public void InterfaceMethod_EmitsTheDoubledLength_ForLaterCards()
        {
            var forgery = Forgery();
            // The ILetterCountModifier method is what Forgery emits to cards after it: 3 → 6.
            Assert.AreEqual(6, ((ILetterCountModifier)forgery).ResolveWordLength(Ctx("cat", [forgery]), "cat"));
        }

        [TestMethod]
        public void InterfaceMethod_StacksOnAnEarlierForgery()
        {
            var first = Forgery();
            var second = Forgery();
            // The second Forgery's emitted length builds on the first (6) then doubles → 12.
            Assert.AreEqual(12, ((ILetterCountModifier)second).ResolveWordLength(Ctx("cat", [first, second]), "cat"));
        }
    }
}
