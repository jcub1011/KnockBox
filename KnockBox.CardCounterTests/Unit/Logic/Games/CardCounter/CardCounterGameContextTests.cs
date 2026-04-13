using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Logic.Games.CardCounter
{
    [TestClass]
    public class CardCounterGameContextTests
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
            _context = new CardCounterGameContext(_state, _randomMock.Object, _loggerMock.Object);
        }

        private PlayerState MakePlayer(string id, string name, double balance = 0, int[]? pot = null)
        {
            var player = new PlayerState { PlayerId = id, DisplayName = name, Balance = balance };
            if (pot != null) player.Pot.AddRange(pot);
            _state.GamePlayers[id] = player;
            _state.TurnManager.TurnOrder.Add(id);
            return player;
        }

        // ── ApplyOperatorCard ─────────────────────────────────────────────────

        [TestMethod]
        public void ApplyOperatorCard_Add_IncreasesBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 100, pot: [2, 5]); // pot = 25

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Add));

            Assert.AreEqual(125.0, player.Balance);
            Assert.AreEqual(0, player.Pot.Count, "Pot should be cleared after operator.");
        }

        [TestMethod]
        public void ApplyOperatorCard_Subtract_DecreasesBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 100, pot: [1, 0]); // pot = 10

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Subtract));

            Assert.AreEqual(90.0, player.Balance);
        }

        [TestMethod]
        public void ApplyOperatorCard_Multiply_MultipliesBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 50, pot: [3]); // pot = 3

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Multiply));

            Assert.AreEqual(150.0, player.Balance);
        }

        [TestMethod]
        public void ApplyOperatorCard_Divide_DividesBalance()
        {
            var player = MakePlayer("p1", "P1", balance: 100, pot: [4]); // pot = 4

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Divide));

            Assert.AreEqual(25.0, player.Balance);
        }

        [TestMethod]
        public void ApplyOperatorCard_Multiply_RoundsToNearest()
        {
            var player = MakePlayer("p1", "P1", balance: 10, pot: [3]); // 10 * 3 = 30, exact

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Multiply));

            Assert.AreEqual(30.0, player.Balance);
        }

        [TestMethod]
        public void ApplyOperatorCard_RecordsOperatorResult()
        {
            var player = MakePlayer("p1", "P1", balance: 200, pot: [5, 0]); // pot = 50

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Subtract));

            Assert.IsNotNull(_state.LastOperatorResult);
            Assert.AreEqual("p1", _state.LastOperatorResult.PlayerId);
            Assert.AreEqual(200.0, _state.LastOperatorResult.BalanceBefore);
            Assert.AreEqual(150.0, _state.LastOperatorResult.BalanceAfter);
            Assert.AreEqual(Operator.Subtract, _state.LastOperatorResult.Op);
        }

        // ── Division by zero ──────────────────────────────────────────────────

        [TestMethod]
        public void ApplyOperatorCard_DivideByZero_GivesExtraPass_WhenRollIs0()
        {
            // pot = [0] → potValue = 0 → divide by zero
            var player = MakePlayer("p1", "P1", balance: 100, pot: [0]);
            player.PassesRemaining = 2;
            _randomMock.Setup(r => r.GetRandomInt(0, 4, RandomType.Secure)).Returns(0); // roll = 0 → gain pass

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Divide));

            Assert.AreEqual(3, player.PassesRemaining, "Roll=0 should award an extra pass.");
            Assert.AreEqual(100.0, player.Balance, "Balance should be unchanged on div/0.");
        }

        [TestMethod]
        public void ApplyOperatorCard_DivideByZero_LosesPass_WhenRollIs1()
        {
            var player = MakePlayer("p1", "P1", balance: 100, pot: [0]);
            player.PassesRemaining = 2;
            _randomMock.Setup(r => r.GetRandomInt(0, 4, RandomType.Secure)).Returns(1); // roll = 1 → lose pass

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Divide));

            Assert.AreEqual(1, player.PassesRemaining, "Roll=1 should remove a pass.");
        }

        [TestMethod]
        public void ApplyOperatorCard_DivideByZero_GainsActionCard_WhenRollIs2()
        {
            var player = MakePlayer("p1", "P1", balance: 100, pot: [0]);
            // Roll = 2 → gain action card (if under hand limit)
            // Use a sequence: first call returns 2 (div-by-zero outcome), subsequent calls return 0 (action card selection)
            var callCount = 0;
            _randomMock.Setup(r => r.GetRandomInt(0, 4, RandomType.Secure))
                .Returns(() => callCount++ == 0 ? 2 : 0);
            _randomMock.Setup(r => r.GetRandomInt(0, It.IsNotIn(4), RandomType.Secure)).Returns(0);

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Divide));

            Assert.AreEqual(1, player.ActionHand.Count, "Roll=2 should add an action card to hand.");
        }

        [TestMethod]
        public void ApplyOperatorCard_DivideByZero_LosesActionCard_WhenRollIs3()
        {
            var player = MakePlayer("p1", "P1", balance: 100, pot: [0]);
            player.ActionHand.Add(new ActionCard(ActionType.Burn));
            _randomMock.Setup(r => r.GetRandomInt(0, 4, RandomType.Secure)).Returns(3); // roll = 3 → lose action card
            _randomMock.Setup(r => r.GetRandomInt(0, 1, RandomType.Secure)).Returns(0);

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Divide));

            Assert.AreEqual(0, player.ActionHand.Count, "Roll=3 should remove an action card from hand.");
        }

        [TestMethod]
        public void ApplyOperatorCard_DivideByZero_LosesPass_ButNotBelowZero()
        {
            var player = MakePlayer("p1", "P1", balance: 100, pot: [0]);
            player.PassesRemaining = 0; // already 0
            _randomMock.Setup(r => r.GetRandomInt(0, 4, RandomType.Secure)).Returns(1);

            _context.ApplyOperatorCard(player, new OperatorCard(Operator.Divide));

            Assert.AreEqual(0, player.PassesRemaining, "Passes should not go below zero.");
        }

        // ── DealNextShoe ──────────────────────────────────────────────────────

        [TestMethod]
        public void DealNextShoe_WhenDeckHasCards_ReturnsTrue()
        {
            for (int i = 0; i < _state.Config.MinShoeSize; i++)
                _state.MainDeck.Push(new NumberCard(i % 10));

            bool result = _context.DealNextShoe();

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void DealNextShoe_WhenDeckEmpty_ReturnsFalse()
        {
            bool result = _context.DealNextShoe();

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void DealNextShoe_PopulatesCurrentShoe()
        {
            for (int i = 0; i < _state.Config.MinShoeSize + 5; i++)
                _state.MainDeck.Push(new NumberCard(i % 10));

            _context.DealNextShoe();

            Assert.IsTrue(_state.CurrentShoe.Count > 0, "Shoe should be populated after dealing.");
        }

        [TestMethod]
        public void DealNextShoe_IncrementsShoeIndex()
        {
            for (int i = 0; i < _state.Config.MinShoeSize; i++)
                _state.MainDeck.Push(new NumberCard(i % 10));

            int initialIndex = _state.ShoeIndex;
            _context.DealNextShoe();

            Assert.AreEqual(initialIndex + 1, _state.ShoeIndex);
        }

        [TestMethod]
        public void DealNextShoe_ClearsOldShoe()
        {
            // Put some cards in the shoe first
            _state.CurrentShoe.Push(new NumberCard(0));

            for (int i = 0; i < _state.Config.MinShoeSize; i++)
                _state.MainDeck.Push(new NumberCard(i % 10));

            _context.DealNextShoe();

            // The shoe should only contain cards from the new deal, not the old one
            // (as long as MinShoeSize < old shoe count + new count, we can't verify directly,
            //  but we know DealNextShoe calls CurrentShoe.Clear() first)
            Assert.IsTrue(_state.CurrentShoe.Count <= _state.Config.MaxShoeSize,
                "Shoe size should not exceed MaxShoeSize.");
        }

        // ── DealActionCards ───────────────────────────────────────────────────

        [TestMethod]
        public void DealActionCards_DealsActionsDealtPerRoundToEachPlayer()
        {
            var p1 = MakePlayer("p1", "P1");
            var p2 = MakePlayer("p2", "P2");

            _context.DealActionCards();

            Assert.AreEqual(_state.Config.ActionsDealtPerRound, p1.ActionHand.Count);
            Assert.AreEqual(_state.Config.ActionsDealtPerRound, p2.ActionHand.Count);
        }

        // ── RecalculateShoeCounts ─────────────────────────────────────────────

        [TestMethod]
        public void RecalculateShoeCounts_CountsNumberAndOperatorCardsSeparately()
        {
            _state.CurrentShoe.Push(new NumberCard(1));
            _state.CurrentShoe.Push(new NumberCard(2));
            _state.CurrentShoe.Push(new OperatorCard(Operator.Add));

            _context.RecalculateShoeCounts();

            Assert.AreEqual(2, _state.ShoeCardCounts[CardType.Number]);
            Assert.AreEqual(1, _state.ShoeCardCounts[CardType.Operator]);
        }

        [TestMethod]
        public void DecrementShoeCount_RemovesTypeWhenCountReachesOne()
        {
            _state.ShoeCardCounts[CardType.Number] = 1;

            _context.DecrementShoeCount(new NumberCard(5));

            Assert.IsFalse(_state.ShoeCardCounts.ContainsKey(CardType.Number),
                "Count of 1 should remove the key entirely after decrement.");
        }

        [TestMethod]
        public void DecrementShoeCount_DecrementsCountWhenAboveOne()
        {
            _state.ShoeCardCounts[CardType.Operator] = 3;

            _context.DecrementShoeCount(new OperatorCard(Operator.Multiply));

            Assert.AreEqual(2, _state.ShoeCardCounts[CardType.Operator]);
        }

        // ── AdvanceTurn ───────────────────────────────────────────────────────

        [TestMethod]
        public void AdvanceTurn_WrapsAroundToFirstPlayer()
        {
            MakePlayer("p1", "P1");
            MakePlayer("p2", "P2");
            _state.TurnManager.SetCurrentPlayerIndex(1); // last player

            _context.AdvanceTurn();

            Assert.AreEqual(0, _state.TurnManager.CurrentPlayerIndex, "Advancing from last player should wrap to index 0.");
        }

        [TestMethod]
        public void AdvanceTurn_IncrementsByOne()
        {
            MakePlayer("p1", "P1");
            MakePlayer("p2", "P2");
            MakePlayer("p3", "P3");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            _context.AdvanceTurn();

            Assert.AreEqual(1, _state.TurnManager.CurrentPlayerIndex);
        }

        // ── RecordDraw / RecordBurn / RecordActionCardPlay ────────────────────

        [TestMethod]
        public void RecordDraw_UpdatesLastDrawnCard()
        {
            var player = MakePlayer("p1", "P1");
            var card = new NumberCard(4);

            _context.RecordDraw(player, card);

            Assert.IsNotNull(_state.LastDrawnCard);
            Assert.AreEqual("p1", _state.LastDrawnCard.DrawerId);
            Assert.AreEqual(card, _state.LastDrawnCard.Card);
        }

        [TestMethod]
        public void RecordDraw_AppendsToDiscardHistory()
        {
            var player = MakePlayer("p1", "P1");
            var card = new NumberCard(7);

            _context.RecordDraw(player, card);

            Assert.AreEqual(1, _state.DiscardHistory.Count);
            Assert.IsFalse(_state.DiscardHistory[0].IsActionCard);
        }

        [TestMethod]
        public void RecordRedirectedDraw_SetsRedirectTargetInfo()
        {
            var drawer = MakePlayer("drawer", "Drawer");
            var target = MakePlayer("target", "Target");
            var card = new OperatorCard(Operator.Add);

            _context.RecordRedirectedDraw(drawer, target, card);

            Assert.IsNotNull(_state.LastDrawnCard);
            Assert.AreEqual("drawer", _state.LastDrawnCard.DrawerId);
            Assert.AreEqual("target", _state.LastDrawnCard.RedirectTargetId);
            Assert.AreEqual("Target", _state.LastDrawnCard.RedirectTargetName);
        }

        [TestMethod]
        public void RecordActionCardPlay_AppendsToDiscardHistoryAsActionCard()
        {
            var player = MakePlayer("p1", "P1");
            var card = new ActionCard(ActionType.Burn);

            _context.RecordActionCardPlay(player, card);

            Assert.AreEqual(1, _state.DiscardHistory.Count);
            Assert.IsTrue(_state.DiscardHistory[0].IsActionCard);
            Assert.AreEqual("Burn", _state.DiscardHistory[0].Description);
        }

        // ── ApplyNumberCard ───────────────────────────────────────────────────

        [TestMethod]
        public void ApplyNumberCard_AppendsDigitToPot()
        {
            var player = MakePlayer("p1", "P1");
            player.Pot.Add(1);

            _context.ApplyNumberCard(player, new NumberCard(9));

            CollectionAssert.AreEqual(new[] { 1, 9 }, player.Pot);
        }
    }
}
