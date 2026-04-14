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
        public void ResetForNewRound_ClearsRoundStateAndIncrementsRound()
        {
            _state.CurrentRound = 1;
            _state.TotalTurnsTaken = 10;
            _state.CollectionProgress[CollectionType.RenaissanceMasters] = 5;
            
            var player = new HiddenAgendaPlayerState { PlayerId = "p1", RoundScore = 10, TurnsTakenThisRound = 3 };
            _state.GamePlayers["p1"] = player;
            _state.CurrentTaskPool = TaskPool.AllTasks.Take(30).ToList();
            
            _rngMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), RandomType.Fast)).Returns(0);
            
            _context.ResetForNewRound();
            
            Assert.AreEqual(2, _state.CurrentRound);
            Assert.AreEqual(0, _state.TotalTurnsTaken);
            Assert.AreEqual(0, _state.CollectionProgress[CollectionType.RenaissanceMasters]);
            Assert.AreEqual(0, player.RoundScore);
            Assert.AreEqual(0, player.TurnsTakenThisRound);
            Assert.AreEqual(3, player.SecretTasks.Count);
        }
    }
}
