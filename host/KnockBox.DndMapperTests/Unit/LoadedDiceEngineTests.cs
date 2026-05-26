using System.Collections.Immutable;
using System.Text.Json;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class LoadedDiceEngineTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private SequentialRng _rng = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _rng) = EngineTestFactory.Build();
        }

        private void EnableLoadedDice()
        {
            var r = _engine.UpdateSettingsAsync(_state, _host, _state.Settings with { LoadedDiceEnabled = true });
            Assert.IsTrue(r.IsSuccess);
        }

        private static LoadedDiceRule SetResultRule(string name, int sides, int value) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Conditions = [new DiceTypeRolledCondition(sides)],
            Modifications = [new SetResultModification(value)],
        };

        [TestMethod]
        public void AddLoadedDiceRule_RequiresHost()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.AddLoadedDiceRuleAsync(_state, player, new LoadedDiceRule { Name = "x" });
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void AddLoadedDiceRule_AssignsIdWhenEmpty()
        {
            var result = _engine.AddLoadedDiceRuleAsync(_state, _host, new LoadedDiceRule { Name = "x" });
            Assert.IsTrue(result.TryGetSuccess(out var id));
            Assert.AreNotEqual(Guid.Empty, id);
            Assert.AreEqual(1, _state.LoadedDiceRules.Count);
        }

        [TestMethod]
        public void RollAsync_LoadedDiceDisabled_NoMutation()
        {
            // Add rule, but leave master toggle off — rule is dormant.
            _engine.AddLoadedDiceRuleAsync(_state, _host, SetResultRule("Crit", 20, 20));

            _rng.Enqueue(7);
            var req = new RollRequest([new DiceTerm(1, 20)], null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(7, roll.Total);
            Assert.AreEqual(0, roll.AppliedRules.Count);
        }

        [TestMethod]
        public void RollAsync_LoadedDiceEnabled_RuleFires_StampsApplied()
        {
            EnableLoadedDice();
            _engine.AddLoadedDiceRuleAsync(_state, _host, SetResultRule("Crit", 20, 20));

            _rng.Enqueue(7);
            var req = new RollRequest([new DiceTerm(1, 20)], null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(20, roll.Total);
            Assert.AreEqual(1, roll.AppliedRules.Count);
            Assert.AreEqual("Crit", roll.AppliedRules[0].RuleName);
        }

        [TestMethod]
        public void RollAsync_RuleOnlyMatchesD6_LeavesD20Alone()
        {
            EnableLoadedDice();
            _engine.AddLoadedDiceRuleAsync(_state, _host, SetResultRule("d6 to 6", 6, 6));

            _rng.Enqueue(11);
            var req = new RollRequest([new DiceTerm(1, 20)], null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(11, roll.Total);
            Assert.AreEqual(0, roll.AppliedRules.Count);
        }

        [TestMethod]
        public void RollAsync_AdvantageWithSetToTwenty_KeepsTwenty()
        {
            EnableLoadedDice();
            _engine.AddLoadedDiceRuleAsync(_state, _host, SetResultRule("Crit", 20, 20));

            // Both raw rolls become 20 after rule; discard step picks the higher
            // (a tie — first stays kept). Total = 20.
            _rng.Enqueue(3);
            _rng.Enqueue(15);
            var req = new RollRequest([new DiceTerm(1, 20)], null, 0, RollMode.Advantage, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(20, roll.Total);
        }

        [TestMethod]
        public void UpdateHostInputState_NonHostIsIgnoredSilently()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            // Non-host attempt — verb succeeds (no-op) but the state isn't mutated.
            var r = _engine.UpdateHostInputStateAsync(_state, player, new[] { "Space" });
            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual(0, _state.HostHeldKeys.Count);
        }

        [TestMethod]
        public void UpdateHostInputState_HostSnapshotIsLive_RuleFiresOnSpace()
        {
            EnableLoadedDice();
            _engine.AddLoadedDiceRuleAsync(_state, _host, new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Space=1",
                Conditions = [new HostKeyHeldCondition(" ")],
                Modifications = [new SetResultModification(1)],
            });
            // Use literal space character (some platforms report " " for the spacebar).
            _engine.UpdateHostInputStateAsync(_state, _host, new[] { " " });

            _rng.Enqueue(14);
            var req = new RollRequest([new DiceTerm(1, 20)], null, 0, RollMode.Normal, "");
            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(1, roll.Total);
        }

        [TestMethod]
        public void RemoveLoadedDiceRule_DropsIt()
        {
            var added = _engine.AddLoadedDiceRuleAsync(_state, _host, SetResultRule("X", 20, 20));
            Assert.IsTrue(added.TryGetSuccess(out var id));
            var removed = _engine.RemoveLoadedDiceRuleAsync(_state, _host, id);
            Assert.IsTrue(removed.IsSuccess);
            Assert.AreEqual(0, _state.LoadedDiceRules.Count);
        }

        [TestMethod]
        public void MoveLoadedDiceRule_ReordersList()
        {
            var a = _engine.AddLoadedDiceRuleAsync(_state, _host, SetResultRule("A", 20, 20));
            var b = _engine.AddLoadedDiceRuleAsync(_state, _host, SetResultRule("B", 20, 1));
            Assert.IsTrue(a.TryGetSuccess(out var idA));
            Assert.IsTrue(b.TryGetSuccess(out _));
            _engine.MoveLoadedDiceRuleAsync(_state, _host, idA, 1);
            Assert.AreEqual("B", _state.LoadedDiceRules[0].Name);
            Assert.AreEqual("A", _state.LoadedDiceRules[1].Name);
        }

        [TestMethod]
        public void RollAsync_AttributeRefWithNullName_DrivesRuleMatching_NoModifierApplied()
        {
            // Host rolls "as Alice" via the picker but doesn't select an
            // attribute — AttributeRef carries the sheet with a null name.
            // A rule targeted at Alice's sheet must still fire; no
            // attribute modifier should be added to the total.
            EnableLoadedDice();
            var aliceId = _engine.CreateSheetAsync(_state, _host, _host.Id, "Alice").TryGetSuccess(out var sId) ? sId : Guid.Empty;
            Assert.AreNotEqual(Guid.Empty, aliceId);

            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Alice crits",
                TargetSheetIds = System.Collections.Immutable.ImmutableHashSet.Create(aliceId),
                Modifications = [new SetResultModification(20)],
            };
            _engine.AddLoadedDiceRuleAsync(_state, _host, rule);

            _rng.Enqueue(4);
            var req = new RollRequest(
                Dice: [new DiceTerm(1, 20)],
                AttributeRef: new AttributeRef(aliceId, null),
                FlatModifier: 0,
                Mode: RollMode.Normal,
                Label: "");

            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            Assert.AreEqual(20, roll.Total);
            Assert.IsNull(roll.AttributeModifier);
            Assert.AreEqual(1, roll.AppliedRules.Count);
        }

        [TestMethod]
        public void RollAsync_AttributeRefWithName_AppliesModifierAndMatchesRules()
        {
            // With both sheet and attribute set, the engine resolves the
            // modifier AND the rule processor sees the sheet — single
            // value, one source of truth.
            EnableLoadedDice();
            var aliceId = _engine.CreateSheetAsync(_state, _host, _host.Id, "Alice").TryGetSuccess(out var a) ? a : Guid.Empty;
            _engine.UpdateSheetAttributeAsync(_state, _host, aliceId, "STR", AttributeValue.Score(14));

            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Alice crits",
                TargetSheetIds = System.Collections.Immutable.ImmutableHashSet.Create(aliceId),
                Modifications = [new SetResultModification(20)],
            };
            _engine.AddLoadedDiceRuleAsync(_state, _host, rule);

            _rng.Enqueue(4);
            var req = new RollRequest(
                Dice: [new DiceTerm(1, 20)],
                AttributeRef: new AttributeRef(aliceId, "STR"),
                FlatModifier: 0,
                Mode: RollMode.Normal,
                Label: "");

            var result = _engine.RollAsync(_state, _host, req);
            Assert.IsTrue(result.TryGetSuccess(out var roll));
            // d20 set to 20 by the rule, plus +2 STR modifier from Alice.
            Assert.AreEqual(22, roll.Total);
            Assert.AreEqual(2, roll.AttributeModifier);
        }

        [TestMethod]
        public void LoadedDiceRule_RoundTripsThroughJson()
        {
            // Polymorphic Conditions and Modifications must survive a save/load
            // cycle so persisted rules behave the same after a host reload.
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = "Mixed",
                TargetSheetIds = ImmutableHashSet.Create(Guid.NewGuid()),
                Conditions =
                [
                    new DiceTypeRolledCondition(20),
                    new RollModeIsCondition(RollMode.Advantage),
                    new HostKeyHeldCondition("Space"),
                    new CombatActiveCondition(),
                ],
                Modifications =
                [
                    new SetResultModification(15),
                    new ClampMaxModification(18),
                    new BiasLowerModification(2),
                    new RerollOnModification(ImmutableHashSet.Create(1, 2)),
                ],
            };

            var json = JsonSerializer.Serialize(rule);
            var roundtrip = JsonSerializer.Deserialize<LoadedDiceRule>(json)!;

            Assert.AreEqual(rule.Id, roundtrip.Id);
            Assert.AreEqual(rule.Name, roundtrip.Name);
            Assert.AreEqual(4, roundtrip.Conditions.Count);
            Assert.IsInstanceOfType(roundtrip.Conditions[0], typeof(DiceTypeRolledCondition));
            Assert.IsInstanceOfType(roundtrip.Conditions[2], typeof(HostKeyHeldCondition));
            Assert.AreEqual("Space", ((HostKeyHeldCondition)roundtrip.Conditions[2]).Key);
            Assert.AreEqual(4, roundtrip.Modifications.Count);
            Assert.IsInstanceOfType(roundtrip.Modifications[3], typeof(RerollOnModification));
            CollectionAssert.AreEquivalent(
                new[] { 1, 2 },
                ((RerollOnModification)roundtrip.Modifications[3]).Values.ToArray());
        }
    }
}
