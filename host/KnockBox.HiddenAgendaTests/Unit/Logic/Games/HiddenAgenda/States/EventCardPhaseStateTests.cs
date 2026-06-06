using System;
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
    public class EventCardPhaseStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLogger = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;

        private Guid[] _playerIds = default!;

        [TestInitialize]
        public void Setup()
        {
            _rng = new Mock<IRandomNumberService>();
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<HiddenAgendaGameState>>();

            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new HiddenAgendaGameState(host, _stateLogger.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();
            _context = new HiddenAgendaGameContext(_state, _rng.Object, _logger.Object);

            _playerIds = new Guid[4];
            for (int i = 0; i < 4; i++)
            {
                _playerIds[i] = Guid.NewGuid();
                _state.GamePlayers[_playerIds[i]] = new HiddenAgendaPlayerState
                {
                    PlayerId = _playerIds[i],
                    DisplayName = $"Player {i}"
                };
            }
            _state.TurnManager.SetTurnOrder(_playerIds);
        }

        [TestMethod]
        public void OnEnter_NoEventCard_TransitionsToSpinPhase()
        {
            _state.GamePlayers[_playerIds[0]].HeldEventCard = null;
            var state = new EventCardPhaseState();
            var result = state.OnEnter(_context);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<SpinPhaseState>(result.Value);
        }

        [TestMethod]
        public void OnEnter_HasEventCard_StaysInState()
        {
            _state.GamePlayers[_playerIds[0]].HeldEventCard = EventCardDefinitions.Catalog;
            var state = new EventCardPhaseState();
            var result = state.OnEnter(_context);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
            Assert.AreEqual(GamePhase.EventCardPhase, _state.Phase);
        }

        [TestMethod]
        public void PlayCatalog_Valid_RevealsCardsAndEndsTurn()
        {
            var p0 = _state.GamePlayers[_playerIds[0]];
            var p1 = _state.GamePlayers[_playerIds[1]];
            p0.HeldEventCard = EventCardDefinitions.Catalog;

            var drawn = new List<CurationCard> {
                new(CurationCardType.Acquire, "C1", []),
                new(CurationCardType.Acquire, "C2", []),
                new(CurationCardType.Acquire, "C3", [])
            };
            p1.CardDrawHistory.Add(new CardDrawRecord(1, drawn));

            var state = new EventCardPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new PlayCatalogCommand(_playerIds[0], _playerIds[1]));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(_state.CatalogRevealedCards);
            Assert.HasCount(3, _state.CatalogRevealedCards);
            Assert.IsNull(p0.HeldEventCard);

            // AdvanceToNextPlayer returns EventCardPhaseState for next player
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
            Assert.AreEqual(_playerIds[1], _state.TurnManager.CurrentPlayer);
        }

        [TestMethod]
        public void PlayDetour_Valid_SetsPendingAndTransitionsToSpin()
        {
            var p0 = _state.GamePlayers[_playerIds[0]];
            var p1 = _state.GamePlayers[_playerIds[1]];
            p0.HeldEventCard = EventCardDefinitions.Detour;
            p1.LastMoveDestination = 5;

            var state = new EventCardPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new PlayDetourCommand(_playerIds[0], _playerIds[1]));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<SpinPhaseState>(result.Value);
            Assert.IsTrue(p0.DetourPending);
            Assert.AreEqual(_playerIds[1], p0.DetourTargetPlayerId);
            Assert.IsNull(p0.HeldEventCard);
        }

        [TestMethod]
        public void SkipEventCard_TransitionsToSpinPhase()
        {
            _state.GamePlayers[_playerIds[0]].HeldEventCard = EventCardDefinitions.Catalog;
            var state = new EventCardPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new SkipEventCardCommand(_playerIds[0]));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<SpinPhaseState>(result.Value);
        }

        [TestMethod]
        public void Tick_AutoSkipsAfterTimeout()
        {
            _state.UpdateSettings(s => s with { EnableTimers = true });
            _state.GamePlayers[_playerIds[0]].HeldEventCard = EventCardDefinitions.Catalog;
            var state = new EventCardPhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Settings.EventCardPhaseTimeoutMs + 100));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<SpinPhaseState>(result.Value);
        }

        [TestMethod]
        public void Tick_TimersDisabled_DoesNotAutoAdvance()
        {
            _state.GamePlayers[_playerIds[0]].HeldEventCard = EventCardDefinitions.Catalog;
            var state = new EventCardPhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Settings.EventCardPhaseTimeoutMs + 100));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void CallVote_AnyPlayer_TransitionsToFinalGuess()
        {
            _state.GamePlayers[_playerIds[0]].HeldEventCard = EventCardDefinitions.Catalog;
            var state = new EventCardPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new CallVoteCommand(_playerIds[1]));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<FinalGuessState>(result.Value);
        }
    }
}
