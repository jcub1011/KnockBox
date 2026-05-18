using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class StatusEffectVerbsTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        private Guid SeedPlayerSheet(out User player, int? hp = null, int? maxHp = null)
        {
            player = EngineTestFactory.RegisterPlayer(_state);
            var sheet = _engine.CreateSheetAsync(_state, player, player.Id, "Char");
            Assert.IsTrue(sheet.TryGetSuccess(out var id));
            _engine.UpdateSheetFreeFieldsAsync(_state, _host, id, "Char", string.Empty, hp, maxHp);
            return id;
        }

        // ── ApplyStatusEffectAsync ──

        [TestMethod]
        public void Apply_HostOnSheet_Succeeds_AndStacks()
        {
            var sheetId = SeedPlayerSheet(out _);
            var r1 = _engine.ApplyStatusEffectAsync(_state, _host, sheetId, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);
            var r2 = _engine.ApplyStatusEffectAsync(_state, _host, sheetId, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);
            Assert.IsTrue(r1.IsSuccess);
            Assert.IsTrue(r2.IsSuccess);
            Assert.AreEqual(2, _state.Sheets[sheetId].StatusEffects.Count);
        }

        [TestMethod]
        public void Apply_NonHostNonOwner_Rejected()
        {
            var sheetId = SeedPlayerSheet(out var owner);
            var other = EngineTestFactory.RegisterPlayer(_state, "Other");
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings
            {
                SheetEditByOthers = SheetEditPolicy.OwnersAndHost,
            });
            var result = _engine.ApplyStatusEffectAsync(_state, other, sheetId, "X",
                [new AttributeDelta("INT", -1)], null, null, null);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void Apply_OnApplyHpDelta_AdjustsHpOnce()
        {
            var sheetId = SeedPlayerSheet(out _, hp: 20, maxHp: 20);
            var r = _engine.ApplyStatusEffectAsync(_state, _host, sheetId, "Wounded",
                [], maxHpDelta: -5, onApplyHpDelta: -3, notes: null);
            Assert.IsTrue(r.IsSuccess);
            // Current hp: 20 - 3 = 17. Effective MaxHp: 20 - 5 = 15. Clamp to 15.
            Assert.AreEqual(15, _state.Sheets[sheetId].Hp);
        }

        [TestMethod]
        public void Apply_EmptyName_Rejected()
        {
            var sheetId = SeedPlayerSheet(out _);
            var r = _engine.ApplyStatusEffectAsync(_state, _host, sheetId, "  ", [], null, null, null);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void Apply_UnknownSheet_Rejected()
        {
            var r = _engine.ApplyStatusEffectAsync(_state, _host, Guid.NewGuid(), "X", [], null, null, null);
            Assert.IsTrue(r.IsFailure);
        }

        // ── RemoveStatusEffectAsync ──

        [TestMethod]
        public void Remove_DropsContributionAndReclampsHp()
        {
            var sheetId = SeedPlayerSheet(out _, hp: 20, maxHp: 20);
            // Wounded: MaxHpDelta=-5, OnApplyHpDelta=-3 → hp 17/15.
            var apply = _engine.ApplyStatusEffectAsync(_state, _host, sheetId, "Wounded",
                [], -5, -3, null);
            Assert.IsTrue(apply.TryGetSuccess(out var effectId));

            // After apply: 20 - 3 = 17, clamped to effective max (20 - 5) = 15.
            Assert.AreEqual(15, _state.Sheets[sheetId].Hp);

            var remove = _engine.RemoveStatusEffectAsync(_state, _host, sheetId, effectId);
            Assert.IsTrue(remove.IsSuccess);

            // OnApplyHpDelta is NOT reversed (§8.5.6); current Hp stays where it
            // was (15). Effective MaxHp returns to the base 20.
            Assert.AreEqual(15, _state.Sheets[sheetId].Hp);
            Assert.AreEqual(20, _state.Sheets[sheetId].MaxHp);
        }

        [TestMethod]
        public void Remove_UnknownEffectId_Rejected()
        {
            var sheetId = SeedPlayerSheet(out _);
            var r = _engine.RemoveStatusEffectAsync(_state, _host, sheetId, Guid.NewGuid());
            Assert.IsTrue(r.IsFailure);
        }

        // ── UpdateStatusEffectAsync ──

        [TestMethod]
        public void Update_RewritesFields_AndDoesNotRetroapplyOnApplyHpDelta()
        {
            var sheetId = SeedPlayerSheet(out _, hp: 20, maxHp: 20);
            var apply = _engine.ApplyStatusEffectAsync(_state, _host, sheetId, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);
            Assert.IsTrue(apply.TryGetSuccess(out var effectId));

            // Set OnApplyHpDelta after the fact — should NOT change current Hp.
            var update = _engine.UpdateStatusEffectAsync(_state, _host, sheetId, effectId, "Brain Fog (severe)",
                [new AttributeDelta("INT", -7)], null, -10, "worse");
            Assert.IsTrue(update.IsSuccess);

            var effect = _state.Sheets[sheetId].StatusEffects[0];
            Assert.AreEqual("Brain Fog (severe)", effect.Name);
            Assert.AreEqual(-7, effect.AttributeDeltas[0].Delta);
            Assert.AreEqual(20, _state.Sheets[sheetId].Hp); // unchanged
        }

        // ── Template verbs ──

        [TestMethod]
        public void CreateTemplate_NonHost_Rejected()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.CreateStatusEffectTemplateAsync(_state, player, "T", [], null, null, null);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void CreateUpdateDeleteTemplate_Host_Works()
        {
            var c = _engine.CreateStatusEffectTemplateAsync(_state, _host, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);
            Assert.IsTrue(c.TryGetSuccess(out var id));

            var u = _engine.UpdateStatusEffectTemplateAsync(_state, _host, id, "Brain Fog v2",
                [new AttributeDelta("INT", -3)], null, null, null);
            Assert.IsTrue(u.IsSuccess);

            var d = _engine.DeleteStatusEffectTemplateAsync(_state, _host, id);
            Assert.IsTrue(d.IsSuccess);
            Assert.AreEqual(0, _state.StatusEffectTemplates.Count);
        }

        [TestMethod]
        public void DeleteTemplate_DoesNotAffectAppliedInstances()
        {
            var sheetId = SeedPlayerSheet(out _);
            var ct = _engine.CreateStatusEffectTemplateAsync(_state, _host, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);
            Assert.IsTrue(ct.TryGetSuccess(out var templateId));

            // Apply ad-hoc (mirroring "applied from template" cloning behaviour).
            _engine.ApplyStatusEffectAsync(_state, _host, sheetId, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);

            _engine.DeleteStatusEffectTemplateAsync(_state, _host, templateId);

            Assert.AreEqual(0, _state.StatusEffectTemplates.Count);
            Assert.AreEqual(1, _state.Sheets[sheetId].StatusEffects.Count);
        }

        // ── Roll integration (covered in DiceTests too, lightweight smoke here) ──

        [TestMethod]
        public void RollWithActiveEffect_AppliesDeltaToValueAndRecomputesModifier()
        {
            // §8.5 semantics: deltas modify the attribute *value*, then the
            // scoring mode derives the modifier. For Score attributes that
            // re-floors after subtraction, so a 14 INT − 5 lands at score 9 →
            // mod −1, NOT modifier 2 − 5 = −3.
            (var engine, var state, var host, var rng) = EngineTestFactory.Build(10);
            var player = EngineTestFactory.RegisterPlayer(state);
            var sheet = engine.CreateSheetAsync(state, player, player.Id, "Char");
            Assert.IsTrue(sheet.TryGetSuccess(out var sheetId));
            engine.UpdateSheetAttributeAsync(state, host, sheetId, "INT", AttributeValue.Score(14)); // base mod +2
            engine.ApplyStatusEffectAsync(state, host, sheetId, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);

            var roll = engine.RollAsync(state, player, new RollRequest(
                Dice: [new DiceTerm(1, 20)],
                AttributeRef: new AttributeRef(sheetId, "INT"),
                FlatModifier: 0,
                Mode: RollMode.Normal,
                Label: "INT check"));

            Assert.IsTrue(roll.TryGetSuccess(out var result));
            // d20=10, score 14 - 5 = 9, mod = floor((9-10)/2) = -1, total = 10 + (-1) = 9.
            Assert.AreEqual(9, result.Total);
            Assert.AreEqual(-1, result.AttributeModifier);
            Assert.IsNotNull(result.ModifierBreakdown);
            StringAssert.Contains(result.ModifierBreakdown, "Brain Fog");
        }

        [TestMethod]
        public void RollWithActiveEffect_ModifierTypeAttribute_PassesDeltaThrough()
        {
            // Modifier-type attributes have no scoring conversion, so delta
            // arithmetic on the value matches delta arithmetic on the
            // modifier — but the path through the resolver must be the same.
            (var engine, var state, var host, _) = EngineTestFactory.Build(10);
            var player = EngineTestFactory.RegisterPlayer(state);
            engine.ChangeSchemaAsync(state, host, AttributeSchema.FromPreset(AttributePreset.SimpleD20));
            var sheet = engine.CreateSheetAsync(state, player, player.Id, "Char");
            Assert.IsTrue(sheet.TryGetSuccess(out var sheetId));
            engine.UpdateSheetAttributeAsync(state, host, sheetId, "Modifier", AttributeValue.Modifier(3));
            engine.ApplyStatusEffectAsync(state, host, sheetId, "Hexed",
                [new AttributeDelta("Modifier", -2)], null, null, null);

            var roll = engine.RollAsync(state, player, new RollRequest(
                Dice: [new DiceTerm(1, 20)],
                AttributeRef: new AttributeRef(sheetId, "Modifier"),
                FlatModifier: 0,
                Mode: RollMode.Normal,
                Label: "check"));

            Assert.IsTrue(roll.TryGetSuccess(out var result));
            // d20=10, mod 3 - 2 = 1, total = 11.
            Assert.AreEqual(11, result.Total);
            Assert.AreEqual(1, result.AttributeModifier);
        }

        [TestMethod]
        public void RollWithStackedEffects_AppliesAllDeltasToValue()
        {
            // Two stacked -5 INT effects → score 14 - 10 = 4 → mod floor((4-10)/2) = -3.
            // Naïve modifier-arithmetic would give 2 - 5 - 5 = -8 (wrong).
            (var engine, var state, var host, _) = EngineTestFactory.Build(10);
            var player = EngineTestFactory.RegisterPlayer(state);
            var sheet = engine.CreateSheetAsync(state, player, player.Id, "Char");
            Assert.IsTrue(sheet.TryGetSuccess(out var sheetId));
            engine.UpdateSheetAttributeAsync(state, host, sheetId, "INT", AttributeValue.Score(14));
            engine.ApplyStatusEffectAsync(state, host, sheetId, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);
            engine.ApplyStatusEffectAsync(state, host, sheetId, "Brain Fog",
                [new AttributeDelta("INT", -5)], null, null, null);

            var roll = engine.RollAsync(state, player, new RollRequest(
                Dice: [new DiceTerm(1, 20)],
                AttributeRef: new AttributeRef(sheetId, "INT"),
                FlatModifier: 0,
                Mode: RollMode.Normal,
                Label: "INT check"));

            Assert.IsTrue(roll.TryGetSuccess(out var result));
            Assert.AreEqual(10 - 3, result.Total);
            Assert.AreEqual(-3, result.AttributeModifier);
        }
    }
}
