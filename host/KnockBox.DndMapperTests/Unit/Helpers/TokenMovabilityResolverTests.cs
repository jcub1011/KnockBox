using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class TokenMovabilityResolverTests
    {
        [TestMethod]
        public void OwnerOrHost_OwnerOfPlayerToken_True()
            => Assert.IsTrue(TokenMovabilityResolver.CanMove(
                isHost: false, isOwner: true, isParticipant: true,
                TokenMovementPolicy.OwnerOrHost));

        [TestMethod]
        public void OwnerOrHost_HostOfAnyToken_True()
            => Assert.IsTrue(TokenMovabilityResolver.CanMove(
                isHost: true, isOwner: false, isParticipant: false,
                TokenMovementPolicy.OwnerOrHost));

        [TestMethod]
        public void OwnerOrHost_NonOwnerNonHost_False()
            => Assert.IsFalse(TokenMovabilityResolver.CanMove(
                isHost: false, isOwner: false, isParticipant: true,
                TokenMovementPolicy.OwnerOrHost));

        [TestMethod]
        public void Anyone_HostTrue()
            => Assert.IsTrue(TokenMovabilityResolver.CanMove(
                isHost: true, isOwner: false, isParticipant: false,
                TokenMovementPolicy.Anyone));

        [TestMethod]
        public void Anyone_ParticipantTrue()
            => Assert.IsTrue(TokenMovabilityResolver.CanMove(
                isHost: false, isOwner: false, isParticipant: true,
                TokenMovementPolicy.Anyone));

        [TestMethod]
        public void Anyone_NonParticipantNonHost_False()
            => Assert.IsFalse(TokenMovabilityResolver.CanMove(
                isHost: false, isOwner: false, isParticipant: false,
                TokenMovementPolicy.Anyone));

        [TestMethod]
        public void HostOnly_HostTrue()
            => Assert.IsTrue(TokenMovabilityResolver.CanMove(
                isHost: true, isOwner: true, isParticipant: true,
                TokenMovementPolicy.HostOnly));

        [TestMethod]
        public void HostOnly_PlayerOwner_False()
            => Assert.IsFalse(TokenMovabilityResolver.CanMove(
                isHost: false, isOwner: true, isParticipant: true,
                TokenMovementPolicy.HostOnly));
    }
}
