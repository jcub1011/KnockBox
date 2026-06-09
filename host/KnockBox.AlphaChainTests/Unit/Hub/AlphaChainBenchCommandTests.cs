using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.AlphaChain.Pages.Bench;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Hub
{
    /// <summary>
    /// Covers the host-only Testing Bay hub commands: the enter/exit lifecycle (gates + lobby
    /// close/reopen + disposal), the projection short-circuit to the bench's inner state
    /// (<c>IsBench</c> + the card catalogue), bench mutations reflected after re-projection, and
    /// the notification bridge that makes a bench mutation re-project (the load-bearing invariant).
    /// </summary>
    [TestClass]
    public class AlphaChainBenchCommandTests
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private User _host = default!;
        private AlphaChainGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _host = UserFactory.Create("Host", Guid.NewGuid());
            _engine = new AlphaChainGameEngine(
                new StubWordListService("cat"),
                new FixedRandomNumberService(),
                new EngineEvaluator(),
                new ModifierCardFactory(),
                Mock.Of<ILogger<AlphaChainGameEngine>>(),
                Mock.Of<ILogger<AlphaChainGameState>>());
        }

        private IGameCommandHandler Hub => _engine;

        private async Task<AlphaChainGameState> HostAloneAsync()
            => (AlphaChainGameState)(await _engine.CreateStateAsync(_host)).Value!;

        private AlphaChainView Project(AlphaChainGameState state, Guid recipientId)
            => (AlphaChainView)((IGameStateProjector)_engine).ProjectFor(state, recipientId)!;

        private static string Json<T>(T payload) => JsonSerializer.Serialize(payload, WriteOptions);

        // ── Enter / exit lifecycle + gates ──

        [TestMethod]
        public async Task BenchEnter_WhenHostAlone_OpensBenchAndClosesLobby()
        {
            using var state = await HostAloneAsync();

            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(state.Bench);
            Assert.IsTrue(state.Bench!.IsReady);
            Assert.IsFalse(state.IsJoinable, "The lobby is closed while the bench is active (hard join lock).");
        }

        [TestMethod]
        public async Task BenchEnter_WhenAnotherPlayerPresent_IsRejected()
        {
            using var state = await HostAloneAsync();
            state.RegisterPlayer(UserFactory.Create("Joiner", Guid.NewGuid()));

            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);

            Assert.IsTrue(result.IsFailure);
            Assert.IsNull(state.Bench);
        }

        [TestMethod]
        public async Task BenchEnter_ByNonHost_IsRejected()
        {
            using var state = await HostAloneAsync();
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());

            var result = await Hub.HandleCommandAsync(stranger, state, AlphaChainCommands.BenchEnter, null);

            Assert.IsTrue(result.IsFailure);
            Assert.IsNull(state.Bench);
        }

        [TestMethod]
        public async Task BenchExit_DisposesBenchAndReopensLobby()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);
            var bench = state.Bench!;

            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchExit, null);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(state.Bench);
            Assert.IsFalse(bench.IsReady, "The bench's inner state is disposed on exit.");
            Assert.IsTrue(state.IsJoinable, "The lobby reopens after leaving the bench.");
        }

        // ── Projection ──

        [TestMethod]
        public async Task ProjectFor_WhenBenchActive_SetsIsBench_AndProjectsInnerStateWithCatalogue()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);

            var view = Project(state, _host.Id);

            Assert.IsTrue(view.IsBench);
            Assert.IsTrue(view.RecipientIsHost, "The real host id is preserved for host-only gating.");
            Assert.HasCount(2, view.Players);
            Assert.AreEqual(ModifierCardFactory.AllDealableIds.Count(), view.CardCatalogue.Count);
        }

        [TestMethod]
        public async Task ProjectFor_WhenLobby_IsNotBench_AndCatalogueEmpty()
        {
            using var state = await HostAloneAsync();

            var view = Project(state, _host.Id);

            Assert.IsFalse(view.IsBench);
            Assert.IsEmpty(view.CardCatalogue);
        }

        [TestMethod]
        public async Task BenchSetBay_ThenProject_ReflectsTheBay()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);
            var target = Project(state, _host.Id).Players[0].UserId;

            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchSetBay,
                Json(new BenchBayPayload(target, [ModifierId.Vanilla.ToString(), ModifierId.Speedracer.ToString()])));

            var bay = Project(state, _host.Id).Players.Single(p => p.UserId == target).EngineBay;
            CollectionAssert.AreEqual(
                new[] { ModifierId.Vanilla, ModifierId.Speedracer },
                bay.Select(c => c.Id).ToArray());
        }

        [TestMethod]
        public async Task BenchReset_WithNewPlayerCount_ReprojectsThatManyPlayers()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);
            Assert.HasCount(AlphaChainBenchScenario.MinPlayers, Project(state, _host.Id).Players);

            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchReset,
                Json(new BenchResetPayload(4)));

            Assert.IsTrue(result.IsSuccess);
            Assert.HasCount(4, Project(state, _host.Id).Players);
        }

        [TestMethod]
        public async Task BenchSetBan_ThenProject_SurfacesUpperCasedBannedLetter()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);

            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchSetBan,
                Json(new BenchBanPayload("z")));

            Assert.IsTrue(result.IsSuccess);
            // The projector upper-cases the (lower-case) stored ban for display.
            Assert.AreEqual("Z", Project(state, _host.Id).BannedLetter);
        }

        [TestMethod]
        public async Task BenchSetScore_ThenProject_ReflectsTheScore()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);
            var target = Project(state, _host.Id).Players[0].UserId;

            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchSetScore,
                Json(new BenchScorePayload(target, 42)));

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(42, Project(state, _host.Id).Players.Single(p => p.UserId == target).Score);
        }

        [TestMethod]
        public async Task BenchSkip_AdvancesTheActiveSeat()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);
            var before = Project(state, _host.Id).CurrentPlayerId;

            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchSkip, null);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreNotEqual(before, Project(state, _host.Id).CurrentPlayerId,
                "Skipping hands the turn to the next seat without playing a word.");
        }

        [TestMethod]
        public async Task BenchSubmit_AcceptedWord_ProjectsReplay_EvenWithEmptyBay()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);

            // BenchWordListService accepts any all-letter token; the era-1 opening word is a free choice.
            var result = await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchSubmit,
                Json(new BenchSubmitPayload("cat", null)));

            Assert.IsTrue(result.IsSuccess);
            // The bench branch projects the replay unconditionally (no HasAnimation gate), so even a
            // bare word over an empty bay surfaces its breakdown.
            Assert.IsNotNull(Project(state, _host.Id).LatestReplay);
        }

        // ── Notification bridge (the load-bearing invariant) ──

        [TestMethod]
        public async Task BenchCommand_FiresLobbyStateChanged_SoTheCoordinatorReprojects()
        {
            using var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);

            bool fired = false;
            using var sub = state.StateChangedEventManager.Subscribe(() => { fired = true; return ValueTask.CompletedTask; });

            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchSkip, null);

            Assert.IsTrue(fired, "A bench mutation must ping the lobby state so the view coordinator re-projects.");
        }

        // ── Disposal (no leaked inner state) ──

        [TestMethod]
        public async Task DisposingLobbyState_DisposesTheBench()
        {
            var state = await HostAloneAsync();
            await Hub.HandleCommandAsync(_host, state, AlphaChainCommands.BenchEnter, null);
            var bench = state.Bench!;

            state.Dispose();

            Assert.IsFalse(bench.IsReady, "The bench's inner state must be disposed when the lobby tears down.");
        }
    }
}
