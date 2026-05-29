using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Tracery.Tests.Helpers;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.Tracery.Tests.Unit.Logic.Games
{
    [TestClass]
    public class TraceryGameEngineTests
    {
        private Mock<ILogger<TraceryGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<TraceryGameState>> _stateLoggerMock = default!;
        private Mock<IWordListService> _wordListServiceMock = default!;
        private User _host = default!;
        private TraceryGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<TraceryGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<TraceryGameState>>();
            // These lifecycle tests don't build the dictionary trie, so a bare mock
            // word service is enough — GetSolver/GetGenerator are exercised separately.
            _wordListServiceMock = new Mock<IWordListService>();
            _host = UserFactory.Create("Host", "host1");
            // A real RNG (not an empty SequentialRng): EnterPlaying now draws letters during
            // board generation. With the bare mock word service the trie is empty, so Generate
            // fails gracefully to the empty-board branch — exactly what these phase/lifecycle
            // tests exercise. Tests needing a real board build their own engine with the real
            // WordListService.
            _engine = new TraceryGameEngine(
                _wordListServiceMock.Object, new RandomNumberService(), _engineLoggerMock.Object, _stateLoggerMock.Object);
        }

        // ── Generator wiring ────────────────────────────────────────────────

        [TestMethod]
        public void GetGenerator_BuildsOnce_AndReusesTheSameInstance()
        {
            // Use the real word service so the trie/solver the generator depends on actually build.
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            var engine = new TraceryGameEngine(
                svc, new RandomNumberService(),
                NullLogger<TraceryGameEngine>.Instance, NullLogger<TraceryGameState>.Instance);

            GridGenerator first = engine.GetGenerator();
            GridGenerator second = engine.GetGenerator();

            Assert.IsNotNull(first);
            Assert.AreSame(first, second);
        }

        // ── Construction / lifecycle ────────────────────────────────────────

        [TestMethod]
        public void PlayerCountRange_IsTwoToEight()
        {
            Assert.AreEqual(2, _engine.MinPlayerCount);
            Assert.AreEqual(8, _engine.MaxPlayerCount);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsJoinableState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (TraceryGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public async Task CreateStateAsync_NullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_AsHost_FlipsJoinableOff()
        {
            var state = await CreateStateAsync();

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public async Task StartAsync_NonHost_ReturnsError()
        {
            var state = await CreateStateAsync();
            var stranger = UserFactory.Create("Stranger", "stranger1");

            var result = await _engine.StartAsync(stranger, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ── Start: phase + roster freeze ────────────────────────────────────

        [TestMethod]
        public async Task StartAsync_AsHost_AdvancesPastLobbyAndFreezesParticipants()
        {
            var state = await CreateStateAsync();
            // Long timers so no scheduled callback fires before the synchronous assertions.
            state.UpdateSettings(s => s with { TransitionDuration = TimeSpan.FromMinutes(5) });

            var start = DateTimeOffset.UtcNow;
            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.RoundIntro, state.Phase);
            Assert.AreEqual(0, state.CurrentRound);
            Assert.IsNotNull(state.PhaseExpiresAtUtc);
            Assert.IsTrue(state.PhaseExpiresAtUtc!.Value >= start);
            // Host alone → participates, and the frozen roster captures the host.
            Assert.HasCount(1, state.Participants);
            Assert.AreEqual(_host.Id, state.Participants[0].User.Id);
        }

        [TestMethod]
        public async Task StartAsync_HostSolo_HostParticipates()
        {
            var state = await CreateStateAsync();

            await _engine.StartAsync(_host, state);

            Assert.IsTrue(state.HostIsParticipant);
            Assert.IsTrue(state.PlayerStates.ContainsKey(_host.Id));
        }

        [TestMethod]
        public async Task StartAsync_WithOtherPlayers_HostBecomesObserver()
        {
            var state = await CreateStateAsync();
            var players = RegisterPlayers(state, 2);

            await _engine.StartAsync(_host, state);

            Assert.IsFalse(state.HostIsParticipant);
            Assert.IsFalse(state.PlayerStates.ContainsKey(_host.Id));
            Assert.IsTrue(state.PlayerStates.ContainsKey(players[0].Id));
            Assert.IsTrue(state.PlayerStates.ContainsKey(players[1].Id));
        }

        [TestMethod]
        public async Task StartAsync_WithOtherPlayersAndHostPlaysAlong_HostParticipates()
        {
            var state = await CreateStateAsync();
            RegisterPlayers(state, 2);
            state.UpdateSettings(s => s with { HostPlaysAlong = true });

            await _engine.StartAsync(_host, state);

            Assert.IsTrue(state.HostIsParticipant);
            Assert.IsTrue(state.PlayerStates.ContainsKey(_host.Id));
        }

        // ── Placeholder phase progression ───────────────────────────────────

        [TestMethod]
        public async Task DrivingPastLastRound_LandsOnFinalStandings()
        {
            var state = await CreateStateAsync();
            state.UpdateSettings(s => s with
            {
                TotalRounds = 2,
                TransitionDuration = TimeSpan.FromMinutes(5),
                RoundTimer = TimeSpan.FromMinutes(5)
            });

            await _engine.StartAsync(_host, state);
            Assert.AreEqual(GamePhase.RoundIntro, state.Phase);

            // Drive the placeholder flow directly (no wall-clock waits) through both rounds.
            for (int round = 1; round <= 2; round++)
            {
                state.Execute(() => _engine.EnterPlaying(state));
                Assert.AreEqual(GamePhase.Playing, state.Phase);
                Assert.AreEqual(round, state.CurrentRound);

                state.Execute(() => _engine.CompleteRound(state));
                Assert.AreEqual(GamePhase.Reveal, state.Phase);

                state.Execute(() => _engine.EnterRoundOver(state));
                Assert.AreEqual(GamePhase.RoundOver, state.Phase);

                state.Execute(() => _engine.AdvanceAfterResults(state));
            }

            Assert.AreEqual(GamePhase.FinalStandings, state.Phase);
            Assert.IsNull(state.PhaseExpiresAtUtc);
            Assert.HasCount(2, state.RoundResults);
        }

        // ── Round activation: board generation & input gate ─────────────────

        [TestMethod]
        public async Task EnterPlaying_PopulatesGridFindableWordsAndActivatesRound()
        {
            // Real services so the trie/solver/generator actually produce a board.
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            var engine = new TraceryGameEngine(
                svc, new RandomNumberService(),
                NullLogger<TraceryGameEngine>.Instance, NullLogger<TraceryGameState>.Instance);

            var createResult = await engine.CreateStateAsync(_host);
            Assert.IsTrue(createResult.TryGetSuccess(out var created));
            var state = (TraceryGameState)created!;
            state.UpdateSettings(s => s with
            {
                TransitionDuration = TimeSpan.FromMinutes(5),
                RoundTimer = TimeSpan.FromMinutes(5)
            });

            await engine.StartAsync(_host, state);

            var start = DateTimeOffset.UtcNow;
            state.Execute(() => engine.EnterPlaying(state));

            Assert.AreEqual(GamePhase.Playing, state.Phase);
            Assert.IsTrue(state.IsRoundActive);
            Assert.IsNotNull(state.CurrentGrid);
            Assert.AreEqual(state.Settings.GridWidth, state.CurrentGrid!.Width);
            Assert.AreEqual(state.Settings.GridHeight, state.CurrentGrid.Height);
            Assert.IsTrue(state.FindableWords.Count > 0, "Generated board should expose findable words.");
            Assert.IsNotNull(state.RoundStartTime);
            Assert.IsTrue(state.RoundStartTime!.Value >= start.AddSeconds(-1));
        }

        [TestMethod]
        public async Task EnterPlaying_TimedRound_SetsPhaseExpiry()
        {
            var state = await CreateStateAsync();
            state.UpdateSettings(s => s with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            await _engine.StartAsync(_host, state);

            state.Execute(() => _engine.EnterPlaying(state));

            Assert.AreEqual(GamePhase.Playing, state.Phase);
            Assert.IsTrue(state.IsRoundActive);
            Assert.IsNotNull(state.PhaseExpiresAtUtc);
        }

        [TestMethod]
        public async Task EnterPlaying_UnlimitedTimer_LeavesPhaseExpiryNull()
        {
            var state = await CreateStateAsync();
            state.UpdateSettings(s => s with
            {
                RoundTimer = TimeSpan.Zero,
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            await _engine.StartAsync(_host, state);

            state.Execute(() => _engine.EnterPlaying(state));

            Assert.AreEqual(GamePhase.Playing, state.Phase);
            Assert.IsTrue(state.IsRoundActive);
            Assert.IsNull(state.PhaseExpiresAtUtc);
        }

        [TestMethod]
        public async Task RoundIntro_HasInputGateClosed()
        {
            var state = await CreateStateAsync();
            state.UpdateSettings(s => s with { TransitionDuration = TimeSpan.FromMinutes(5) });

            await _engine.StartAsync(_host, state);

            Assert.AreEqual(GamePhase.RoundIntro, state.Phase);
            Assert.IsFalse(state.IsRoundActive);
        }

        [TestMethod]
        public async Task CompleteRound_ClosesInputGate()
        {
            var state = await DriveIntoPlaying();
            Assert.IsTrue(state.IsRoundActive);

            state.Execute(() => _engine.CompleteRound(state));

            Assert.IsFalse(state.IsRoundActive);
            Assert.AreEqual(GamePhase.Reveal, state.Phase);
        }

        [TestMethod]
        public async Task FinalStandings_HasInputGateClosed()
        {
            var state = await CreateStateAsync();
            state.UpdateSettings(s => s with { TransitionDuration = TimeSpan.FromMinutes(5) });
            await _engine.StartAsync(_host, state);

            state.Execute(() => _engine.EnterFinalStandings(state));

            Assert.AreEqual(GamePhase.FinalStandings, state.Phase);
            Assert.IsFalse(state.IsRoundActive);
        }

        // ── EndRoundIfStillActive: timer-fire guard ─────────────────────────

        [TestMethod]
        public async Task EndRoundIfStillActive_CompletesRound_WhenStillActive()
        {
            var state = await DriveIntoPlaying();
            int round = state.CurrentRound;

            state.Execute(() => _engine.EndRoundIfStillActive(state, round));

            Assert.AreEqual(GamePhase.Reveal, state.Phase);
            Assert.IsFalse(state.IsRoundActive);
        }

        [TestMethod]
        public async Task EndRoundIfStillActive_NoOps_WhenRoundAlreadyAdvanced()
        {
            var state = await DriveIntoPlaying();
            int staleRound = state.CurrentRound;

            // Advance to the next Playing round (default TotalRounds = 3, so there's room).
            state.Execute(() => _engine.CompleteRound(state));      // → Reveal
            state.Execute(() => _engine.EnterRoundOver(state));     // → RoundOver
            state.Execute(() => _engine.AdvanceAfterResults(state));// → RoundIntro
            state.Execute(() => _engine.EnterPlaying(state));       // → Playing (next round)

            var phaseBefore = state.Phase;
            var roundBefore = state.CurrentRound;
            Assert.AreNotEqual(staleRound, roundBefore);

            // A stale timer captured for the earlier round must not end the current one.
            state.Execute(() => _engine.EndRoundIfStillActive(state, staleRound));

            Assert.AreEqual(phaseBefore, state.Phase);
            Assert.AreEqual(roundBefore, state.CurrentRound);
            Assert.IsTrue(state.IsRoundActive);
        }

        // ── Disconnect mid-round ────────────────────────────────────────────

        [TestMethod]
        public async Task PlayerDisconnectMidRound_DoesNotHang_AndRoundStillCompletes()
        {
            var state = await CreateStateAsync();
            // Two players join → host observes. Keep p1's registration token so we can drop it.
            var p1 = UserFactory.Create("P1", "p1");
            var p2 = UserFactory.Create("P2", "p2");
            var p1Token = state.RegisterPlayer(p1);
            Assert.IsTrue(p1Token.TryGetSuccess(out var p1Registration));
            Assert.IsTrue(state.RegisterPlayer(p2).IsSuccess);

            state.UpdateSettings(s => s with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            await _engine.StartAsync(_host, state);
            state.Execute(() => _engine.EnterPlaying(state));

            // Both bank a word, then p1 drops mid-round (circuit lost → registration token disposed,
            // which fires PlayerUnregistered and removes p1 from the live roster).
            state.Execute(() =>
            {
                state.CreatePlayerState(p1.Id).Bank(new TracedWord("rate", [0]));
                state.CreatePlayerState(p2.Id).Bank(new TracedWord("table", [0]));
            });
            p1Registration!.Dispose();

            // The round must still close cleanly off the timer path — no "wait for everyone" gate
            // can hang on the missing player.
            state.Execute(() => _engine.CompleteRound(state));

            Assert.AreEqual(GamePhase.Reveal, state.Phase);
            Assert.IsFalse(state.IsRoundActive);

            // The surviving player is scored; the round result is built from the live roster, so the
            // leaver simply isn't in this round's outcomes — and the round didn't hang waiting on them.
            var outcomes = state.RoundResults[^1].Outcomes;
            Assert.IsTrue(outcomes.Any(o => o.UserId == p2.Id), "The remaining player must be scored.");

            // The match can still be driven to its end despite the disconnect.
            state.Execute(() => _engine.EnterRoundOver(state));
            state.Execute(() => _engine.AdvanceAfterResults(state));
            Assert.AreNotEqual(GamePhase.Playing, state.Phase);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // Starts a match (mock word service → fail-safe empty board) and drives into the
        // first Playing round with long timers so no scheduled callback fires mid-assert.
        private async Task<TraceryGameState> DriveIntoPlaying()
        {
            var state = await CreateStateAsync();
            state.UpdateSettings(s => s with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            await _engine.StartAsync(_host, state);
            state.Execute(() => _engine.EnterPlaying(state));
            return state;
        }

        private async Task<TraceryGameState> CreateStateAsync()
        {
            var result = await _engine.CreateStateAsync(_host);
            Assert.IsTrue(result.TryGetSuccess(out var state));
            return (TraceryGameState)state!;
        }

        private static List<User> RegisterPlayers(TraceryGameState state, int count)
        {
            var players = new List<User>();
            for (int i = 0; i < count; i++)
            {
                var player = UserFactory.Create($"P{i + 1}", Guid.NewGuid().ToString());
                Assert.IsTrue(state.RegisterPlayer(player).IsSuccess);
                players.Add(player);
            }
            return players;
        }
    }
}
