using KnockBox.Services.Logic.Games.CardCounter.FSM;
using KnockBox.Services.Logic.Games.CardCounter.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBoxTests.Unit.Logic.Games.CardCounter
{
    [TestClass]
    public class PlayerTurnStateTests
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

        private PlayerState AddPlayer(string id, string name)
        {
            var player = new PlayerState { PlayerId = id, DisplayName = name };
            _state.GamePlayers[id] = player;
            _state.TurnOrder.Add(id);
            return player;
        }

        private void SetCurrentPlayer(int index)
        {
            _state.CurrentPlayerIndex = index;
        }

        // ── EnableActionTimer = false ─────────────────────────────────────────

        [TestMethod]
        public void Tick_WhenActionTimerEnabled_AutoDrawsAfterTimeout()
        {
            var p1 = AddPlayer("p1", "Player 1");
            AddPlayer("p2", "Player 2");
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(3)); // stays after draw
            _state.CurrentShoe.Push(new NumberCard(7)); // auto-drawn
            _state.Config.EnableActionTimer = true;

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);

            var next = fsmState.Tick(_context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsNotNull(next, "Tick should return a state transition after timeout when timer is enabled.");
            Assert.AreEqual(1, p1.Pot.Count, "Card should be auto-drawn after timeout.");
        }

        // ── DrawCard ──────────────────────────────────────────────────────────

        [TestMethod]
        public void DrawCard_NumberCard_AppendsDigitToActivePot()
        {
            var p1 = AddPlayer("p1", "Player 1");
            SetCurrentPlayer(0);

            // Stack is LIFO: push the extra card first, then the card to draw on top
            _state.CurrentShoe.Push(new NumberCard(3)); // stays in shoe after draw
            _state.CurrentShoe.Push(new NumberCard(7)); // drawn first (on top)

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.AreEqual(1, p1.Pot.Count, "One digit should be in the pot.");
            Assert.AreEqual(7, p1.Pot[0]);
        }

        [TestMethod]
        public void DrawCard_OperatorCard_AppliesOperatorToBalance()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Balance = 100;
            p1.Pot.Add(5); // pot = 5
            SetCurrentPlayer(0);

            // The top card drawn will be Add (+5 → balance becomes 105)
            _state.CurrentShoe.Push(new NumberCard(1)); // extra so shoe isn't empty after
            _state.CurrentShoe.Push(new OperatorCard(Operator.Add));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.AreEqual(105.0, p1.Balance);
            Assert.AreEqual(0, p1.Pot.Count, "Pot should be cleared after operator applied.");
        }

        [TestMethod]
        public void DrawCard_WhenShoeIsEmpty_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            SetCurrentPlayer(0);
            // No cards in shoe

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var result = fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.IsNull(result);
            Assert.AreEqual(0, p1.Pot.Count);
        }

        [TestMethod]
        public void DrawCard_WhenNotActivePlayer_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            SetCurrentPlayer(0); // p1 is active
            _state.CurrentShoe.Push(new NumberCard(4));
            _state.CurrentShoe.Push(new NumberCard(5)); // extra

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new DrawCardCommand("p2")); // p2 tries to draw

            Assert.AreEqual(0, p2.Pot.Count, "Non-active player should not be able to draw.");
            Assert.AreEqual(0, p1.Pot.Count, "p1 pot should be unaffected.");
        }

        [TestMethod]
        public void DrawCard_LastCardInShoe_TransitionsToRoundEndState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            SetCurrentPlayer(0);
            // Only one card in shoe; no cards in main deck → RoundEnd → GameOver
            _state.CurrentShoe.Push(new NumberCard(9));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(RoundEndState));
        }

        [TestMethod]
        public void DrawCard_AdvancesTurnToNextPlayer()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // extra
            _state.CurrentShoe.Push(new NumberCard(2));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.AreEqual(1, _state.CurrentPlayerIndex, "Turn should advance to p2 (index 1).");
        }

        [TestMethod]
        public void DrawCard_RecordsInDiscardHistory()
        {
            var p1 = AddPlayer("p1", "Player 1");
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // extra
            _state.CurrentShoe.Push(new NumberCard(3));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.AreEqual(1, _state.DiscardHistory.Count);
        }

        [TestMethod]
        public void DrawCard_WithNotMyMoneyInHand_EntersNotMyMoneyState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.NotMyMoney));
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // extra
            _state.CurrentShoe.Push(new OperatorCard(Operator.Add));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(NotMyMoneyState));
        }

        [TestMethod]
        public void DrawCard_NumberCard_WithNotMyMoneyInHand_DoesNotEnterNotMyMoneyState()
        {
            // Not My Money only activates for operator cards, not number cards
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.NotMyMoney));
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // extra
            _state.CurrentShoe.Push(new NumberCard(5));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.IsInstanceOfType<PlayerTurnState>(next, "Drawing a number card should transition to a fresh PlayerTurnState (resetting the timer).");
            Assert.AreEqual(1, p1.Pot.Count, "Number card digit should still be added to pot.");
        }

        // ── PlayActionCard – restrictions ─────────────────────────────────────

        [TestMethod]
        public void PlayActionCard_WhenNotActivePlayer_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            p2.ActionHand.Add(new ActionCard(ActionType.Burn));
            SetCurrentPlayer(0); // p1 active
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p2", 0));

            Assert.IsNull(next);
            Assert.AreEqual(1, p2.ActionHand.Count, "Action card should not be consumed.");
        }

        [TestMethod]
        public void PlayActionCard_CompdCannotBePlayedDirectly()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.Compd));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.IsNull(next);
            Assert.AreEqual(1, p1.ActionHand.Count, "Comp'd should remain in hand.");
        }

        [TestMethod]
        public void PlayActionCard_NotMyMoneyCannotBePlayedDirectly()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.NotMyMoney));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.IsNull(next);
            Assert.AreEqual(1, p1.ActionHand.Count, "Not My Money should remain in hand.");
        }

        [TestMethod]
        public void PlayActionCard_InvalidCardIndex_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.Burn));
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 99));

            Assert.IsNull(next);
            Assert.AreEqual(1, p1.ActionHand.Count);
        }

        // ── PlayActionCard – FeelingLucky ─────────────────────────────────────

        [TestMethod]
        public void PlayActionCard_FeelingLucky_TransitionsToFeelingLuckyChainState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            p1.ActionHand.Add(new ActionCard(ActionType.FeelingLucky));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(FeelingLuckyChainState));
        }

        [TestMethod]
        public void PlayActionCard_FeelingLucky_WithSinglePlayer_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.FeelingLucky));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.IsNull(next, "Feeling Lucky with only one player should be a no-op.");
        }

        // ── PlayActionCard – MakeMyLuck ───────────────────────────────────────

        [TestMethod]
        public void PlayActionCard_MakeMyLuck_TransitionsToMakeMyLuckState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.MakeMyLuck));
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1));
            _state.CurrentShoe.Push(new NumberCard(2));
            _state.CurrentShoe.Push(new NumberCard(3));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(MakeMyLuckState));
            Assert.IsNotNull(p1.PrivateReveal, "PrivateReveal should be set for Make My Luck.");
        }

        [TestMethod]
        public void PlayActionCard_MakeMyLuck_WhenShoeEmpty_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.MakeMyLuck));
            SetCurrentPlayer(0);
            // No cards in shoe

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.IsNull(next);
        }

        // ── PlayActionCard – Burn ─────────────────────────────────────────────

        [TestMethod]
        public void PlayActionCard_Burn_RemovesTopCardFromShoe()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.Burn));
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // extra so shoe isn't empty
            _state.CurrentShoe.Push(new NumberCard(7)); // this gets burned

            int initialCount = _state.CurrentShoe.Count;

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.AreEqual(initialCount - 1, _state.CurrentShoe.Count);
        }

        // ── PlayActionCard – TurnTheTable ─────────────────────────────────────

        [TestMethod]
        public void PlayActionCard_TurnTheTable_OnOtherPlayer_EntersWaitingForReactionState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            p1.ActionHand.Add(new ActionCard(ActionType.TurnTheTable));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0, TargetPlayerId: "p2"));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(WaitingForReactionState));
        }

        [TestMethod]
        public void PlayActionCard_TurnTheTable_OnSelf_ReversesPotImmediately()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Pot.AddRange([1, 2, 3]);
            p1.ActionHand.Add(new ActionCard(ActionType.TurnTheTable));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0, TargetPlayerId: "p1"));

            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, p1.Pot, "Self-targeted TurnTheTable should reverse pot immediately.");
        }

        [TestMethod]
        public void PlayActionCard_TurnTheTable_NoTarget_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.TurnTheTable));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            // The card is consumed but no blockable effect is applied (no target specified).
            Assert.IsNull(next);
        }

        // ── PlayActionCard – Launder ──────────────────────────────────────────

        [TestMethod]
        public void PlayActionCard_Launder_OnOtherPlayer_EntersWaitingForReactionState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            p1.ActionHand.Add(new ActionCard(ActionType.Launder));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0, TargetPlayerId: "p2"));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(WaitingForReactionState));
        }

        [TestMethod]
        public void PlayActionCard_Launder_OnSelf_IsNoOpAndAdvancesToPlayerTurnState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Pot.AddRange([1, 2, 3]);
            p1.ActionHand.Add(new ActionCard(ActionType.Launder));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0, TargetPlayerId: "p1"));

            // Self-launder is a no-op but still transitions to PlayerTurnState
            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(PlayerTurnState));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, p1.Pot, "Self-launder should not change the pot.");
        }

        // ── PlayActionCard – Skim ─────────────────────────────────────────────

        [TestMethod]
        public void PlayActionCard_Skim_BothPlayersHaveDigits_TransitionsToSkimState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            p1.Pot.Add(1);
            p2.Pot.Add(2);
            p1.ActionHand.Add(new ActionCard(ActionType.Skim));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0, TargetPlayerId: "p2"));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(SkimState));
        }

        [TestMethod]
        public void PlayActionCard_Skim_SourceHasEmptyPot_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            // p1 pot is empty
            p2.Pot.Add(5);
            p1.ActionHand.Add(new ActionCard(ActionType.Skim));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0, TargetPlayerId: "p2"));

            // Card is consumed but no SkimState transition occurs (empty source pot).
            Assert.IsNull(next);
            Assert.AreEqual(0, p1.Pot.Count, "Source pot should remain empty.");
        }

        [TestMethod]
        public void PlayActionCard_Skim_TargetHasEmptyPot_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            p1.Pot.Add(5);
            // p2 pot is empty
            p1.ActionHand.Add(new ActionCard(ActionType.Skim));
            SetCurrentPlayer(0);

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0, TargetPlayerId: "p2"));

            // Card is consumed but no SkimState transition occurs (empty target pot).
            Assert.IsNull(next);
            Assert.AreEqual(0, p2.Pot.Count, "Target pot should remain empty.");
        }

        // ── PlayActionCard – records LastPlayedAction ─────────────────────────

        [TestMethod]
        public void PlayActionCard_Burn_RecordsLastPlayedAction()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.ActionHand.Add(new ActionCard(ActionType.Burn));
            SetCurrentPlayer(0);
            _state.CurrentShoe.Push(new NumberCard(1));
            _state.CurrentShoe.Push(new NumberCard(2));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new PlayActionCardCommand("p1", 0));

            Assert.IsNotNull(_state.LastPlayedAction);
            Assert.AreEqual(ActionType.Burn, _state.LastPlayedAction.Action);
            Assert.AreEqual("p1", _state.LastPlayedAction.PlayerId);
        }

        // ── OnEnter clears pending state ──────────────────────────────────────

        [TestMethod]
        public void OnEnter_ClearsPendingReactionAndFeelingLuckyTarget()
        {
            _state.PendingReaction = new PendingReactionInfo("src", "src", "tgt", new ActionCard(ActionType.TurnTheTable));
            _state.FeelingLuckyTargetId = "some-target";
            _state.IsNotMyMoneySelecting = true;
            _state.PendingNotMyMoneyOperator = Operator.Add;

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);

            Assert.IsNull(_state.PendingReaction);
            Assert.IsNull(_state.FeelingLuckyTargetId);
            Assert.IsFalse(_state.IsNotMyMoneySelecting);
            Assert.IsNull(_state.PendingNotMyMoneyOperator);
        }

        [TestMethod]
        public void OnEnter_SetsPlayingPhase()
        {
            _state.GamePhase = GamePhase.BuyIn;
            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(_context);

            Assert.AreEqual(GamePhase.Playing, _state.GamePhase);
        }
    }
}
