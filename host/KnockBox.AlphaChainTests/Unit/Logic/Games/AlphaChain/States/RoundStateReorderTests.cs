using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Validates Engine Bay reordering: a permutation of the current cards is applied, while
    /// unknown ids and over-capacity requests are rejected and leave the bay untouched.
    /// </summary>
    [TestClass]
    public class RoundStateReorderTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", "host1");
        }

        private static User MakePlayer(int index) => UserFactory.Create($"Player{index}", $"p{index}-id");

        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State, string Player)> StartWithBayAsync(
            params string[] modifierIds)
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService("cat"), new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < 2; i++)
                state.RegisterPlayer(MakePlayer(i));

            await engine.StartAsync(_host, state);

            var player = state.TurnManager.CurrentPlayer!;
            state.Execute(() =>
            {
                var bay = state.GamePlayers[player].EngineBay;
                foreach (var id in modifierIds)
                    bay.Add(ModifierLibrary.FindById(id)!);
            });

            return (engine, state, player);
        }

        [TestMethod]
        public async Task Reorder_ValidPermutation_IsApplied()
        {
            var (engine, state, player) = await StartWithBayAsync("anchor", "vowel-surge", "sprinter");
            using var _ = state;

            // Reverse the order.
            var newOrder = new[] { "sprinter", "vowel-surge", "anchor" };
            var result = await engine.ReorderEngineBayAsync(player, newOrder, state);

            Assert.IsTrue(result.IsSuccess);
            CollectionAssert.AreEqual(
                newOrder,
                state.GamePlayers[player].EngineBay.Select(c => c.Id).ToArray());
        }

        [TestMethod]
        public async Task Reorder_RejectedWhenCardIdMissing()
        {
            var (engine, state, player) = await StartWithBayAsync("anchor", "vowel-surge", "sprinter");
            using var _ = state;

            // Same count, but one id is not in the bay.
            var bad = new[] { "anchor", "vowel-surge", "does-not-exist" };
            var result = await engine.ReorderEngineBayAsync(player, bad, state);

            Assert.IsTrue(result.TryGetFailure(out var _ignored));
            // Bay untouched.
            CollectionAssert.AreEqual(
                new[] { "anchor", "vowel-surge", "sprinter" },
                state.GamePlayers[player].EngineBay.Select(c => c.Id).ToArray());
        }

        [TestMethod]
        public async Task Reorder_RejectedWhenLengthExceedsModifierSlots()
        {
            var (engine, state, player) = await StartWithBayAsync("anchor", "vowel-surge", "sprinter");
            using var _ = state;
            // Default ModifierSlots is 3.
            Assert.AreEqual(3, state.GamePlayers[player].ModifierSlots);

            var tooMany = new[] { "anchor", "vowel-surge", "sprinter", "architect" };
            var result = await engine.ReorderEngineBayAsync(player, tooMany, state);

            Assert.IsTrue(result.TryGetFailure(out var _ignored));
            CollectionAssert.AreEqual(
                new[] { "anchor", "vowel-surge", "sprinter" },
                state.GamePlayers[player].EngineBay.Select(c => c.Id).ToArray());
        }

        [TestMethod]
        public async Task Reorder_RejectedWhenDuplicateId()
        {
            var (engine, state, player) = await StartWithBayAsync("anchor", "vowel-surge", "sprinter");
            using var _ = state;

            var dup = new[] { "anchor", "anchor", "sprinter" };
            var result = await engine.ReorderEngineBayAsync(player, dup, state);

            Assert.IsTrue(result.TryGetFailure(out var _ignored));
            CollectionAssert.AreEqual(
                new[] { "anchor", "vowel-surge", "sprinter" },
                state.GamePlayers[player].EngineBay.Select(c => c.Id).ToArray());
        }
    }
}
