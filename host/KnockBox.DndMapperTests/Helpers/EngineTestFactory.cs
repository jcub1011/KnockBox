using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Helpers
{
    internal static class EngineTestFactory
    {
        public static (DndMapperGameEngine engine, DndMapperGameState state, User host, SequentialRng rng) Build(
            params int[] rngValues)
        {
            var rng = new SequentialRng(rngValues);
            var engine = new DndMapperGameEngine(
                NullLogger<DndMapperGameEngine>.Instance,
                NullLogger<DndMapperGameState>.Instance,
                rng);
            var host = UserFactory.Create("Host", Guid.NewGuid().ToString());
            var stateResult = engine.CreateStateAsync(host).GetAwaiter().GetResult();
            Assert.IsTrue(stateResult.TryGetSuccess(out var abstractState), "CreateStateAsync failed.");
            return (engine, (DndMapperGameState)abstractState, host, rng);
        }

        public static User RegisterPlayer(DndMapperGameState state, string? name = null)
        {
            var player = UserFactory.Create(name ?? $"P{Guid.NewGuid().ToString()[..4]}", Guid.NewGuid().ToString());
            var reg = state.RegisterPlayer(player);
            Assert.IsTrue(reg.TryGetSuccess(out _), $"RegisterPlayer failed: {reg}");
            return player;
        }

        public static IDisposable RegisterPlayerWithToken(DndMapperGameState state, out User player, string? name = null)
        {
            player = UserFactory.Create(name ?? $"P{Guid.NewGuid().ToString()[..4]}", Guid.NewGuid().ToString());
            var reg = state.RegisterPlayer(player);
            Assert.IsTrue(reg.TryGetSuccess(out var token), $"RegisterPlayer failed: {reg}");
            return token;
        }
    }
}
