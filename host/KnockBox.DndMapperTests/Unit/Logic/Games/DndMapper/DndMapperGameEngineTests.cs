using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DndMapper.Tests.Unit.Logic
{
    [TestClass]
    public class DndMapperGameEngineTests
    {
        private Mock<ILogger<DndMapperGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DndMapperGameState>> _stateLoggerMock = default!;
        private Mock<IRandomNumberService> _rngMock = default!;
        private User _host = default!;
        private DndMapperGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<DndMapperGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DndMapperGameState>>();
            _rngMock = new Mock<IRandomNumberService>();
            _host = UserFactory.Create("Host", "host1");

            _engine = new DndMapperGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object,
                _rngMock.Object);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsJoinableState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (DndMapperGameState)result.Value!;
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
        public async Task StartAsync_HostStartsGame_FlipsIsJoinableFalse()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DndMapperGameState)stateResult.Value!;

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
        }
    }
}
