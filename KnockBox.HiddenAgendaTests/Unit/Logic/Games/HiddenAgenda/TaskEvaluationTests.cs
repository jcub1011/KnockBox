using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgenda.Tests.Unit.Logic
{
    [TestClass]
    public class TaskEvaluationTests
    {
        private Mock<IRandomNumberService> _rngMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLoggerMock = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;
        private string _playerId = "p1";

        [TestInitialize]
        public void Setup()
        {
            _rngMock = new Mock<IRandomNumberService>();
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<HiddenAgendaGameState>>();

            var host = new User("Host", "host1");
            _state = new HiddenAgendaGameState(host, _stateLoggerMock.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();

            _state.GamePlayers[_playerId] = new HiddenAgendaPlayerState { PlayerId = _playerId };

            _context = new HiddenAgendaGameContext(_state, _rngMock.Object, _loggerMock.Object);
        }

        private SecretTask GetTask(string id) => TaskPool.AllTasks.First(t => t.Id == id);

        [TestMethod]
        public void EvaluateDevotion_D1_ReturnsTrue_IfThresholdMet()
        {
            var task = GetTask("D1");
            var player = _state.GamePlayers[_playerId];

            // 4 separate turns adding to RenaissanceMasters
            for (int i = 1; i <= 4; i++)
            {
                player.CardPlayHistory.Add(new CardPlayRecord(
                    i,
                    new CurationCard(CurationCardType.Acquire, "", []),
                    0,
                    [CollectionType.RenaissanceMasters],
                    CurationCardType.Acquire,
                    CurationCardType.Acquire
                ));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateDevotion_D1_ReturnsFalse_IfBelowThreshold()
        {
            var task = GetTask("D1");
            var player = _state.GamePlayers[_playerId];

            // 3 separate turns
            for (int i = 1; i <= 3; i++)
            {
                player.CardPlayHistory.Add(new CardPlayRecord(
                    i,
                    new CurationCard(CurationCardType.Acquire, "", []),
                    0,
                    [CollectionType.RenaissanceMasters],
                    CurationCardType.Acquire,
                    CurationCardType.Acquire
                ));
            }

            Assert.IsFalse(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateNeglect_N1_ReturnsTrue_IfNeverPlayed()
        {
            var task = GetTask("N1");
            var player = _state.GamePlayers[_playerId];

            player.CardPlayHistory.Add(new CardPlayRecord(
                1,
                new CurationCard(CurationCardType.Acquire, "", []),
                0,
                [CollectionType.ContemporaryShowcase],
                CurationCardType.Acquire,
                CurationCardType.Acquire
            ));

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateNeglect_N1_ReturnsFalse_IfPlayed()
        {
            var task = GetTask("N1");
            var player = _state.GamePlayers[_playerId];

            player.CardPlayHistory.Add(new CardPlayRecord(
                1,
                new CurationCard(CurationCardType.Acquire, "", []),
                0,
                [CollectionType.RenaissanceMasters],
                CurationCardType.Acquire,
                CurationCardType.Acquire
            ));

            Assert.IsFalse(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateStyle_Y1_ReturnsTrue_IfThresholdMet()
        {
            var task = GetTask("Y1");
            var player = _state.GamePlayers[_playerId];

            for (int i = 1; i <= 3; i++)
            {
                player.CardPlayHistory.Add(new CardPlayRecord(
                    i,
                    new CurationCard(CurationCardType.Remove, "", []),
                    0,
                    [CollectionType.RenaissanceMasters],
                    CurationCardType.Remove,
                    CurationCardType.Remove
                ));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateMovement_M1_ReturnsTrue_IfAllWingsVisited()
        {
            var task = GetTask("M1");
            var player = _state.GamePlayers[_playerId];

            player.MovementHistory.Add(new MovementRecord(1, 1, Wing.GrandHall, 3));
            player.MovementHistory.Add(new MovementRecord(2, 6, Wing.ModernWing, 3));
            player.MovementHistory.Add(new MovementRecord(3, 11, Wing.SculptureGarden, 3));
            player.MovementHistory.Add(new MovementRecord(4, 16, Wing.RestorationRoom, 3));

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateRivalry_R4_ReturnsTrue_IfHighestRemoveMet()
        {
            var task = GetTask("R4");
            var player = _state.GamePlayers[_playerId];

            var snapshot = new Dictionary<CollectionType, int>
            {
                { CollectionType.RenaissanceMasters, 10 },
                { CollectionType.ContemporaryShowcase, 5 }
            };

            for (int i = 1; i <= 3; i++)
            {
                player.CardPlayHistory.Add(new CardPlayRecord(
                    i,
                    new CurationCard(CurationCardType.Remove, "", []),
                    0,
                    [CollectionType.RenaissanceMasters],
                    CurationCardType.Remove,
                    CurationCardType.Remove,
                    snapshot
                ));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateStyle_Y3_ReturnsTrue_IfConsecutiveCollectionMet()
        {
            var task = GetTask("Y3");
            var player = _state.GamePlayers[_playerId];

            // Turn 1: RM
            // Turn 2: RM
            // Turn 3: RM
            for (int i = 1; i <= 3; i++)
            {
                player.CardPlayHistory.Add(new CardPlayRecord(
                    i,
                    new CurationCard(CurationCardType.Acquire, "", []),
                    0,
                    [CollectionType.RenaissanceMasters],
                    CurationCardType.Acquire,
                    CurationCardType.Acquire
                ));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateStyle_Y4_ReturnsTrue_IfAlternatingMet()
        {
            var task = GetTask("Y4");
            var player = _state.GamePlayers[_playerId];

            // A, R, A, R
            for (int i = 1; i <= 4; i++)
            {
                var type = i % 2 == 1 ? CurationCardType.Acquire : CurationCardType.Remove;
                player.CardPlayHistory.Add(new CardPlayRecord(
                    i,
                    new CurationCard(type, "", []),
                    0,
                    [CollectionType.RenaissanceMasters],
                    type,
                    type
                ));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateStyle_Y5_ReturnsTrue_IfHighestValueMet()
        {
            var task = GetTask("Y5");
            var player = _state.GamePlayers[_playerId];

            var highCard = new CurationCard(CurationCardType.Acquire, "High", [new(CollectionType.RenaissanceMasters, 3)]);
            var lowCard = new CurationCard(CurationCardType.Acquire, "Low", [new(CollectionType.RenaissanceMasters, 1)]);

            for (int i = 1; i <= 4; i++)
            {
                player.CardDrawHistory.Add(new CardDrawRecord(i, [highCard, lowCard, lowCard]));
                player.CardPlayHistory.Add(new CardPlayRecord(
                    i,
                    highCard,
                    0,
                    [CollectionType.RenaissanceMasters],
                    CurationCardType.Acquire,
                    CurationCardType.Acquire
                ));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateMovement_M3_ReturnsTrue_IfSameSpotMet()
        {
            var task = GetTask("M3");
            var player = _state.GamePlayers[_playerId];
            string otherId = "p2";
            _state.GamePlayers[otherId] = new HiddenAgendaPlayerState { PlayerId = otherId };

            // Turn 1: Other moves to space 1
            _state.RoundPlayHistory.Add(new TurnRecord(1, otherId, null, 1, Wing.GrandHall));
            // Turn 2: I move to space 1
            player.MovementHistory.Add(new MovementRecord(1, 1, Wing.GrandHall, 3));
            _state.RoundPlayHistory.Add(new TurnRecord(2, _playerId, null, 1, Wing.GrandHall));

            // Turn 3: Other moves to space 2
            _state.RoundPlayHistory.Add(new TurnRecord(3, otherId, null, 2, Wing.GrandHall));
            // Turn 4: I move to space 2
            player.MovementHistory.Add(new MovementRecord(2, 2, Wing.GrandHall, 3));
            _state.RoundPlayHistory.Add(new TurnRecord(4, _playerId, null, 2, Wing.GrandHall));

            // Turn 5: Other moves to space 3
            _state.RoundPlayHistory.Add(new TurnRecord(5, otherId, null, 3, Wing.GrandHall));
            // Turn 6: I move to space 3
            player.MovementHistory.Add(new MovementRecord(3, 3, Wing.GrandHall, 3));
            _state.RoundPlayHistory.Add(new TurnRecord(6, _playerId, null, 3, Wing.GrandHall));

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateMovement_M4_ReturnsTrue_IfFullDistanceMet()
        {
            var task = GetTask("M4");
            var player = _state.GamePlayers[_playerId];

            // Space 0 -> 3 (dist 3, spin 3)
            // Space 3 -> 6 (dist 3, spin 3)
            // Space 6 -> 9 (dist 3, spin 3)
            // Space 9 -> 12 (dist 3, spin 3)
            player.MovementHistory.Add(new MovementRecord(1, 0, Wing.GrandHall, 3));
            player.MovementHistory.Add(new MovementRecord(2, 3, Wing.GrandHall, 3));
            player.MovementHistory.Add(new MovementRecord(3, 6, Wing.ModernWing, 3));
            player.MovementHistory.Add(new MovementRecord(4, 9, Wing.ModernWing, 3));
            player.MovementHistory.Add(new MovementRecord(5, 12, Wing.SculptureGarden, 3));

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateRivalry_R1_ReturnsTrue_IfRescueMet()
        {
            var task = GetTask("R1");
            var player = _state.GamePlayers[_playerId];
            string otherId = "p2";
            _state.GamePlayers[otherId] = new HiddenAgendaPlayerState { PlayerId = otherId };

            for (int i = 1; i <= 3; i++)
            {
                int turnBase = (i - 1) * 2 + 1;
                var removeRecord = new CardPlayRecord(i, new CurationCard(CurationCardType.Remove, "", []), 0, [CollectionType.RenaissanceMasters], CurationCardType.Remove, CurationCardType.Remove);
                _state.RoundPlayHistory.Add(new TurnRecord(turnBase, otherId, removeRecord, 1, Wing.GrandHall));

                var acquireRecord = new CardPlayRecord(i, new CurationCard(CurationCardType.Acquire, "", []), 0, [CollectionType.RenaissanceMasters], CurationCardType.Acquire, CurationCardType.Acquire);
                player.CardPlayHistory.Add(acquireRecord);
                _state.RoundPlayHistory.Add(new TurnRecord(turnBase + 1, _playerId, acquireRecord, 1, Wing.GrandHall));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void EvaluateRivalry_R6_ReturnsTrue_IfShadowMet()
        {
            var task = GetTask("R6");
            var player = _state.GamePlayers[_playerId];
            string otherId = "p2";
            player.RivalryTargetPlayerId = otherId;
            _state.GamePlayers[otherId] = new HiddenAgendaPlayerState { PlayerId = otherId };

            for (int i = 1; i <= 4; i++)
            {
                int turnBase = (i - 1) * 2 + 1;
                // Other player moves to GrandHall
                _state.RoundPlayHistory.Add(new TurnRecord(turnBase, otherId, null, 1, Wing.GrandHall));
                
                // This player moves to GrandHall
                player.MovementHistory.Add(new MovementRecord(i, 2, Wing.GrandHall, 3));
                _state.RoundPlayHistory.Add(new TurnRecord(turnBase + 1, _playerId, null, 2, Wing.GrandHall));
            }

            Assert.IsTrue(_context.EvaluateTaskCompletion(_playerId, task));
        }

        [TestMethod]
        public void DrawTasksForPlayer_AssignsR6Target()
        {
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2" };
            _state.CurrentTaskPool = [GetTask("R6")];
            _rngMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), RandomType.Fast)).Returns(0);

            _context.DrawTasksForPlayer(_playerId);

            var player = _state.GamePlayers[_playerId];
            Assert.AreEqual("p2", player.RivalryTargetPlayerId);
        }
    }
}
