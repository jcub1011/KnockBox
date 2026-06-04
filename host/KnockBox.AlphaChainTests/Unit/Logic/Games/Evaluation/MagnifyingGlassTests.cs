using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.Evaluation
{
    /// <summary>
    /// The Magnifying Glass magnifies the effect of the card immediately to its right by ×1.5, decided
    /// per-card: a real scoring card scales its magnitude directly (a ×2 becomes a ×3), an inert ×1.0
    /// card is left alone, and Forgery scales its perceived-length factor. Glasses in series compound
    /// onto the one neighbor (1.5 × 1.5 = 2.25), with no card knowing about its neighbor and the
    /// <see cref="IEffectMagnifier"/> service knowing nothing about stacking.
    /// </summary>
    [TestClass]
    public class MagnifyingGlassTests
    {
        private readonly EngineEvaluator _eval = new();
        private static readonly ModifierCardFactory Factory = new();

        private static IModifierCard Card(ModifierId id) => Factory.CreateCard(default, id);
        private static IModifierCard Glass() => Card(ModifierId.MagnifyingGlass);

        private static CommonModifier Additive(double value) => new(
            ModifierId.Unknown, "Add", "", ModifierType.Additive, static _ => true, (_, _) => value);

        private static CommonModifier Mult(double factor) => new(
            ModifierId.Unknown, "Mul", "", ModifierType.Multiplier, static _ => true, (_, _) => factor);

        private static EngineEvaluationContext Ctx(string word, IReadOnlyList<IModifierCard> bay)
            => new(word, Array.Empty<char>(), new[] { new AlphaChainPlayerState { UserId = Guid.NewGuid() } })
            {
                Bay = bay,
                PlayerIndex = 0,
            };

        [TestMethod]
        public void Glass_MagnifiesImmediateAdditiveNeighbor()
            // [Glass, +10] "cat": seed 3 + (10 × 1.5) = 18.
            => Assert.AreEqual(18, _eval.Calculate(Ctx("cat", [Glass(), Additive(10)])));

        [TestMethod]
        public void Glass_DoesNotMagnifyANonAdjacentCard()
            // [Glass, +0, +10] "cat": the glass hits only the +0; the +10 two slots over is untouched → 13.
            => Assert.AreEqual(13, _eval.Calculate(Ctx("cat", [Glass(), Additive(0), Additive(10)])));

        [TestMethod]
        public void Glass_ScalesAMultiplierFactorDirectly()
            // [Glass, ×2] "cat": factor 2 × 1.5 = ×3 → 3 × 3 = 9.
            => Assert.AreEqual(9, _eval.Calculate(Ctx("cat", [Glass(), Mult(2)])));

        [TestMethod]
        public void Glass_LeavesAnInertCardInert()
            // [Glass, Catalyst] "bridge": Catalyst is ×1.0 / FX — the glass must NOT turn it into a ×1.5.
            => Assert.AreEqual(6, _eval.Calculate(Ctx("bridge", [Glass(), Card(ModifierId.Catalyst)])));

        [TestMethod]
        public void Glass_IsScoreNeutralOnItsOwn()
            // A glass with nothing to its right contributes nothing.
            => Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Glass()])));

        [TestMethod]
        public void TwoGlasses_CompoundOntoTheNeighbor()
        {
            // [Glass, Glass, +4] "cat": the second glass is itself magnified (×1.5) so it emits ×2.25 → +9 → 12.
            Assert.AreEqual(12, _eval.Calculate(Ctx("cat", [Glass(), Glass(), Additive(4)])));
            // Same compounding on a multiplier: ×2 × 2.25 = ×4.5 → 3 × 4.5 = 13.5 → 14 (half-up).
            Assert.AreEqual(14, _eval.Calculate(Ctx("cat", [Glass(), Glass(), Mult(2)])));
        }

        [TestMethod]
        public void Glass_MagnifiesForgerysPerceivedLengthDirectly()
        {
            // Control: [Forgery, Vanilla] "cat" → Vanilla perceives 6 → +6 → 9.
            Assert.AreEqual(9, _eval.Calculate(Ctx("cat", [Card(ModifierId.Forgery), Card(ModifierId.Vanilla)])));
            // [Glass, Forgery, Vanilla] "cat": Forgery's ×2 becomes ×3 → Vanilla perceives 9 (≥7) → +18 → 21.
            Assert.AreEqual(21, _eval.Calculate(Ctx("cat", [Glass(), Card(ModifierId.Forgery), Card(ModifierId.Vanilla)])));
        }

        [TestMethod]
        public void Glass_AppearsInTheScoreStepTrace_WithTheMagnifiedValue()
        {
            // [Glass, ×2] → the ×2 step reads ×3 in the replay trace.
            var breakdown = _eval.CalculateSteps(Ctx("cat", [Glass(), Mult(2)]), taxed: false);
            Assert.AreEqual("×3", breakdown.Steps[1].ValueText);
            Assert.AreEqual(9, breakdown.FinalScore);
        }
    }
}
