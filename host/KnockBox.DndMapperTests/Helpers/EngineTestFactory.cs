using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Helpers
{
    internal static class EngineTestFactory
    {
        public static (DndMapperGameEngine engine, DndMapperGameState state, User host, SequentialRng rng) Build(
            params int[] rngValues)
            => Build(storage: null, engineLogger: null, rngValues: rngValues);

        // The `storage` parameter is preserved as no-op for backwards-compat with tests
        // that still pass it. After the IndexedDB migration the engine no longer touches
        // IPluginStorage; image bytes live in the host's browser, not on the server.
        public static (DndMapperGameEngine engine, DndMapperGameState state, User host, SequentialRng rng) Build(
            InMemoryPluginStorage? storage,
            params int[] rngValues)
            => Build(storage, engineLogger: null, rngValues: rngValues);

        public static (DndMapperGameEngine engine, DndMapperGameState state, User host, SequentialRng rng) Build(
            InMemoryPluginStorage? storage,
            ILogger<DndMapperGameEngine>? engineLogger,
            params int[] rngValues)
        {
            _ = storage; // accepted for compatibility; engine no longer uses IPluginStorage.
            var rng = new SequentialRng(rngValues);
            var engine = new DndMapperGameEngine(
                engineLogger ?? NullLogger<DndMapperGameEngine>.Instance,
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
