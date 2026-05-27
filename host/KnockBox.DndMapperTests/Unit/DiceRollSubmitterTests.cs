using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class DiceRollSubmitterTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build(10, 12);
        }

        // The RollLogPanel used to overwrite Config.Mode → Normal whenever the
        // dice config became invalid for Adv/Dis. That silently destroyed the
        // user's preference. The fix lives at submit time: SubmitAsync coerces
        // the *request* mode to Normal without touching the config, so the
        // user's selection survives a brief 2d6 detour.
        [TestMethod]
        public async Task SubmitAsync_CoercesAdvantageToNormal_WhenDiceNotSingleDie_AndDoesNotMutateConfigMode()
        {
            var config = new DiceRollerConfig
            {
                Terms = [new DiceTerm(2, 6)],
                Mode = RollMode.Advantage,
                Label = "fireball check",
            };

            var ok = await DiceRollSubmitter.SubmitAsync(_engine, _state, _host, config, toasts: null);

            Assert.IsTrue(ok, "Roll should succeed under coercion (Adv was invalid for 2d6 but submitter handled it).");
            Assert.AreEqual(RollMode.Advantage, config.Mode, "User's sticky Mode preference must survive the submission.");
            Assert.AreEqual(RollMode.Normal, _state.RollLog[^1].Mode, "Submitted roll's effective mode must be coerced to Normal.");
        }

        // Mirror coercion check for Disadvantage on the 2d6 path.
        [TestMethod]
        public async Task SubmitAsync_CoercesDisadvantageToNormal_WhenDiceNotSingleDie()
        {
            var config = new DiceRollerConfig
            {
                Terms = [new DiceTerm(2, 6)],
                Mode = RollMode.Disadvantage,
            };

            var ok = await DiceRollSubmitter.SubmitAsync(_engine, _state, _host, config, toasts: null);

            Assert.IsTrue(ok);
            Assert.AreEqual(RollMode.Disadvantage, config.Mode);
            Assert.AreEqual(RollMode.Normal, _state.RollLog[^1].Mode);
        }

        // Sanity: a valid 1d20 + Adv config still rolls with Advantage.
        [TestMethod]
        public async Task SubmitAsync_KeepsAdvantage_WhenDiceIsSingleDie()
        {
            var config = new DiceRollerConfig
            {
                Terms = [new DiceTerm(1, 20)],
                Mode = RollMode.Advantage,
            };

            var ok = await DiceRollSubmitter.SubmitAsync(_engine, _state, _host, config, toasts: null);

            Assert.IsTrue(ok);
            Assert.AreEqual(RollMode.Advantage, _state.RollLog[^1].Mode);
        }

        // Per-click mode override (the dock's Shift/Ctrl path) must not write
        // back into Config.Mode — that would defeat the whole point of
        // "sticky default + per-roll override".
        [TestMethod]
        public async Task SubmitAsync_WithModeOverride_DoesNotMutateConfigMode()
        {
            var config = new DiceRollerConfig
            {
                Terms = [new DiceTerm(1, 20)],
                Mode = RollMode.Normal,
            };

            var ok = await DiceRollSubmitter.SubmitAsync(_engine, _state, _host, config, toasts: null, modeOverride: RollMode.Disadvantage);

            Assert.IsTrue(ok);
            Assert.AreEqual(RollMode.Normal, config.Mode, "Sticky default must not change because of a per-click override.");
            Assert.AreEqual(RollMode.Disadvantage, _state.RollLog[^1].Mode, "Override should reach the engine for this roll.");
        }

        // SubmitRequestAsync is the path the dock's chips and the log's re-roll
        // button use. It bypasses DiceRollerConfig entirely so it must not be
        // affected by Config.Mode or its sticky state.
        [TestMethod]
        public async Task SubmitRequestAsync_RollsTheGivenRequestVerbatim()
        {
            var sheetResult = _engine.CreateSheetAsync(_state, _host, _host.Id, "Hero");
            Assert.IsTrue(sheetResult.TryGetSuccess(out var sheetId));
            _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "STR", AttributeValue.Score(14));

            var attrRef = new AttributeRef(sheetId, "STR");
            var request = new RollRequest(
                Dice: [new DiceTerm(1, 20)],
                AttributeRef: attrRef,
                FlatModifier: 0,
                Mode: RollMode.Normal,
                Label: "STR");

            var ok = await DiceRollSubmitter.SubmitRequestAsync(_engine, _state, _host, request, toasts: null);

            Assert.IsTrue(ok);
            var roll = _state.RollLog[^1];
            Assert.AreEqual("STR", roll.Label);
            Assert.AreEqual(attrRef, roll.OriginalAttributeRef);
            Assert.AreEqual(2, roll.AttributeModifier); // STR 14 → +2
        }
    }
}
