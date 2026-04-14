using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgendaTests.Unit.Logic.Games.HiddenAgenda.States
{
    [TestClass]
    public class DrawPhaseStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLogger = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _rng = new Mock<IRandomNumberService>();
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<HiddenAgendaGameState>>();

            var host = new User("Host", "host-id");
            _state = new HiddenAgendaGameState(host, _stateLogger.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();
            _context = new HiddenAgendaGameContext(_state, _rng.Object, _logger.Object);

            for (int i = 0; i < 4; i++)
            {
                var pid = $"p{i}";
                _state.GamePlayers[pid] = new HiddenAgendaPlayerState
                {
                    PlayerId = pid,
                    DisplayName = $"Player {i}",
                    CurrentSpaceId = 0
                };
            }
            _state.TurnManager.SetTurnOrder(new List<string> { "p0", "p1", "p2", "p3" });
        }

        [TestMethod]
        public void OnEnter_CurationSpot_DrawsThreeCards()
        {
            _state.GamePlayers["p0"].CurrentSpaceId = 0; // Grand Hall Foyer (Curation)
            var state = new DrawPhaseState();
            state.OnEnter(_context);

            Assert.IsNotNull(_state.DrawnCards);
            Assert.AreEqual(3, _state.DrawnCards.Count);
            Assert.AreEqual(1, _state.GamePlayers["p0"].CardDrawHistory.Count);
        }

        [TestMethod]
        public void OnEnter_EventSpot_AutoTakesIfNoCard()
        {
            _state.GamePlayers["p0"].CurrentSpaceId = 4; // Grand Hall Event (Event)
            var state = new DrawPhaseState();
            var result = state.OnEnter(_context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GuessPhaseState>(result.Value);
            Assert.IsNotNull(_state.GamePlayers["p0"].HeldEventCard);
        }

        [TestMethod]
        public void SelectCurationCard_Valid_AppliesEffectsAndFinishesTurn()
        {
            _state.GamePlayers["p0"].CurrentSpaceId = 0;
            var state = new DrawPhaseState();
            state.OnEnter(_context);

            var card = _state.DrawnCards![0];
            var result = state.HandleCommand(_context, new SelectCurationCardCommand("p0", 0));
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GuessPhaseState>(result.Value);
            
            // Check collection progress
            foreach (var effect in card.Effects)
            {
                Assert.AreEqual(effect.Delta, _state.CollectionProgress[effect.Collection]);
            }
            
            Assert.AreEqual(1, _state.GamePlayers["p0"].CardPlayHistory.Count);
            Assert.AreEqual(1, _state.RoundPlayHistory.Count);
        }

        [TestMethod]
        public void SelectTradeOption_Valid_AppliesSelectedOption()
        {
            _state.GamePlayers["p0"].CurrentSpaceId = 0;
            
            // Inject a trade card
            var tradeCard = new CurationCard(CurationCardType.Trade, "Trade", 
                [new(CollectionType.RenaissanceMasters, 2)], 
                [new(CollectionType.ContemporaryShowcase, 2)]);
            
            var state = new DrawPhaseState();
            state.OnEnter(_context);
            _state.DrawnCards![0] = tradeCard;

            // Select trade card
            var result1 = state.HandleCommand(_context, new SelectCurationCardCommand("p0", 0));
            Assert.IsTrue(result1.IsSuccess);
            Assert.IsNull(result1.Value); // Waiting for trade option

            // Select alternate option
            var result2 = state.HandleCommand(_context, new SelectTradeOptionCommand("p0", true));
            Assert.IsTrue(result2.IsSuccess);
            Assert.IsInstanceOfType<GuessPhaseState>(result2.Value);
            
            Assert.AreEqual(2, _state.CollectionProgress[CollectionType.ContemporaryShowcase]);
            Assert.IsFalse(_state.CollectionProgress.ContainsKey(CollectionType.RenaissanceMasters));
        }

        [TestMethod]
        public void Tick_AutoSelectsFirstCardAfterTimeout()
        {
            _state.GamePlayers["p0"].CurrentSpaceId = 0;
            var state = new DrawPhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Config.DrawPhaseTimeoutMs + 100));
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GuessPhaseState>(result.Value);
            Assert.AreEqual(1, _state.GamePlayers["p0"].CardPlayHistory.Count);
        }
    }
}