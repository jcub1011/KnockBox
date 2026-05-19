using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games
{
    [TestClass]
    public class AlphaChainGameEngineTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private AlphaChainGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", "host1");
            _engine = new AlphaChainGameEngine(_engineLoggerMock.Object, _stateLoggerMock.Object);
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
            var state = (AlphaChainGameState)result.Value!;
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
            var state = (AlphaChainGameState)stateResult.Value!;

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public async Task StartAsync_NonHost_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (AlphaChainGameState)stateResult.Value!;
            var stranger = UserFactory.Create("Stranger", "stranger1");

            var result = await _engine.StartAsync(stranger, state);

            Assert.IsTrue((bool)result.IsFailure);
        }
    }
}
