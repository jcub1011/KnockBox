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
    public class CardCounterGameContextBehaviorTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();
        }

        [TestMethod]
        public void ApplyOperatorCard_EmptyPot_IsNoOp()
        {
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);
            foreach (var op in Enum.GetValues<Operator>())
            {
                var player = new PlayerState
                {
                    PlayerId = "p1",
                    DisplayName = "Player 1",
                    Balance = 42
                };

                context.ApplyOperatorCard(player, new OperatorCard(op));

                Assert.AreEqual(42.0, player.Balance);
                Assert.AreEqual(0, player.Pot.Count);
            }
        }

        [TestMethod]
        public void WaitingForReaction_CompdResponse_UpdatesLastPlayedActionToCompd()
        {
            var host = new User("Host", "host-id");
            var sourceUser = new User("Source", "source-id");
            var targetUser = new User("Target", "target-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);
            var source = new PlayerState { PlayerId = sourceUser.Id, DisplayName = sourceUser.Name };
            var target = new PlayerState { PlayerId = targetUser.Id, DisplayName = targetUser.Name };
            target.ActionHand.Add(new ActionCard(ActionType.Compd));
            state.GamePlayers[source.PlayerId] = source;
            state.GamePlayers[target.PlayerId] = target;

            var fsmState = new WaitingForReactionState(
                source.PlayerId,
                target.PlayerId,
                new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(context);

            var next = fsmState.HandleCommand(context, new PlayActionCardCommand(target.PlayerId, 0));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(PlayerTurnState));
            Assert.IsNotNull(state.LastPlayedAction);
            Assert.AreEqual(target.PlayerId, state.LastPlayedAction.PlayerId);
            Assert.AreEqual(ActionType.Compd, state.LastPlayedAction.Action);
            Assert.AreEqual(1, state.DiscardHistory.Count);
            Assert.IsTrue(state.DiscardHistory[0].IsActionCard);
            Assert.AreEqual("Comp'd", state.DiscardHistory[0].Description);
        }

        [TestMethod]
        public void RecordBurn_UsesBurnIconForDiscardTopEntry()
        {
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);

            context.RecordBurn(new NumberCard(7));

            Assert.AreEqual(1, state.DiscardHistory.Count);
            var burnEntry = state.DiscardHistory[0];
            Assert.IsTrue(burnEntry.IsActionCard);
            Assert.AreEqual("🔥", burnEntry.Symbol);
            Assert.AreEqual("# 7 (Burned)", burnEntry.Description);
        }

        [TestMethod]
        public void Burn_LastCardInShoe_TransitionsToRoundEndState()
        {
            // Arrange: one player, one card in the shoe, main deck empty
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);

            var player = new PlayerState { PlayerId = "p1", DisplayName = "Player 1" };
            player.ActionHand.Add(new ActionCard(ActionType.Burn));
            state.GamePlayers[player.PlayerId] = player;
            state.TurnOrder.Add(player.PlayerId);
            state.CurrentPlayerIndex = 0;

            // Place exactly one card in the shoe; main deck stays empty
            state.CurrentShoe.Push(new NumberCard(5));

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(context);

            // Act: play the Burn action card
            var next = fsmState.HandleCommand(context, new PlayActionCardCommand(player.PlayerId, 0));

            // Assert: shoe is empty so the game must advance to RoundEndState
            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(RoundEndState));
            Assert.AreEqual(0, state.CurrentShoe.Count);
        }

        [TestMethod]
        public void Tilt_EvenDistribution_DistributesDigitsEvenly()
        {
            // Arrange: 3 players with pots that together have 6 digits (evenly divisible by 3)
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);

            // Mock RNG to return deterministic shuffle (identity permutation: each call returns i for index i)
            int shuffleCall = 0;
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), RandomType.Secure))
                .Returns(() => shuffleCall++); // 0, 1, 2, 3, 4, 5 — identity shuffle

            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);

            var p1 = new PlayerState { PlayerId = "p1", DisplayName = "P1" };
            var p2 = new PlayerState { PlayerId = "p2", DisplayName = "P2" };
            var p3 = new PlayerState { PlayerId = "p3", DisplayName = "P3" };

            p1.Pot.AddRange([1, 2]);
            p2.Pot.AddRange([3, 4]);
            p3.Pot.AddRange([5, 6]);

            p1.ActionHand.Add(new ActionCard(ActionType.Tilt));

            state.GamePlayers[p1.PlayerId] = p1;
            state.GamePlayers[p2.PlayerId] = p2;
            state.GamePlayers[p3.PlayerId] = p3;
            state.TurnOrder.Add(p1.PlayerId);
            state.TurnOrder.Add(p2.PlayerId);
            state.TurnOrder.Add(p3.PlayerId);
            state.CurrentPlayerIndex = 0;

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(context);

            // Act
            var next = fsmState.HandleCommand(context, new PlayActionCardCommand(p1.PlayerId, 0));

            // Assert: returns null (stays in PlayerTurnState) and each player gets exactly 2 digits
            Assert.IsNull(next);
            Assert.AreEqual(2, p1.Pot.Count, "P1 should have 2 digits");
            Assert.AreEqual(2, p2.Pot.Count, "P2 should have 2 digits");
            Assert.AreEqual(2, p3.Pot.Count, "P3 should have 2 digits");
            // Total digit count preserved
            Assert.AreEqual(6, p1.Pot.Count + p2.Pot.Count + p3.Pot.Count);
        }

        [TestMethod]
        public void Tilt_UnevenDistribution_ExtraCardsGoToSourcePlayerFirst()
        {
            // Arrange: 3 players with 7 total digits (7 / 3 = 2 base, 1 extra)
            // The extra card should go to the player who played Tilt (p1 at index 0)
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);

            // Use identity shuffle so results are predictable
            int shuffleCall = 0;
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), RandomType.Secure))
                .Returns(() => shuffleCall++);

            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);

            var p1 = new PlayerState { PlayerId = "p1", DisplayName = "P1" };
            var p2 = new PlayerState { PlayerId = "p2", DisplayName = "P2" };
            var p3 = new PlayerState { PlayerId = "p3", DisplayName = "P3" };

            p1.Pot.AddRange([1, 2, 3]);
            p2.Pot.AddRange([4, 5]);
            p3.Pot.AddRange([6, 7]);

            p1.ActionHand.Add(new ActionCard(ActionType.Tilt));

            state.GamePlayers[p1.PlayerId] = p1;
            state.GamePlayers[p2.PlayerId] = p2;
            state.GamePlayers[p3.PlayerId] = p3;
            state.TurnOrder.Add(p1.PlayerId);
            state.TurnOrder.Add(p2.PlayerId);
            state.TurnOrder.Add(p3.PlayerId);
            state.CurrentPlayerIndex = 0;

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(context);

            // Act
            var next = fsmState.HandleCommand(context, new PlayActionCardCommand(p1.PlayerId, 0));

            // Assert: 7 total digits, 3 players → base 2, 1 extra → p1 gets 3, p2 gets 2, p3 gets 2
            Assert.IsNull(next);
            Assert.AreEqual(3, p1.Pot.Count, "P1 (card player) should get the extra digit");
            Assert.AreEqual(2, p2.Pot.Count);
            Assert.AreEqual(2, p3.Pot.Count);
            Assert.AreEqual(7, p1.Pot.Count + p2.Pot.Count + p3.Pot.Count);
        }

        [TestMethod]
        public void Tilt_ExtraCardsInTurnOrder_StartingFromSourcePlayer()
        {
            // Arrange: 3 players, 8 total digits (8 / 3 = 2 base, 2 extras)
            // Source player is p2 (index 1), so extras go to p2 then p3
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);

            int shuffleCall = 0;
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), RandomType.Secure))
                .Returns(() => shuffleCall++);

            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);

            var p1 = new PlayerState { PlayerId = "p1", DisplayName = "P1" };
            var p2 = new PlayerState { PlayerId = "p2", DisplayName = "P2" };
            var p3 = new PlayerState { PlayerId = "p3", DisplayName = "P3" };

            p1.Pot.AddRange([1, 2]);
            p2.Pot.AddRange([3, 4, 5]);
            p3.Pot.AddRange([6, 7, 8]);

            p2.ActionHand.Add(new ActionCard(ActionType.Tilt));

            state.GamePlayers[p1.PlayerId] = p1;
            state.GamePlayers[p2.PlayerId] = p2;
            state.GamePlayers[p3.PlayerId] = p3;
            state.TurnOrder.Add(p1.PlayerId);
            state.TurnOrder.Add(p2.PlayerId);
            state.TurnOrder.Add(p3.PlayerId);
            state.CurrentPlayerIndex = 1; // p2 is the active player

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(context);

            // Act
            var next = fsmState.HandleCommand(context, new PlayActionCardCommand(p2.PlayerId, 0));

            // Assert: 8 total, 3 players → base 2, 2 extras → p2 gets 3, p3 gets 3, p1 gets 2
            Assert.IsNull(next);
            Assert.AreEqual(2, p1.Pot.Count, "P1 should get the base amount (no extra)");
            Assert.AreEqual(3, p2.Pot.Count, "P2 (card player) gets first extra");
            Assert.AreEqual(3, p3.Pot.Count, "P3 gets second extra");
            Assert.AreEqual(8, p1.Pot.Count + p2.Pot.Count + p3.Pot.Count);
        }

        [TestMethod]
        public void Tilt_AllEmptyPots_ResultsInAllEmptyPots()
        {
            // Arrange: all players have empty pots — Tilt should be a no-op
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), RandomType.Secure)).Returns(0);

            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);

            var p1 = new PlayerState { PlayerId = "p1", DisplayName = "P1" };
            var p2 = new PlayerState { PlayerId = "p2", DisplayName = "P2" };

            p1.ActionHand.Add(new ActionCard(ActionType.Tilt));

            state.GamePlayers[p1.PlayerId] = p1;
            state.GamePlayers[p2.PlayerId] = p2;
            state.TurnOrder.Add(p1.PlayerId);
            state.TurnOrder.Add(p2.PlayerId);
            state.CurrentPlayerIndex = 0;

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(context);

            // Act
            var next = fsmState.HandleCommand(context, new PlayActionCardCommand(p1.PlayerId, 0));

            // Assert: stays in PlayerTurnState, all pots still empty
            Assert.IsNull(next);
            Assert.AreEqual(0, p1.Pot.Count);
            Assert.AreEqual(0, p2.Pot.Count);
        }

        [TestMethod]
        public void Tilt_RecordsActionCardPlay()
        {
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), RandomType.Secure)).Returns(0);

            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);

            var p1 = new PlayerState { PlayerId = "p1", DisplayName = "P1" };
            p1.Pot.AddRange([1, 2]);
            p1.ActionHand.Add(new ActionCard(ActionType.Tilt));

            state.GamePlayers[p1.PlayerId] = p1;
            state.TurnOrder.Add(p1.PlayerId);
            state.CurrentPlayerIndex = 0;

            var fsmState = new PlayerTurnState();
            fsmState.OnEnter(context);

            fsmState.HandleCommand(context, new PlayActionCardCommand(p1.PlayerId, 0));

            Assert.AreEqual(1, state.DiscardHistory.Count);
            Assert.IsTrue(state.DiscardHistory[0].IsActionCard);
            Assert.AreEqual("Tilt", state.DiscardHistory[0].Description);
        }

        [TestMethod]
        public void GetRandomActionCard_TiltIsDrawnAtExpectedRarity()
        {
            // The weighted selection draws a single roll in [0, totalWeight).
            // totalWeight = (actionTypeCount - 1) * 10 + 1  (Tilt weight=1, others weight=10).
            // Tilt is at enum index 8; the 8 cards before it each have weight 10, so Tilt
            // occupies the single roll slot equal to 8 * 10 = 80.
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);

            int totalTypes = Enum.GetValues<ActionType>().Length; // includes Tilt
            int nonTiltTypes = totalTypes - 1;
            int totalWeight = nonTiltTypes * 10 + 1;              // Tilt weight = 1

            // roll=0 → hits the first non-Tilt card
            _randomMock.Setup(r => r.GetRandomInt(0, totalWeight, RandomType.Secure)).Returns(0);
            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);
            var firstCard = context.GetRandomActionCard();
            Assert.IsNotNull(firstCard, "roll=0 must return a card");
            Assert.AreNotEqual(ActionType.Tilt, firstCard.Action, "roll=0 must not return Tilt");

            // Compute the roll that lands on Tilt: sum of weights for all cards before Tilt in enum order.
            // Tilt is at index 8, so 8 non-Tilt cards × weight 10 each = 80.
            int tiltIndex = Array.IndexOf(Enum.GetValues<ActionType>(), ActionType.Tilt);
            int tiltRoll = tiltIndex * 10; // each non-Tilt card before Tilt contributes weight 10
            _randomMock.Setup(r => r.GetRandomInt(0, totalWeight, RandomType.Secure)).Returns(tiltRoll);
            var tiltCard = context.GetRandomActionCard();
            Assert.IsNotNull(tiltCard, "tiltRoll must return a card");
            Assert.AreEqual(ActionType.Tilt, tiltCard.Action,
                $"roll={tiltRoll} must land on Tilt (index {tiltIndex} in enum)");
        }
    }
}
