using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgenda.Tests.Unit.State
{
    [TestClass]
    public class HiddenAgendaGameStateTests
    {
        private Mock<ILogger<HiddenAgendaGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<HiddenAgendaGameState>>();
            _host = new User("HostUser", "host-id");
        }

        [TestMethod]
        public void Constructor_SetsHostAndDefaultPhase()
        {
            using var state = new HiddenAgendaGameState(_host, _loggerMock.Object);

            Assert.AreSame(_host, state.Host);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public void SetPhase_UpdatesPhase()
        {
            using var state = new HiddenAgendaGameState(_host, _loggerMock.Object);

            state.SetPhase(GamePhase.RoundSetup);
            Assert.AreEqual(GamePhase.RoundSetup, state.Phase);

            state.SetPhase(GamePhase.MatchOver);
            Assert.AreEqual(GamePhase.MatchOver, state.Phase);
        }
    }
}
