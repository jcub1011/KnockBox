using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class DiceTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private SequentialRng _rng = default!;

        private void SetupRng(params int[] values)
        {
            (_engine, _state, _host, _rng) = EngineTestFactory.Build(values);
        }

        [TestInitialize]
        public void Setup()
        {
            SetupRng();
        }

        [TestMethod]
        public void RollAsync_SingleD20_ReturnsTotal()
        {
            SetupRng(17);
            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(17, roll.Total);
        }

        [TestMethod]
        public void RollAsync_2d6_PlusFlatModifier_ReturnsTotal()
        {
            SetupRng(4, 5);
            var req = new RollRequest(new[] { new DiceTerm(2, 6) }, null, 3, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(12, roll.Total);
        }

        [TestMethod]
        public void RollAsync_AttributeRefOwnSheet_AddsModifier()
        {
            SetupRng(10);
            var sheetResult = _engine.CreateSheetAsync(_state, _host, _host.Id, "Hero");
            Assert.IsTrue(sheetResult.TryGetSuccess(out var sheetId));
            _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "STR", AttributeValue.Score(14));

            var req = new RollRequest(
                new[] { new DiceTerm(1, 20) },
                new AttributeRef(sheetId, "STR"),
                0, RollMode.Normal, "STR check");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(12, roll.Total); // 10 + 2 (STR modifier)
            Assert.AreEqual(2, roll.AttributeModifier);
        }

        [TestMethod]
        public void RollAsync_AttributeRefForeignSheetByPlayer_ReturnsError()
        {
            SetupRng(10);
            var owner = EngineTestFactory.RegisterPlayer(_state);
            var other = EngineTestFactory.RegisterPlayer(_state);
            var sheetResult = _engine.CreateSheetAsync(_state, _host, owner.Id, "Hero");
            Assert.IsTrue(sheetResult.TryGetSuccess(out var sheetId));

            var req = new RollRequest(
                new[] { new DiceTerm(1, 20) },
                new AttributeRef(sheetId, "STR"),
                0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, other, req);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void RollAsync_AttributeRefForeignSheetByHost_Allowed()
        {
            SetupRng(10);
            var player = EngineTestFactory.RegisterPlayer(_state);
            var sheetResult = _engine.CreateSheetAsync(_state, _host, player.Id, "Hero");
            Assert.IsTrue(sheetResult.TryGetSuccess(out var sheetId));

            var req = new RollRequest(
                new[] { new DiceTerm(1, 20) },
                new AttributeRef(sheetId, "STR"),
                0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.IsSuccess);
        }

        [TestMethod]
        public void RollAsync_AttributeRefTextAttribute_ReturnsError()
        {
            SetupRng(10);
            var customSchema = new AttributeSchema(AttributePreset.Custom, new[]
            {
                new AttributeRow("Notes", AttributeValueType.Text, AttributeValue.Text("hi")),
            });
            _engine.ChangeSchemaAsync(_state, _host, customSchema);
            var sheetResult = _engine.CreateSheetAsync(_state, _host, _host.Id, "X");
            Assert.IsTrue(sheetResult.TryGetSuccess(out var sheetId));

            var req = new RollRequest(
                new[] { new DiceTerm(1, 20) },
                new AttributeRef(sheetId, "Notes"),
                0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void RollAsync_DieCountOverTwenty_ReturnsError()
        {
            var req = new RollRequest(new[] { new DiceTerm(21, 6) }, null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void RollAsync_AdvantageWithMultipleDieTerms_ReturnsError()
        {
            var req = new RollRequest(
                new[] { new DiceTerm(1, 20), new DiceTerm(1, 6) },
                null, 0, RollMode.Advantage, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void RollAsync_AdvantageWithSingleD20_KeepsHigher()
        {
            SetupRng(5, 17);
            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Advantage, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(17, roll.Total);
            Assert.IsTrue(roll.Rolls[0].Discarded);
            Assert.IsFalse(roll.Rolls[1].Discarded);
        }

        [TestMethod]
        public void RollAsync_DisadvantageWithSingleD20_KeepsLower()
        {
            SetupRng(17, 5);
            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Disadvantage, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(5, roll.Total);
            Assert.IsTrue(roll.Rolls[0].Discarded);
        }

        [TestMethod]
        public void RollAsync_AdvantageWithSingleD6_KeepsHigherAndRollsSecondD6()
        {
            SetupRng(2, 5);
            var req = new RollRequest(new[] { new DiceTerm(1, 6) }, null, 0, RollMode.Advantage, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.HasCount(2, roll.Rolls);
            // Second die must share the size of the configured term, not be hardcoded to d20.
            Assert.AreEqual(6, roll.Rolls[0].Sides);
            Assert.AreEqual(6, roll.Rolls[1].Sides);
            Assert.AreEqual(5, roll.Total);
            Assert.IsTrue(roll.Rolls[0].Discarded);
            Assert.IsFalse(roll.Rolls[1].Discarded);
        }

        [TestMethod]
        public void RollAsync_DisadvantageWithSingleD8_KeepsLowerAndRollsSecondD8()
        {
            SetupRng(7, 3);
            var req = new RollRequest(new[] { new DiceTerm(1, 8) }, null, 0, RollMode.Disadvantage, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(8, roll.Rolls[0].Sides);
            Assert.AreEqual(8, roll.Rolls[1].Sides);
            Assert.AreEqual(3, roll.Total);
            Assert.IsTrue(roll.Rolls[0].Discarded);
        }

        [TestMethod]
        public void RollAsync_PopulatesFormulaFromDiceTerms()
        {
            SetupRng(3, 5, 7);
            var req = new RollRequest(
                new[] { new DiceTerm(2, 6), new DiceTerm(1, 8) },
                null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            // Formula captures original request shape, not the expanded per-die rolls.
            Assert.AreEqual("2d6+1d8", roll.Formula);
        }

        [TestMethod]
        public void RollAsync_PopulatesFormulaForAdvantage_DoesNotDoubleDie()
        {
            SetupRng(5, 17);
            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Advantage, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            // Adv adds a second die internally, but the formula reflects the
            // original single-die request — the Mode field carries the ADV info.
            Assert.AreEqual("1d20", roll.Formula);
        }

        [TestMethod]
        public void RollAsync_AppendsToRollLog()
        {
            SetupRng(10);
            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Normal, "");
            _engine.RollAsync(_state, _host, req);
            Assert.HasCount(1, _state.RollLog);
        }

        [TestMethod]
        public void RollAsync_RollLogCappedAtRollLogCap_DropsOldest()
        {
            int cap = DndMapperGameState.RollLogCap;
            var values = Enumerable.Repeat(10, cap + 1).ToArray();
            SetupRng(values);
            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Normal, "");
            for (int i = 0; i < cap + 1; i++) _engine.RollAsync(_state, _host, req);
            Assert.HasCount(cap, _state.RollLog);
        }

        [TestMethod]
        public void RollAsync_ForcedByUserIdIsNullInV1()
        {
            SetupRng(10);
            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.IsNull(roll.ForcedByUserId);
        }

        [TestMethod]
        public void RollAsync_UnsupportedDieSize_ReturnsError()
        {
            var req = new RollRequest(new[] { new DiceTerm(1, 7) }, null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void RollAsync_EmptyDice_ReturnsError()
        {
            var req = new RollRequest(Array.Empty<DiceTerm>(), null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.IsFailure);
        }
    }
}
