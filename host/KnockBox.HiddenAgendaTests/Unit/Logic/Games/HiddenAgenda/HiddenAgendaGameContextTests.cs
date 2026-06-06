using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgenda.Tests.Unit.Logic
{
    [TestClass]
    public class HiddenAgendaGameContextTests
    {
        private Mock<IRandomNumberService> _rngMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLoggerMock = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;

        private Guid _p1Id = default!;
        private Guid _p2Id = default!;
        private Guid _p3Id = default!;
        private Guid _p4Id = default!;
        private Guid _p5Id = default!;
        private Guid _p6Id = default!;

        [TestInitialize]
        public void Setup()
        {
            _p1Id = Guid.NewGuid();
            _p2Id = Guid.NewGuid();
            _p3Id = Guid.NewGuid();
            _p4Id = Guid.NewGuid();
            _p5Id = Guid.NewGuid();
            _p6Id = Guid.NewGuid();

            _rngMock = new Mock<IRandomNumberService>();
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<HiddenAgendaGameState>>();

            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new HiddenAgendaGameState(host, _stateLoggerMock.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();

            _context = new HiddenAgendaGameContext(_state, _rngMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        public void SpinSpinner_ReturnsValueInRange()
        {
            _rngMock.Setup(r => r.GetRandomInt(3, 13, RandomType.Fast)).Returns(7);

            var result = _context.SpinSpinner();

            Assert.AreEqual(7, result);
            _rngMock.Verify(r => r.GetRandomInt(3, 13, RandomType.Fast), Times.Once);
        }

        [TestMethod]
        public void ApplyCollectionEffects_UpdatesProgress()
        {
            var effects = new List<CollectionEffect>
            {
                new(CollectionType.RenaissanceMasters, 2),
                new(CollectionType.ContemporaryShowcase, -1)
            };

            _state.CollectionProgress[CollectionType.RenaissanceMasters] = 5;
            _state.CollectionProgress[CollectionType.ContemporaryShowcase] = 0;

            _context.ApplyCollectionEffects(effects);

            Assert.AreEqual(7, _state.CollectionProgress[CollectionType.RenaissanceMasters]);
            Assert.AreEqual(0, _state.CollectionProgress[CollectionType.ContemporaryShowcase]); // Clamped at 0
        }

        [TestMethod]
        public void GetCompletedCollectionCount_ReturnsCorrectCount()
        {
            _state.CollectionProgress[CollectionType.RenaissanceMasters] = 12; // Target 12
            _state.CollectionProgress[CollectionType.ContemporaryShowcase] = 9;  // Target 10
            _state.CollectionProgress[CollectionType.ImpressionistGallery] = 10; // Target 10

            var count = _context.GetCompletedCollectionCount();

            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void GetMaxTurnsPerPlayer_ReturnsCorrectValues()
        {
            // 3 players
            _state.GamePlayers[_p1Id] = new HiddenAgendaPlayerState();
            _state.GamePlayers[_p2Id] = new HiddenAgendaPlayerState();
            _state.GamePlayers[_p3Id] = new HiddenAgendaPlayerState();
            Assert.AreEqual(12, _context.GetMaxTurnsPerPlayer());

            // 4 players
            _state.GamePlayers[_p4Id] = new HiddenAgendaPlayerState();
            Assert.AreEqual(11, _context.GetMaxTurnsPerPlayer());

            // 6 players
            _state.GamePlayers[_p5Id] = new HiddenAgendaPlayerState();
            _state.GamePlayers[_p6Id] = new HiddenAgendaPlayerState();
            Assert.AreEqual(9, _context.GetMaxTurnsPerPlayer());
        }

        [TestMethod]
        public void CheckRoundEndConditions_NoConditionsMet_ReturnsNone()
        {
            var result = _context.CheckRoundEndConditions();
            Assert.AreEqual(HiddenAgendaGameContext.RoundEndTrigger.None, result);
        }

        [TestMethod]
        public void CheckRoundEndConditions_CollectionTrigger_ReturnsCollectionTrigger()
        {
            _state.CollectionProgress[CollectionType.RenaissanceMasters] = 12;
            _state.CollectionProgress[CollectionType.ContemporaryShowcase] = 10;
            _state.CollectionProgress[CollectionType.ImpressionistGallery] = 10;

            var result = _context.CheckRoundEndConditions();
            Assert.AreEqual(HiddenAgendaGameContext.RoundEndTrigger.CollectionTrigger, result);
        }

        [TestMethod]
        public void CheckRoundEndConditions_MaxTurnsMet_ReturnsMaxTurns()
        {
            _state.GamePlayers[_p1Id] = new HiddenAgendaPlayerState { PlayerId = _p1Id, TurnsTakenThisRound = 12 };
            _state.GamePlayers[_p2Id] = new HiddenAgendaPlayerState { PlayerId = _p2Id, TurnsTakenThisRound = 12 };
            _state.GamePlayers[_p3Id] = new HiddenAgendaPlayerState { PlayerId = _p3Id, TurnsTakenThisRound = 12 };

            var result = _context.CheckRoundEndConditions();
            Assert.AreEqual(HiddenAgendaGameContext.RoundEndTrigger.MaxTurns, result);
        }

        [TestMethod]
        public void ResetForNewRound_ClearsRoundState()
        {
            _state.CurrentRound = 1;
            _state.TotalTurnsTaken = 10;
            _state.CollectionProgress[CollectionType.RenaissanceMasters] = 5;

            var player = new HiddenAgendaPlayerState { PlayerId = _p1Id, RoundScore = 10, TurnsTakenThisRound = 3 };
            _state.GamePlayers[_p1Id] = player;
            _state.CurrentTaskPool = TaskPool.AllTasks.Take(30).ToList();

            _rngMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), RandomType.Fast)).Returns(0);

            _context.ResetForNewRound();

            Assert.AreEqual(1, _state.CurrentRound); // No longer increments here
            Assert.AreEqual(0, _state.TotalTurnsTaken);
            Assert.AreEqual(0, _state.CollectionProgress[CollectionType.RenaissanceMasters]);
            Assert.AreEqual(0, player.RoundScore);
            Assert.AreEqual(0, player.TurnsTakenThisRound);
            Assert.IsEmpty(player.SecretTasks); // ResetForNewRound clears tasks, SetupState re-draws them
        }

        [TestMethod]
        public void ScoreRound_CorrectlyCalculatesTaskAndGuessPoints()
        {
            var p1 = new HiddenAgendaPlayerState
            {
                PlayerId = _p1Id,
                DisplayName = "Player 1",
                SecretTasks = [
                    new SecretTask("D1", TaskCategory.Devotion, TaskDifficulty.Easy, "Renaissance Masters", "4", 1), // Easy (1 pt)
                    new SecretTask("Y1", TaskCategory.Style, TaskDifficulty.Medium, "Remove Count", "3", 2),          // Medium (2 pts)
                    new SecretTask("N1", TaskCategory.Neglect, TaskDifficulty.Hard, "Renaissance Masters", "0", 3)  // Hard (3 pts)
                ]
            };
            var p2 = new HiddenAgendaPlayerState { PlayerId = _p2Id, DisplayName = "Player 2", SecretTasks = [TaskPool.AllTasks[10], TaskPool.AllTasks[11], TaskPool.AllTasks[12]] };

            _state.GamePlayers[_p1Id] = p1;
            _state.GamePlayers[_p2Id] = p2;

            var ghPool = CurationCardPool.GetPool(Wing.GrandHall);

            // p1 completed D1 and Y1, but failed N1
            // D1: Acquire 4 times Renaissance Masters
            p1.CardPlayHistory.AddRange(Enumerable.Range(0, 4).Select(i => new CardPlayRecord(i, ghPool[0], 0, [CollectionType.RenaissanceMasters], CurationCardType.Acquire, CurationCardType.Acquire)));
            // Y1: Remove 3 times
            p1.CardPlayHistory.AddRange(Enumerable.Range(4, 3).Select(i => new CardPlayRecord(i, ghPool[10], 0, [CollectionType.RenaissanceMasters], CurationCardType.Remove, CurationCardType.Remove)));
            // N1 fails because we played Acquire Renaissance Masters for D1

            // p1 guessed p2's tasks correctly
            p1.GuessSubmission = new Dictionary<Guid, List<string>>
            {
                { _p2Id, p2.SecretTasks.Select(t => t.Id).ToList() } // 3 correct guesses = 3 pts
            };

            var result = _context.ScoreRound();

            var p1Result = result.PlayerResults[_p1Id];
            Assert.AreEqual(1 + 2, p1Result.TaskPoints); // D1 (1) + Y1 (2)
            Assert.AreEqual(3, p1Result.GuessPoints);
            Assert.AreEqual(6, p1Result.TotalRoundPoints);
            Assert.AreEqual(6, p1.RoundScore);

            Assert.IsTrue(p1Result.TaskResults.First(t => t.Task.Id == "D1").Completed);
            Assert.IsTrue(p1Result.TaskResults.First(t => t.Task.Id == "Y1").Completed);
            Assert.IsFalse(p1Result.TaskResults.First(t => t.Task.Id == "N1").Completed);
        }

        [TestMethod]
        public void ValidateGuessSubmission_Valid_ReturnsNull()
        {
            _state.GamePlayers[_p1Id] = new HiddenAgendaPlayerState { PlayerId = _p1Id };
            _state.GamePlayers[_p2Id] = new HiddenAgendaPlayerState { PlayerId = _p2Id };
            _state.GamePlayers[_p3Id] = new HiddenAgendaPlayerState { PlayerId = _p3Id };

            _state.CurrentTaskPool = TaskPool.AllTasks.Take(30).ToList();
            var validGuesses = new Dictionary<Guid, List<string>>
            {
                { _p2Id, _state.CurrentTaskPool.Take(3).Select(t => t.Id).ToList() },
                { _p3Id, _state.CurrentTaskPool.Skip(3).Take(3).Select(t => t.Id).ToList() }
            };

            var result = _context.ValidateGuessSubmission(_p1Id, validGuesses);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ValidateGuessSubmission_InvalidOpponent_ReturnsError()
        {
            _state.GamePlayers[_p1Id] = new HiddenAgendaPlayerState { PlayerId = _p1Id };
            _state.GamePlayers[_p2Id] = new HiddenAgendaPlayerState { PlayerId = _p2Id };

            var invalidGuesses = new Dictionary<Guid, List<string>>
            {
                { Guid.NewGuid(), TaskPool.AllTasks.Take(3).Select(t => t.Id).ToList() }
            };

            var result = _context.ValidateGuessSubmission(_p1Id, invalidGuesses);
            Assert.IsNotNull(result);
            Assert.Contains("Invalid opponent ID", result);
        }

        [TestMethod]
        public void ValidateGuessSubmission_WrongTaskCount_ReturnsError()
        {
            _state.GamePlayers[_p1Id] = new HiddenAgendaPlayerState { PlayerId = _p1Id };
            _state.GamePlayers[_p2Id] = new HiddenAgendaPlayerState { PlayerId = _p2Id };

            var invalidGuesses = new Dictionary<Guid, List<string>>
            {
                { _p2Id, TaskPool.AllTasks.Take(2).Select(t => t.Id).ToList() }
            };

            var result = _context.ValidateGuessSubmission(_p1Id, invalidGuesses);
            Assert.IsNotNull(result);
            Assert.Contains("exactly 3 tasks", result);
        }
    }
}
