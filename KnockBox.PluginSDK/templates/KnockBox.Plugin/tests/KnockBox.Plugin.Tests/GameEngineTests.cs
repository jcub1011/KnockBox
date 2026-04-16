using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Microsoft.Extensions.Logging;
using KnockBox.Plugin.Services.Logic;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Plugin.Services.State;

namespace KnockBox.Plugin.Tests
{
    [TestClass]
    public class GameEngineTests
    {
        [TestMethod]
        public async Task CreateStateAsync_ReturnsValidGameState()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<GameEngine>>();
            var engine = new GameEngine(loggerMock.Object);
            var host = new User("host-id", "HostUser");

            // Act
            var result = await engine.CreateStateAsync(host);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType(result.Value, typeof(GameState));
            Assert.AreEqual(host, result.Value.Host);
        }
    }
}
