using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Tracery.Tests.Unit.Logic.Games
{
    [TestClass]
    public class TraceryGameEngineTests
    {
        private Mock<ILogger<TraceryGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<TraceryGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private TraceryGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<TraceryGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<TraceryGameState>>();
            _host = UserFactory.Create("Host", "host1");
            _engine = new TraceryGameEngine(_engineLoggerMock.Object, _stateLoggerMock.Object);
        }

        [TestMethod]
        public void PlayerCountRange_IsTwoToEight()
        {
            Assert.AreEqual(2, _engine.MinPlayerCount);
            Assert.AreEqual(8, _engine.MaxPlayerCount);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsJoinableState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (TraceryGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
        }

        [TestMethod]
        public async Task CreateStateAsync_NullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_AsHost_FlipsJoinableOff()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (TraceryGameState)stateResult.Value!;

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public async Task StartAsync_NonHost_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (TraceryGameState)stateResult.Value!;
            var stranger = UserFactory.Create("Stranger", "stranger1");

            var result = await _engine.StartAsync(stranger, state);

            Assert.IsTrue((bool)result.IsFailure);
        }
    }
}
