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

        [TestInitialize]
        public void Setup()
        {
            _rngMock = new Mock<IRandomNumberService>();
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<HiddenAgendaGameState>>();
            
            var host = new User("Host", "host1");
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
            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState();
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState();
            _state.GamePlayers["p3"] = new HiddenAgendaPlayerState();
            Assert.AreEqual(12, _context.GetMaxTurnsPerPlayer());
            
            // 4 players
            _state.GamePlayers["p4"] = new HiddenAgendaPlayerState();
            Assert.AreEqual(11, _context.GetMaxTurnsPerPlayer());
            
            // 6 players
            _state.GamePlayers["p5"] = new HiddenAgendaPlayerState();
            _state.GamePlayers["p6"] = new HiddenAgendaPlayerState();
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
        public void CheckRoundEndConditions_GuessCountdownActive_SomePlayersRemaining_ReturnsNone()
        {
            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1" };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2", GuessCountdownTurnsRemaining = 2 };
            _state.GamePlayers["p3"] = new HiddenAgendaPlayerState { PlayerId = "p3", GuessCountdownTurnsRemaining = 1 };
            
            _state.GuessCountdownActive = true;
            _state.FirstGuessPlayerId = "p1";
            
            var result = _context.CheckRoundEndConditions();
            Assert.AreEqual(HiddenAgendaGameContext.RoundEndTrigger.None, result);
        }

        [TestMethod]
        public void CheckRoundEndConditions_GuessCountdownActive_AllPlayersExhausted_ReturnsGuessCountdown()
        {
            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1" };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2", GuessCountdownTurnsRemaining = 0 };
            _state.GamePlayers["p3"] = new HiddenAgendaPlayerState { PlayerId = "p3", HasSubmittedGuess = true };
            
            _state.GuessCountdownActive = true;
            _state.FirstGuessPlayerId = "p1";
            
            var result = _context.CheckRoundEndConditions();
            Assert.AreEqual(HiddenAgendaGameContext.RoundEndTrigger.GuessCountdown, result);
        }

        [TestMethod]
        public void CheckRoundEndConditions_MaxTurnsMet_ReturnsMaxTurns()
        {
            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1", TurnsTakenThisRound = 12 };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2", TurnsTakenThisRound = 12 };
            _state.GamePlayers["p3"] = new HiddenAgendaPlayerState { PlayerId = "p3", TurnsTakenThisRound = 12 };
            
            var result = _context.CheckRoundEndConditions();
            Assert.AreEqual(HiddenAgendaGameContext.RoundEndTrigger.MaxTurns, result);
        }

        [TestMethod]
        public void ResetForNewRound_ClearsRoundState()
        {
            _state.CurrentRound = 1;
            _state.TotalTurnsTaken = 10;
            _state.CollectionProgress[CollectionType.RenaissanceMasters] = 5;
            
            var player = new HiddenAgendaPlayerState { PlayerId = "p1", RoundScore = 10, TurnsTakenThisRound = 3 };
            _state.GamePlayers["p1"] = player;
            _state.CurrentTaskPool = TaskPool.AllTasks.Take(30).ToList();
            
            _rngMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), RandomType.Fast)).Returns(0);
            
            _context.ResetForNewRound();
            
            Assert.AreEqual(1, _state.CurrentRound); // No longer increments here
            Assert.AreEqual(0, _state.TotalTurnsTaken);
            Assert.AreEqual(0, _state.CollectionProgress[CollectionType.RenaissanceMasters]);
            Assert.AreEqual(0, player.RoundScore);
            Assert.AreEqual(0, player.TurnsTakenThisRound);
            Assert.AreEqual(0, player.SecretTasks.Count); // ResetForNewRound clears tasks, SetupState re-draws them
        }

        [TestMethod]
        public void ScoreRound_CorrectlyCalculatesTaskAndGuessPoints()
        {
            var p1 = new HiddenAgendaPlayerState 
            { 
                PlayerId = "p1", 
                DisplayName = "Player 1",
                SecretTasks = [
                    new SecretTask("D1", TaskCategory.Devotion, TaskDifficulty.Easy, "Renaissance Masters", "4", 1), // Easy (1 pt)
                    new SecretTask("Y1", TaskCategory.Style, TaskDifficulty.Medium, "Remove Count", "3", 2),          // Medium (2 pts)
                    new SecretTask("N1", TaskCategory.Neglect, TaskDifficulty.Hard, "Renaissance Masters", "0", 3)  // Hard (3 pts)
                ]
            };
            var p2 = new HiddenAgendaPlayerState { PlayerId = "p2", DisplayName = "Player 2", SecretTasks = [TaskPool.AllTasks[10], TaskPool.AllTasks[11], TaskPool.AllTasks[12]] };
            
            _state.GamePlayers["p1"] = p1;
            _state.GamePlayers["p2"] = p2;

            var ghPool = CurationCardPool.GetPool(Wing.GrandHall);

            // p1 completed D1 and Y1, but failed N1
            // D1: Acquire 4 times Renaissance Masters
            p1.CardPlayHistory.AddRange(Enumerable.Range(0, 4).Select(i => new CardPlayRecord(i, ghPool[0], 0, [CollectionType.RenaissanceMasters], CurationCardType.Acquire, CurationCardType.Acquire)));
            // Y1: Remove 3 times
            p1.CardPlayHistory.AddRange(Enumerable.Range(4, 3).Select(i => new CardPlayRecord(i, ghPool[10], 0, [CollectionType.RenaissanceMasters], CurationCardType.Remove, CurationCardType.Remove)));
            // N1 fails because we played Acquire Renaissance Masters for D1

            // p1 guessed p2's tasks correctly
            p1.GuessSubmission = new Dictionary<string, List<string>>
            {
                { "p2", p2.SecretTasks.Select(t => t.Id).ToList() } // 3 correct guesses = 3 pts
            };

            var result = _context.ScoreRound();

            var p1Result = result.PlayerResults["p1"];
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
            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1" };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2" };
            _state.GamePlayers["p3"] = new HiddenAgendaPlayerState { PlayerId = "p3" };

            _state.CurrentTaskPool = TaskPool.AllTasks.Take(30).ToList();
            var validGuesses = new Dictionary<string, List<string>>
            {
                { "p2", _state.CurrentTaskPool.Take(3).Select(t => t.Id).ToList() },
                { "p3", _state.CurrentTaskPool.Skip(3).Take(3).Select(t => t.Id).ToList() }
            };

            var result = _context.ValidateGuessSubmission("p1", validGuesses);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ValidateGuessSubmission_InvalidOpponent_ReturnsError()
        {
            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1" };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2" };

            var invalidGuesses = new Dictionary<string, List<string>>
            {
                { "wrong_id", TaskPool.AllTasks.Take(3).Select(t => t.Id).ToList() }
            };

            var result = _context.ValidateGuessSubmission("p1", invalidGuesses);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("Invalid opponent ID"));
        }

        [TestMethod]
        public void ValidateGuessSubmission_WrongTaskCount_ReturnsError()
        {
            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1" };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2" };

            var invalidGuesses = new Dictionary<string, List<string>>
            {
                { "p2", TaskPool.AllTasks.Take(2).Select(t => t.Id).ToList() }
            };

            var result = _context.ValidateGuessSubmission("p1", invalidGuesses);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("exactly 3 tasks"));
        }
    }
}
