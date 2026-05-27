using System.Collections.Immutable;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.LoadedDice;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class LoadedDiceProcessorTests
    {
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_, _state, _host, _) = EngineTestFactory.Build();
        }

        // Builds a context with a stub RNG that returns sequential values
        // so Bias* modifications are reproducible.
        private LoadedDiceContext ContextFor(int sides, params int[] rngStream)
        {
            int idx = 0;
            return new LoadedDiceContext
            {
                Caller = _host,
                State = _state,
                Request = new RollRequest(
                    Dice: [new DiceTerm(1, sides)],
                    AttributeRef: null,
                    FlatModifier: 0,
                    Mode: RollMode.Normal,
                    Label: "test"),
                RollerSheetId = null,
                DiceTermSides = sides,
                HostHeldKeys = ImmutableHashSet<string>.Empty,
                RollNewDie = _ => idx < rngStream.Length ? rngStream[idx++] : 1,
            };
        }

        [TestMethod]
        public void Apply_NoRules_ReturnsEmptyStampsAndLeavesDice()
        {
            var rolls = new List<DieRoll> { new(20, 11, false) };
            var stamps = LoadedDiceProcessor.Apply(rolls, Array.Empty<LoadedDiceRule>(), null, _ => ContextFor(20));
            Assert.AreEqual(0, stamps.Length);
            Assert.AreEqual(11, rolls[0].Result);
        }

        [TestMethod]
        public void HostKeyHeld_MatchesCaseInsensitively()
        {
            // Rule authored as uppercase "A"; the streamed held set carries
            // lowercase "a" (browser reports "A" only while Shift is held).
            // The match must still fire. Held set uses the Ordinal comparer,
            // mirroring how the engine builds HostHeldKeys.
            var ctx = ContextFor(20) with
            {
                HostHeldKeys = ImmutableHashSet.Create(StringComparer.Ordinal, "a"),
            };
            Assert.IsTrue(new HostKeyHeldCondition("A").Matches(ctx));
            Assert.IsTrue(new HostKeyHeldCondition("a").Matches(ctx));
            Assert.IsFalse(new HostKeyHeldCondition("b").Matches(ctx));
        }

        [TestMethod]
        public void Apply_EmptyConditionsEmptyTargets_FiresOnEveryDie()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Crit",
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
            Assert.AreEqual(1, stamps.Length);
            Assert.AreEqual("Crit", stamps[0].RuleName);
        }

        [TestMethod]
        public void Apply_TargetSetExcludesRoll_SkipsRule()
        {
            var targetSheet = Guid.NewGuid();
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Targeted",
                TargetSheetIds = ImmutableHashSet.Create(targetSheet),
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            // Roll comes from a different (or null) sheet — rule must skip.
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(3, rolls[0].Result);
            Assert.AreEqual(0, stamps.Length);
        }

        [TestMethod]
        public void Apply_ConditionFails_SkipsRule()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "d6 only",
                Conditions = [new DiceTypeRolledCondition(6)],
                Modifications = [new SetResultModification(1)],
            };
            var rolls = new List<DieRoll> { new(20, 17, false) };
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, sides => ContextFor(sides));
            Assert.AreEqual(17, rolls[0].Result);
            Assert.AreEqual(0, stamps.Length);
        }

        [TestMethod]
        public void Apply_DisabledRule_Skipped()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Off",
                Enabled = false,
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(3, rolls[0].Result);
            Assert.AreEqual(0, stamps.Length);
        }

        [TestMethod]
        public void Apply_RulesChain_TopToBottomLastWriteWins()
        {
            // First rule clamps to <=10; second rule overwrites with 18.
            var ruleA = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Cap10",
                Modifications = [new ClampMaxModification(10)],
            };
            var ruleB = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Set18",
                Modifications = [new SetResultModification(18)],
            };
            var rolls = new List<DieRoll> { new(20, 17, false) };
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { ruleA, ruleB }, null, _ => ContextFor(20));
            Assert.AreEqual(18, rolls[0].Result);
            Assert.AreEqual(2, stamps.Length);
            Assert.AreEqual("Cap10", stamps[0].RuleName);
            Assert.AreEqual("Set18", stamps[1].RuleName);
        }

        [TestMethod]
        public void Apply_ClampsResultToDieSidesAfterEachModification()
        {
            // "Set to 99" must clamp to [1, sides] so the visible face stays legal.
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Overshoot",
                Modifications = [new SetResultModification(99)],
            };
            var rolls = new List<DieRoll> { new(6, 4, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(6));
            Assert.AreEqual(6, rolls[0].Result);
        }

        [TestMethod]
        public void Apply_StampsDeduplicatedAcrossMultipleDice()
        {
            // One rule that fires on three d6 should produce a single stamp.
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "All ones",
                Modifications = [new SetResultModification(1)],
            };
            var rolls = new List<DieRoll>
            {
                new(6, 4, false),
                new(6, 5, false),
                new(6, 6, false),
            };
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(6));
            Assert.AreEqual(1, stamps.Length);
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, rolls.Select(r => r.Result).ToArray());
        }

        [TestMethod]
        public void Apply_TargetIsGm_MatchesUnattributedRoll()
        {
            // A roll without a sheet attribution should match a rule whose
            // target list contains only the GM sentinel.
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "GM only",
                TargetSheetIds = ImmutableHashSet.Create(LoadedDiceRule.GmTarget),
                Modifications = [new SetResultModification(1)],
            };
            var rolls = new List<DieRoll> { new(20, 15, false) };
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { rule }, rollerSheetId: null, _ => ContextFor(20));
            Assert.AreEqual(1, rolls[0].Result);
            Assert.AreEqual(1, stamps.Length);
        }

        [TestMethod]
        public void Apply_TargetIsGm_DoesNotMatchAttributedRoll()
        {
            // A roll WITH a sheet must not match a GM-only rule.
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "GM only",
                TargetSheetIds = ImmutableHashSet.Create(LoadedDiceRule.GmTarget),
                Modifications = [new SetResultModification(1)],
            };
            var rolls = new List<DieRoll> { new(20, 15, false) };
            var stamps = LoadedDiceProcessor.Apply(rolls, new[] { rule }, rollerSheetId: Guid.NewGuid(), _ => ContextFor(20));
            Assert.AreEqual(15, rolls[0].Result);
            Assert.AreEqual(0, stamps.Length);
        }

        [TestMethod]
        public void Apply_TargetIsGmPlusSheet_MatchesEither()
        {
            // Mixed targets — GM sentinel + a real sheet — fire for either case.
            var sheetA = Guid.NewGuid();
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "GM or A",
                TargetSheetIds = ImmutableHashSet.Create(LoadedDiceRule.GmTarget, sheetA),
                Modifications = [new SetResultModification(1)],
            };

            var unattributed = new List<DieRoll> { new(20, 15, false) };
            LoadedDiceProcessor.Apply(unattributed, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(1, unattributed[0].Result);

            var attributedA = new List<DieRoll> { new(20, 15, false) };
            LoadedDiceProcessor.Apply(attributedA, new[] { rule }, sheetA, _ => ContextFor(20));
            Assert.AreEqual(1, attributedA[0].Result);

            var attributedOther = new List<DieRoll> { new(20, 15, false) };
            LoadedDiceProcessor.Apply(attributedOther, new[] { rule }, Guid.NewGuid(), _ => ContextFor(20));
            Assert.AreEqual(15, attributedOther[0].Result);
        }

        [TestMethod]
        public void RollerIsCondition_GmSentinel_MatchesNullSheet()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "GM rolls only",
                Conditions = [new RollerIsCondition(LoadedDiceRule.GmTarget)],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, rollerSheetId: null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
        }

        [TestMethod]
        public void BiasLower_UsesContextRollNewDie()
        {
            // Original 5, extra rolls (3, 2). Min of (5, 3, 2) = 2.
            var ctxFor = (int sides) => ContextFor(sides, 3, 2);
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Bias low",
                Modifications = [new BiasLowerModification(2)],
            };
            var rolls = new List<DieRoll> { new(20, 5, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, ctxFor);
            Assert.AreEqual(2, rolls[0].Result);
        }

        // ── Composite conditions (AllOf / AnyOf / Not) ───────────────────────

        [TestMethod]
        public void AllOf_AllChildrenTrue_Fires()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "AllOf",
                Conditions =
                [
                    new AllOfCondition(ImmutableArray.Create<LoadedDiceCondition>(
                        new DiceTypeRolledCondition(20),
                        new RollModeIsCondition(RollMode.Normal))),
                ],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
        }

        [TestMethod]
        public void AllOf_OneChildFalse_DoesNotFire()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "AllOf",
                Conditions =
                [
                    new AllOfCondition(ImmutableArray.Create<LoadedDiceCondition>(
                        new DiceTypeRolledCondition(20),
                        new DiceTypeRolledCondition(6))),
                ],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(3, rolls[0].Result);
        }

        [TestMethod]
        public void AnyOf_OneChildTrue_Fires()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "AnyOf",
                Conditions =
                [
                    new AnyOfCondition(ImmutableArray.Create<LoadedDiceCondition>(
                        new DiceTypeRolledCondition(6),  // false against a d20 roll
                        new DiceTypeRolledCondition(20))), // true
                ],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
        }

        [TestMethod]
        public void AnyOf_EmptyChildren_NeverFires()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Empty OR",
                Conditions = [new AnyOfCondition(ImmutableArray<LoadedDiceCondition>.Empty)],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(3, rolls[0].Result);
        }

        [TestMethod]
        public void AllOf_EmptyChildren_AlwaysFires()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Empty AND",
                Conditions = [new AllOfCondition(ImmutableArray<LoadedDiceCondition>.Empty)],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
        }

        [TestMethod]
        public void Not_InvertsInner()
        {
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "NOT d6",
                Conditions = [new NotCondition(new DiceTypeRolledCondition(6))],
                Modifications = [new SetResultModification(20)],
            };
            // d20 roll: NOT(diceIs6) ⇒ true ⇒ fires.
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
        }

        [TestMethod]
        public void Not_NullInner_Matches()
        {
            // Placeholder state — freshly-added NOT with no child yet must
            // not block the rule from firing if it's the only condition.
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "NOT placeholder",
                Conditions = [new NotCondition(null)],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
        }

        [TestMethod]
        public void Nested_AllOfContainsAnyOfContainsNot_Evaluates()
        {
            // AllOf(
            //   AnyOf(diceIs6, diceIs20),   ⇒ true on a d20
            //   Not(diceIs6))               ⇒ true on a d20
            // ⇒ true overall
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Nested",
                Conditions =
                [
                    new AllOfCondition(ImmutableArray.Create<LoadedDiceCondition>(
                        new AnyOfCondition(ImmutableArray.Create<LoadedDiceCondition>(
                            new DiceTypeRolledCondition(6),
                            new DiceTypeRolledCondition(20))),
                        new NotCondition(new DiceTypeRolledCondition(6)))),
                ],
                Modifications = [new SetResultModification(20)],
            };
            var rolls = new List<DieRoll> { new(20, 3, false) };
            LoadedDiceProcessor.Apply(rolls, new[] { rule }, null, _ => ContextFor(20));
            Assert.AreEqual(20, rolls[0].Result);
        }
    }
}
