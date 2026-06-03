using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DndMapper.Tests.Unit.State
{
    [TestClass]
    public class DndMapperGameStateTests
    {
        private Mock<ILogger<DndMapperGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<DndMapperGameState>>();
            _host = UserFactory.Create("HostUser", Guid.NewGuid());
        }

        [TestMethod]
        public void Construct_DefaultsToNotJoinable()
        {
            using var state = new DndMapperGameState(_host, _loggerMock.Object);

            Assert.IsFalse(state.IsJoinable);
            Assert.AreSame(_host, state.Host);
        }
    }
}
