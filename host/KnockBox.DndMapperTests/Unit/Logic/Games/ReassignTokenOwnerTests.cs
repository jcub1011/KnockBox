using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit.Logic.Games
{
    [TestClass]
    public class ReassignTokenOwnerTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private Guid _mapId;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "M").TryGetSuccess(out _mapId));
            Assert.IsTrue(_engine.SetActiveMapAsync(_state, _host, _mapId).IsSuccess);
        }

        private Guid SpawnNpc(string name = "Goblin")
        {
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, _mapId, name).TryGetSuccess(out var id));
            return id;
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_PromoteNpcToPlayerToken_SetsTypeAndOwner()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var tokenId = SpawnNpc();

            var result = _engine.ReassignTokenOwnerAsync(_state, _host, tokenId, player.Id, TokenType.PlayerToken);

            Assert.IsTrue(result.IsSuccess);
            var token = _state.Maps[0].Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.PlayerToken, token.Type);
            Assert.AreEqual(player.Id, token.OwnerUserId);
            Assert.IsNull(token.RepresentsUserId);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_PlayerTokenWithoutOwnerUserId_ReturnsError()
        {
            var tokenId = SpawnNpc();

            var result = _engine.ReassignTokenOwnerAsync(_state, _host, tokenId, newOwnerUserId: null, TokenType.PlayerToken);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_PlayerTokenForUnregisteredUser_ReturnsError()
        {
            var tokenId = SpawnNpc();

            var result = _engine.ReassignTokenOwnerAsync(_state, _host, tokenId, Guid.NewGuid(), TokenType.PlayerToken);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_PlayerAlreadyOwnsTokenOnMap_ReturnsError()
        {
            // SetActiveMap auto-spawns a player token; reassigning a second NPC
            // to the same player on the same map must be rejected.
            var player = EngineTestFactory.RegisterPlayer(_state);
            Assert.IsTrue(_engine.SetActiveMapAsync(_state, _host, _mapId).IsSuccess);
            var otherNpc = SpawnNpc("Backup");

            var result = _engine.ReassignTokenOwnerAsync(_state, _host, otherNpc, player.Id, TokenType.PlayerToken);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_ToNpcWithOwner_AllowsPlayerOwnedNpc()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var tokenId = SpawnNpc();

            var result = _engine.ReassignTokenOwnerAsync(_state, _host, tokenId, player.Id, TokenType.NPCToken);

            Assert.IsTrue(result.IsSuccess);
            var token = _state.Maps[0].Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.NPCToken, token.Type);
            Assert.AreEqual(player.Id, token.OwnerUserId);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_NpcWithUnregisteredOwnerUserId_ReturnsError()
        {
            var tokenId = SpawnNpc();

            var result = _engine.ReassignTokenOwnerAsync(_state, _host, tokenId, Guid.NewGuid(), TokenType.NPCToken);

            Assert.IsTrue(result.IsFailure);
            var token = _state.Maps[0].Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.NPCToken, token.Type);
            Assert.IsNull(token.OwnerUserId);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_NpcWithNullOwner_PreservesHostOwnedNpc()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var tokenId = SpawnNpc();
            // First make it player-owned so the null reassignment is meaningful.
            Assert.IsTrue(_engine.ReassignTokenOwnerAsync(_state, _host, tokenId, player.Id, TokenType.NPCToken).IsSuccess);

            var result = _engine.ReassignTokenOwnerAsync(_state, _host, tokenId, newOwnerUserId: null, TokenType.NPCToken);

            Assert.IsTrue(result.IsSuccess);
            var token = _state.Maps[0].Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.NPCToken, token.Type);
            Assert.IsNull(token.OwnerUserId);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var tokenId = SpawnNpc();

            var result = _engine.ReassignTokenOwnerAsync(_state, player, tokenId, player.Id, TokenType.PlayerToken);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ReassignTokenOwnerAsync_UnknownToken_ReturnsError()
        {
            var result = _engine.ReassignTokenOwnerAsync(_state, _host, Guid.NewGuid(), null, TokenType.NPCToken);
            Assert.IsTrue(result.IsFailure);
        }
    }
}
