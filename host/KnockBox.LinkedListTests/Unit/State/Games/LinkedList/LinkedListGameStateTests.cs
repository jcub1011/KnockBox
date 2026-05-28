using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.LinkedList.Tests.Unit.State
{
    [TestClass]
    public class LinkedListGameStateTests
    {
        private Mock<ILogger<LinkedListGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<LinkedListGameState>>();
            _host = UserFactory.Create("HostUser", "host-id");
        }

        [TestMethod]
        public void Constructor_SetsHost()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            Assert.AreSame(_host, state.Host);
        }

        [TestMethod]
        public void SetJoinable_TogglesJoinableState()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            state.Execute(() => state.SetJoinable(true));
            Assert.IsTrue(state.IsJoinable);

            state.Execute(() => state.SetJoinable(false));
            Assert.IsFalse(state.IsJoinable);
        }
    }
}
