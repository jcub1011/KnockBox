using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.CardCounter.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Logic.Games.CardCounter
{
    /// <summary>
    /// Tests for Active Operator Mode rules: number cards apply directly to balance using
    /// the player's active operator, operator cards replace the active operator, and
    /// Skim/TurnTheTable/Launder/NotMyMoney are excluded from the action card pool.
    /// </summary>
    [TestClass]
    public class ActiveOperatorModeTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;
        private CardCounterGameState _state = default!;
        private CardCounterGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();

            var host = new User("Host", "host-id");
            _state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _state.Config.ActiveOperatorMode = true;
            _context = new CardCounterGameContext(_state, _randomMock.Object, _loggerMock.Object);
        }

        private PlayerState MakePlayer(string id, string name, double balance = 0, Operator activeOperator = Operator.Add)
        {
            var player = new PlayerState
            {
                PlayerId = id,
                DisplayName = name,
                Balance = balance,
                ActiveOperator = activeOperator
            };
            _state.GamePlayers[id] = player;
            _state.TurnManager.TurnOrder.Add(id);
            return player;
        }

        // ── ApplyNumberCard in Active Operator Mode ───────────────────────────

        [TestMethod]
        public void ApplyNumberCard_ActiveOperatorMode_Add_AddsToBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 100, activeOperator: Operator.Add);

            _context.ApplyNumberCard(player, new NumberCard(5));

            Assert.AreEqual(105.0, player.Balance, "Add operator should add digit to balance.");
            Assert.AreEqual(0, player.Pot.Count, "Pot should remain empty in Active Operator Mode.");
        }

        [TestMethod]
        public void ApplyNumberCard_ActiveOperatorMode_Subtract_SubtractsFromBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 100, activeOperator: Operator.Subtract);

            _context.ApplyNumberCard(player, new NumberCard(7));

            Assert.AreEqual(93.0, player.Balance);
            Assert.AreEqual(0, player.Pot.Count, "Pot should remain empty in Active Operator Mode.");
        }

        [TestMethod]
        public void ApplyNumberCard_ActiveOperatorMode_Multiply_MultipliesBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 50, activeOperator: Operator.Multiply);

            _context.ApplyNumberCard(player, new NumberCard(3));

            Assert.AreEqual(150.0, player.Balance);
        }

        [TestMethod]
        public void ApplyNumberCard_ActiveOperatorMode_Divide_DividesBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 100, activeOperator: Operator.Divide);

            _context.ApplyNumberCard(player, new NumberCard(4));

            Assert.AreEqual(25.0, player.Balance);
        }

        [TestMethod]
        public void ApplyNumberCard_ActiveOperatorMode_Multiply_RoundsResult()
        {
            var player = MakePlayer("p1", "P1", balance: 10, activeOperator: Operator.Multiply);

            _context.ApplyNumberCard(player, new NumberCard(3)); // 10 * 3 = 30

            Assert.AreEqual(30.0, player.Balance);
        }

        [TestMethod]
        public void ApplyNumberCard_ActiveOperatorMode_RecordsOperatorResult()
        {
            var player = MakePlayer("p1", "P1", balance: 50, activeOperator: Operator.Add);

            _context.ApplyNumberCard(player, new NumberCard(8));

            Assert.IsNotNull(_state.LastOperatorResult);
            Assert.AreEqual("p1", _state.LastOperatorResult.PlayerId);
            Assert.AreEqual(50.0, _state.LastOperatorResult.BalanceBefore);
            Assert.AreEqual(58.0, _state.LastOperatorResult.BalanceAfter);
            Assert.AreEqual(Operator.Add, _state.LastOperatorResult.Op);
        }

        [TestMethod]
        public void ApplyNumberCard_ActiveOperatorMode_DivideByZero_TriggersRandomEvent()
        {
            var player = MakePlayer("p1", "P1", balance: 100, activeOperator: Operator.Divide);
            player.PassesRemaining = 2;
            _randomMock.Setup(r => r.GetRandomInt(0, 4, RandomType.Secure)).Returns(0); // gain a pass

            _context.ApplyNumberCard(player, new NumberCard(0));

            Assert.AreEqual(3, player.PassesRemaining, "Division by zero should trigger random event (gain pass on roll=0).");
            Assert.AreEqual(100.0, player.Balance, "Balance should be unchanged on div/0.");
        }

        [TestMethod]
        public void ApplyNumberCard_NormalMode_StillAppendsToPot()
        {
            _state.Config.ActiveOperatorMode = false;
            var player = new PlayerState { PlayerId = "p1", DisplayName = "P1", Balance = 100 };
            player.Pot.Add(1);
            _state.GamePlayers["p1"] = player;

            _context.ApplyNumberCard(player, new NumberCard(9));

            CollectionAssert.AreEqual(new[] { 1, 9 }, player.Pot, "Normal mode should still append digit to pot.");
        }

        // ── ApplyOperatorCard in Active Operator Mode ─────────────────────────

        [TestMethod]
        public void ApplyOperatorCard_ActiveOperatorMode_SetsActiveOperator()
        {
            var player = MakePlayer("p1", "P1", balance: 100, activeOperator: Operator.Add);

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Multiply));

            Assert.AreEqual(Operator.Multiply, player.ActiveOperator, "Operator card should update ActiveOperator.");
            Assert.AreEqual(100.0, player.Balance, "Balance should not change when drawing an operator in Active Operator Mode.");
        }

        [TestMethod]
        public void ApplyOperatorCard_ActiveOperatorMode_DoesNotApplyToPot()
        {
            var player = MakePlayer("p1", "P1", balance: 50, activeOperator: Operator.Add);
            player.Pot.Add(3); // should not be consumed

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Subtract));

            Assert.AreEqual(1, player.Pot.Count, "Pot should not be consumed in Active Operator Mode.");
            Assert.AreEqual(50.0, player.Balance, "Balance should not change when drawing operator in Active Operator Mode.");
        }

        [TestMethod]
        public void ApplyOperatorCard_NormalMode_StillAppliesFromPot()
        {
            _state.Config.ActiveOperatorMode = false;
            var player = new PlayerState { PlayerId = "p1", DisplayName = "P1", Balance = 100 };
            player.Pot.AddRange([2, 5]); // pot = 25
            _state.GamePlayers["p1"] = player;

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Add));

            Assert.AreEqual(125.0, player.Balance, "Normal mode should still compute balance from pot.");
            Assert.AreEqual(0, player.Pot.Count, "Pot should be cleared after operator in normal mode.");
        }

        // ── Action card pool filtering ────────────────────────────────────────

        [TestMethod]
        public void GetRandomActionCard_ActiveOperatorMode_ExcludesSkimTurnTheTableLaunderAndNotMyMoneyFromPool()
        {
            // Verify by inspecting the pool weight: Active Operator Mode excludes Skim,
            // TurnTheTable, Launder, and NotMyMoney, so the RNG upper bound should differ
            // from the full pool.
            // We cover the behavior by checking what action types can be returned when the
            // RNG always rolls the first card in the pool.
            int capturedMax = -1;
            _randomMock.Setup(r => r.GetRandomInt(0, It.IsAny<int>(), RandomType.Secure))
                .Callback<int, int, RandomType>((_, max, _) => capturedMax = max)
                .Returns(0);

            // The returned card when roll=0 is the first in the filtered pool
            var card = _context.GetRandomActionCard();

            // Full pool has 11 types (total weight 101); filtered pool has 7 (total weight 61)
            Assert.AreEqual(61, capturedMax,
                "Active Operator Mode pool (7 cards, weight=61) should be used, not the full pool.");
            Assert.IsNotNull(card, "A card should be returned when weights are non-zero.");
            Assert.AreNotEqual(ActionType.Skim, card.Action, "Skim must not be in the pool.");
            Assert.AreNotEqual(ActionType.TurnTheTable, card.Action, "TurnTheTable must not be in the pool.");
            Assert.AreNotEqual(ActionType.Launder, card.Action, "Launder must not be in the pool.");
            Assert.AreNotEqual(ActionType.NotMyMoney, card.Action, "NotMyMoney must not be in the pool.");
        }

        [TestMethod]
        public void GetRandomActionCard_NormalMode_UsesFullPool_IncludingSkimAndTurnTheTable()
        {
            _state.Config.ActiveOperatorMode = false;
            int capturedMax = -1;
            _randomMock.Setup(r => r.GetRandomInt(0, It.IsAny<int>(), RandomType.Secure))
                .Callback<int, int, RandomType>((_, max, _) => capturedMax = max)
                .Returns(0);

            _context.GetRandomActionCard();

            // Normal mode: 11 action types, 10 with weight=10, Tilt with weight=1 → total=101
            Assert.AreEqual(101, capturedMax,
                "Normal mode pool should include all 11 action types (total weight=101).");
        }

        [TestMethod]
        public void GetRandomActionCard_ActiveOperatorMode_UsesFilteredPool()
        {
            int capturedMax = -1;
            _randomMock.Setup(r => r.GetRandomInt(0, It.IsAny<int>(), RandomType.Secure))
                .Callback<int, int, RandomType>((_, max, _) => capturedMax = max)
                .Returns(0);

            _context.GetRandomActionCard();

            // Active Operator Mode: 7 action types (Skim, TurnTheTable, Launder, NotMyMoney excluded),
            // 6 with weight=10, Tilt with weight=1 → total=61
            Assert.AreEqual(61, capturedMax,
                "Active Operator Mode pool should exclude Skim, TurnTheTable, Launder, and NotMyMoney (total weight=61).");
        }

        // ── Zero-weight card exclusion ────────────────────────────────────────

        [TestMethod]
        public void GetRandomActionCard_ZeroWeightCard_IsExcludedFromPool()
        {
            _state.Config.ActiveOperatorMode = false;
            _state.Config.TiltWeight = 0; // exclude Tilt from pool

            int capturedMax = -1;
            _randomMock.Setup(r => r.GetRandomInt(0, It.IsAny<int>(), RandomType.Secure))
                .Callback<int, int, RandomType>((_, max, _) => capturedMax = max)
                .Returns(0);

            var card = _context.GetRandomActionCard();

            // 11 types total; Tilt removed (weight=0) → 10 types × weight=10 each = total weight 100
            Assert.AreEqual(100, capturedMax,
                "Zero-weight Tilt should be excluded; total weight should be 10×10=100.");
            Assert.IsNotNull(card, "A card should still be returned.");
            Assert.AreNotEqual(ActionType.Tilt, card.Action, "Tilt must not be returned when its weight is 0.");
        }

        [TestMethod]
        public void GetRandomActionCard_AllWeightsZero_ReturnsNull()
        {
            _state.Config.ActiveOperatorMode = false;
            // Set all weights to 0
            _state.Config.FeelingLuckyWeight = 0;
            _state.Config.MakeMyLuckWeight = 0;
            _state.Config.SkimWeight = 0;
            _state.Config.BurnWeight = 0;
            _state.Config.TurnTheTableWeight = 0;
            _state.Config.CompdWeight = 0;
            _state.Config.NotMyMoneyWeight = 0;
            _state.Config.LaunderWeight = 0;
            _state.Config.TiltWeight = 0;
            _state.Config.HedgeYourBetWeight = 0;
            _state.Config.LetItRideWeight = 0;

            var card = _context.GetRandomActionCard();

            Assert.IsNull(card, "Should return null when all action card weights are 0.");
        }

        [TestMethod]
        public void DealActionCards_ZeroWeightCard_NeverDealtToPlayer()
        {
            _state.Config.ActiveOperatorMode = false;
            // Set all weights to 0 except Burn
            _state.Config.FeelingLuckyWeight = 0;
            _state.Config.MakeMyLuckWeight = 0;
            _state.Config.SkimWeight = 0;
            _state.Config.BurnWeight = 10;
            _state.Config.TurnTheTableWeight = 0;
            _state.Config.CompdWeight = 0;
            _state.Config.NotMyMoneyWeight = 0;
            _state.Config.LaunderWeight = 0;
            _state.Config.TiltWeight = 0;
            _state.Config.HedgeYourBetWeight = 0;
            _state.Config.LetItRideWeight = 0;

            _randomMock.Setup(r => r.GetRandomInt(0, It.IsAny<int>(), RandomType.Secure)).Returns(0);

            var p1 = MakePlayer("p1", "P1");
            _context.DealActionCards();

            Assert.IsTrue(p1.ActionHand.All(c => c.Action == ActionType.Burn),
                "Only Burn should appear since all other weights are 0.");
        }

        [TestMethod]
        public void DealActionCards_AllWeightsZero_NothingDealt()
        {
            _state.Config.ActiveOperatorMode = false;
            _state.Config.FeelingLuckyWeight = 0;
            _state.Config.MakeMyLuckWeight = 0;
            _state.Config.SkimWeight = 0;
            _state.Config.BurnWeight = 0;
            _state.Config.TurnTheTableWeight = 0;
            _state.Config.CompdWeight = 0;
            _state.Config.NotMyMoneyWeight = 0;
            _state.Config.LaunderWeight = 0;
            _state.Config.TiltWeight = 0;
            _state.Config.HedgeYourBetWeight = 0;
            _state.Config.LetItRideWeight = 0;

            var p1 = MakePlayer("p1", "P1");
            _context.DealActionCards();

            Assert.AreEqual(0, p1.ActionHand.Count, "No cards should be dealt when all weights are 0.");
        }

        // ── PlayerState.ActiveOperator initialization ─────────────────────────

        [TestMethod]
        public void PlayerState_ActiveOperator_DefaultsToNull()
        {
            var player = new PlayerState();
            Assert.IsNull(player.ActiveOperator, "ActiveOperator should default to null.");
        }

        // ── ReverseBalanceDigits helper ───────────────────────────────────────

        [TestMethod]
        public void ReverseBalanceDigits_PositiveNumber_ReversesDigits()
        {
            double result = CardCounterGameContext.ReverseBalanceDigits(123);
            Assert.AreEqual(321.0, result);
        }

        [TestMethod]
        public void ReverseBalanceDigits_NegativeNumber_ReversesDigitsPreservesSign()
        {
            double result = CardCounterGameContext.ReverseBalanceDigits(-42);
            Assert.AreEqual(-24.0, result);
        }

        [TestMethod]
        public void ReverseBalanceDigits_Zero_ReturnsZero()
        {
            double result = CardCounterGameContext.ReverseBalanceDigits(0);
            Assert.AreEqual(0.0, result);
        }

        [TestMethod]
        public void ReverseBalanceDigits_SingleDigit_ReturnsSame()
        {
            double result = CardCounterGameContext.ReverseBalanceDigits(7);
            Assert.AreEqual(7.0, result);
        }

        // ── WaitingForReactionState: TurnTheTable in Active Operator Mode ─────

        [TestMethod]
        public void TurnTheTable_ActiveOperatorMode_TargetAccepts_ReversesBalanceDigits()
        {
            var source = MakePlayer("src", "Source");
            var target = MakePlayer("tgt", "Target");
            target.Balance = 321;
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            Assert.AreEqual(123.0, target.Balance, "TurnTheTable in Active Operator Mode should reverse balance digits.");
        }

        [TestMethod]
        public void TurnTheTable_ActiveOperatorMode_TargetBlocks_BalanceUnchanged()
        {
            var source = MakePlayer("src", "Source");
            var target = MakePlayer("tgt", "Target");
            target.Balance = 321;
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.AreEqual(321.0, target.Balance, "Blocked TurnTheTable should not change balance.");
        }

        [TestMethod]
        public void TurnTheTable_ActiveOperatorMode_NegativeBalance_ReversesAndPreservesSign()
        {
            var source = MakePlayer("src", "Source");
            var target = MakePlayer("tgt", "Target");
            target.Balance = -42;
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            Assert.AreEqual(-24.0, target.Balance);
        }

        // ── WaitingForReactionState: Launder in Active Operator Mode ──────────

        [TestMethod]
        public void Launder_ActiveOperatorMode_TargetAccepts_SwapsBalances()
        {
            var source = MakePlayer("src", "Source");
            var target = MakePlayer("tgt", "Target");
            source.Balance = 100;
            target.Balance = -50;
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.Launder));
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            Assert.AreEqual(-50.0, source.Balance, "Source should have target's old balance.");
            Assert.AreEqual(100.0, target.Balance, "Target should have source's old balance.");
        }

        [TestMethod]
        public void Launder_ActiveOperatorMode_TargetBlocks_BalancesUnchanged()
        {
            var source = MakePlayer("src", "Source");
            var target = MakePlayer("tgt", "Target");
            source.Balance = 100;
            target.Balance = -50;
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.Launder));
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.AreEqual(100.0, source.Balance, "Source balance should be unchanged when blocked.");
            Assert.AreEqual(-50.0, target.Balance, "Target balance should be unchanged when blocked.");
        }

        // ── PlayerTurnState: TurnTheTable self-target in Active Operator Mode ──

        [TestMethod]
        public void TurnTheTable_SelfTarget_ActiveOperatorMode_ReversesOwnBalance()
        {
            var p1 = MakePlayer("p1", "Player 1");
            var p2 = MakePlayer("p2", "Player 2");
            p1.Balance = 321;
            p1.ActiveOperator = Operator.Add;
            p1.ActionHand.Add(new ActionCard(ActionType.TurnTheTable));
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context,
                new PlayActionCardCommand("p1", 0, TargetPlayerId: "p1"));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            Assert.AreEqual(123.0, p1.Balance,
                "Self-targeted TurnTheTable in Active Operator Mode should reverse own balance digits.");
        }

        // ── GameConfig defaults ───────────────────────────────────────────────

        [TestMethod]
        public void GameConfig_ActiveOperatorMode_DefaultsToFalse()
        {
            var config = new GameConfig();
            Assert.IsFalse(config.ActiveOperatorMode, "ActiveOperatorMode should default to false.");
        }
    }
}
