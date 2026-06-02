using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Exercises the era-boundary Intermission: cards are dealt and bays expanded instantly on
    /// entry (no Deal/Expansion dwell), then the Optimization → Sniper Ban progression, the two
    /// player commands, and era advancement back into the round loop.
    /// </summary>
    [TestClass]
    public class IntermissionStateTests
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

        /// <summary>
        /// Starts a host-as-display game with <paramref name="playerCount"/> players and a fixed RNG
        /// (index 0 every draw → deterministic deals and ban draws). Optionally mutates settings.
        /// </summary>
        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            int playerCount = 4, Action<AlphaChainGameState>? configure = null)
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService(), new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            configure?.Invoke(state);

            await engine.StartAsync(_host, state);
            return (engine, state);
        }

        /// <summary>Transitions the live FSM into a fresh Intermission (OnEnter deals, expands,
        /// and opens Optimization directly — no Deal/Expansion dwell sub-phases).</summary>
        private static void EnterIntermission(AlphaChainGameState state)
            => state.Execute(() => state.Context!.Fsm.TransitionTo(state.Context, new IntermissionState()));

        // ── Deal + Expansion (applied instantly on entry) ──────────────────────

        [TestMethod]
        public async Task OnEnter_DealsConfiguredCounts_ToEveryActivePlayer_AndOpensOptimization()
        {
            var (_, state) = await StartGameAsync(playerCount: 4);
            using var _ = state;

            EnterIntermission(state);

            Assert.AreEqual(IntermissionSubPhase.Optimization, state.IntermissionPhase);
            foreach (var player in state.GamePlayers.Values)
            {
                Assert.AreEqual(state.Settings.ModifiersDealtPerEra, player.EngineBay.Count, "modifier deal count");
                Assert.AreEqual(state.Settings.ReactionsDealtPerEra, player.ReactionHand.Count, "reaction deal count");
                // Dealt cards are tracked so the Optimization panel can flag them NEW.
                Assert.AreEqual(state.Settings.ModifiersDealtPerEra, player.NewlyDealtModifierIds.Count, "new modifier ids");
                Assert.AreEqual(state.Settings.ReactionsDealtPerEra, player.NewlyDealtReactions.Count, "new reactions");
            }
        }

        [TestMethod]
        public async Task OnEnter_SkipsEliminatedPlayers()
        {
            var (_, state) = await StartGameAsync(playerCount: 4);
            using var _ = state;
            var eliminatedId = state.TurnManager.TurnOrder[0];
            state.Execute(() => state.MarkEliminated(state.GamePlayers[eliminatedId]));

            EnterIntermission(state);

            Assert.AreEqual(0, state.GamePlayers[eliminatedId].EngineBay.Count);
            Assert.AreEqual(0, state.GamePlayers[eliminatedId].ReactionHand.Count);
        }

        [TestMethod]
        public async Task OnEnter_AddsOneSlotToEachActivePlayer()
        {
            var (_, state) = await StartGameAsync(playerCount: 4);
            using var _ = state;
            int before = state.GamePlayers.Values.First().ModifierSlots;

            EnterIntermission(state);

            Assert.AreEqual(IntermissionSubPhase.Optimization, state.IntermissionPhase);
            foreach (var player in state.GamePlayers.Values)
                Assert.AreEqual(before + 1, player.ModifierSlots);
        }

        // ── Optimization ──────────────────────────────────────────────────────

        /// <summary>OnEnter opens Optimization directly (deal + expand are instant); asserts that
        /// and returns a base clock the caller can advance to time the sub-phase out.</summary>
        private static DateTimeOffset AdvanceToOptimization(AlphaChainGameEngine engine, AlphaChainGameState state)
        {
            Assert.AreEqual(IntermissionSubPhase.Optimization, state.IntermissionPhase);
            return DateTimeOffset.UtcNow;
        }

        [TestMethod]
        public async Task Optimization_SubmittedOrdering_AppliedToLiveBay()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2);
            using var _ = state;
            EnterIntermission(state);
            var clock = AdvanceToOptimization(engine, state);

            var playerId = state.TurnManager.TurnOrder[0];
            var dealt = state.GamePlayers[playerId].EngineBay.Select(c => c.Id).ToList();
            var reversed = dealt.AsEnumerable().Reverse().ToList();

            var result = await engine.SubmitOptimizationAsync(playerId, reversed, state);
            Assert.IsTrue(result.IsSuccess, "valid optimization should be accepted");

            // Live bay must not change until the sub-phase ends (fog-of-war).
            CollectionAssert.AreEqual(dealt, state.GamePlayers[playerId].EngineBay.Select(c => c.Id).ToList());

            // Optimization closes → submissions applied.
            clock = clock.AddSeconds(60);
            engine.Tick(state.Context!, clock);

            Assert.AreEqual(IntermissionSubPhase.SniperBan, state.IntermissionPhase);
            CollectionAssert.AreEqual(reversed, state.GamePlayers[playerId].EngineBay.Select(c => c.Id).ToList());
        }

        [TestMethod]
        public async Task Optimization_AllSubmitted_AdvancesBeforeTimeout()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2);
            using var _ = state;
            EnterIntermission(state);
            var clock = AdvanceToOptimization(engine, state);

            foreach (var id in state.TurnManager.TurnOrder)
            {
                var ids = state.GamePlayers[id].EngineBay.Select(c => c.Id).ToList();
                await engine.SubmitOptimizationAsync(id, ids, state);
            }

            // A tick still inside the optimization window advances because everyone is ready.
            clock = clock.AddSeconds(1);
            engine.Tick(state.Context!, clock);

            Assert.AreEqual(IntermissionSubPhase.SniperBan, state.IntermissionPhase);
        }

        [TestMethod]
        public async Task Optimization_NonSubmitter_KeepsDealtBay()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2);
            using var _ = state;
            EnterIntermission(state);
            var clock = AdvanceToOptimization(engine, state);

            var playerId = state.TurnManager.TurnOrder[0];
            var dealt = state.GamePlayers[playerId].EngineBay.Select(c => c.Id).ToList();

            // No submission; slots (4) >= cards (3) so nothing is discarded.
            clock = clock.AddSeconds(60);
            engine.Tick(state.Context!, clock);

            CollectionAssert.AreEqual(dealt, state.GamePlayers[playerId].EngineBay.Select(c => c.Id).ToList());
        }

        [TestMethod]
        public async Task Optimization_InvalidCardId_Rejected()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2);
            using var _ = state;
            EnterIntermission(state);
            AdvanceToOptimization(engine, state);

            var playerId = state.TurnManager.TurnOrder[0];
            var result = await engine.SubmitOptimizationAsync(playerId, ["not-a-real-card"], state);

            Assert.IsTrue(result.IsFailure);
            Assert.IsFalse(state.OptimizationSubmissions[playerId].Submitted);
        }

        [TestMethod]
        public async Task Optimization_LengthExceedsSlots_Rejected()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2);
            using var _ = state;
            EnterIntermission(state);
            AdvanceToOptimization(engine, state);

            var playerId = state.TurnManager.TurnOrder[0];
            // Slots are 4 after expansion; ask to keep more ids than that.
            var tooMany = state.GamePlayers[playerId].EngineBay.Select(c => c.Id).ToList();
            tooMany.AddRange(["x1", "x2", "x3"]); // 6 ids > 4 slots

            var result = await engine.SubmitOptimizationAsync(playerId, tooMany, state);

            Assert.IsTrue(result.IsFailure);
        }

        // ── Sniper Ban ──────────────────────────────────────────────────────────

        /// <summary>Fast-forwards through to the SniperBan sub-phase (no optimization submissions).</summary>
        private static DateTimeOffset AdvanceToSniperBan(AlphaChainGameEngine engine, AlphaChainGameState state)
        {
            var clock = AdvanceToOptimization(engine, state);
            clock = clock.AddSeconds(60);
            engine.Tick(state.Context!, clock); // Optimization → SniperBan
            Assert.AreEqual(IntermissionSubPhase.SniperBan, state.IntermissionPhase);
            return clock;
        }

        [TestMethod]
        public async Task SniperBan_LowestScoreActivePlayer_IsPicker()
        {
            var (engine, state) = await StartGameAsync(playerCount: 4);
            using var _ = state;
            var lowestId = state.TurnManager.TurnOrder[2];
            state.Execute(() =>
            {
                int s = 30;
                foreach (var id in state.TurnManager.TurnOrder)
                    state.GamePlayers[id].Score = s -= 5; // strictly descending, [2] not the min...
                state.GamePlayers[lowestId].Score = 1;     // ...force [2] to be the clear minimum.
            });
            EnterIntermission(state);

            AdvanceToSniperBan(engine, state);

            Assert.AreEqual(lowestId, state.SniperBanUserId);
        }

        [TestMethod]
        public async Task SniperBan_TieOnScore_BreaksByEarliestTurnOrder()
        {
            var (engine, state) = await StartGameAsync(playerCount: 4);
            using var _ = state;
            // All scores 0 → the minimum is tied; earliest turn-order index wins.
            EnterIntermission(state);

            AdvanceToSniperBan(engine, state);

            Assert.AreEqual(state.TurnManager.TurnOrder[0], state.SniperBanUserId);
        }

        [TestMethod]
        public async Task SniperBan_PickerSelectsLetter_SetsBanAndReturnsToRound()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2);
            using var _ = state;
            int eraBefore = state.CurrentEra;
            EnterIntermission(state);
            AdvanceToSniperBan(engine, state);

            var picker = state.SniperBanUserId!;
            var result = await engine.SelectSniperBanAsync(picker, 'q', state);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual('q', state.BannedLetter);
            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
            Assert.AreEqual(eraBefore + 1, state.CurrentEra);
        }

        [TestMethod]
        public async Task SniperBan_Timeout_PicksLegalLetterAndReturnsToRound()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2,
                configure: s => s.UpdateSettings(c => c with { BanMode = BanLetterMode.Vowels }));
            using var _ = state;
            EnterIntermission(state);
            var clock = AdvanceToSniperBan(engine, state);

            clock = clock.AddSeconds(60);
            engine.Tick(state.Context!, clock); // SniperBan timeout

            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
            Assert.IsTrue(state.BannedLetter is { } b && "aeiou".Contains(b), "timeout letter must be legal under Vowels");
        }

        [TestMethod]
        public async Task SniperBan_IllegalLetterUnderBanMode_Rejected()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2,
                configure: s => s.UpdateSettings(c => c with { BanMode = BanLetterMode.Vowels }));
            using var _ = state;
            EnterIntermission(state);
            AdvanceToSniperBan(engine, state);

            var picker = state.SniperBanUserId!;
            var result = await engine.SelectSniperBanAsync(picker, 'b', state); // consonant, illegal

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(IntermissionSubPhase.SniperBan, state.IntermissionPhase);
        }

        [TestMethod]
        public async Task SniperBan_NonPicker_Rejected()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2);
            using var _ = state;
            EnterIntermission(state);
            AdvanceToSniperBan(engine, state);

            var notPicker = state.TurnManager.TurnOrder.First(id => id != state.SniperBanUserId);
            var result = await engine.SelectSniperBanAsync(notPicker, 'q', state);

            Assert.IsTrue(result.IsFailure);
        }

        // ── Era progression ─────────────────────────────────────────────────────

        [TestMethod]
        public async Task EraProgression_RunsEraCountMinusOneIntermissions_ThenGameOver()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2,
                configure: s => s.UpdateSettings(c => c with { EraInterval = 2, EraCount = 3 }));
            using var _ = state;

            int intermissions = DriveMatchToGameOver(engine, state);

            Assert.AreEqual(AlphaChainGamePhase.GameOver, state.Phase);
            Assert.AreEqual(state.Settings.EraCount - 1, intermissions);
            Assert.AreEqual(state.Settings.EraCount, state.CurrentEra);
        }

        [TestMethod]
        public async Task FourPlayerTwoEra_Simulation_ProducesSaneResults()
        {
            var (engine, state) = await StartGameAsync(playerCount: 4,
                configure: s => s.UpdateSettings(c => c with { EraInterval = 2, EraCount = 2 }));
            using var _ = state;

            var order = state.TurnManager.TurnOrder;
            state.Execute(() =>
            {
                // Distinct scores so the leaderboard and sniper-ban picker are unambiguous.
                state.GamePlayers[order[0]].Score = 40;
                state.GamePlayers[order[1]].Score = 30;
                state.GamePlayers[order[2]].Score = 20;
                state.GamePlayers[order[3]].Score = 10;
            });

            int intermissions = DriveMatchToGameOver(engine, state);

            Assert.AreEqual(AlphaChainGamePhase.GameOver, state.Phase);
            Assert.AreEqual(1, intermissions, "2 eras → exactly 1 Intermission");
            Assert.AreEqual(2, state.CurrentEra);

            var results = state.Results!;
            Assert.AreEqual(4, results.Rankings.Count);
            Assert.AreEqual(order[0], results.WinnerUserId, "highest score wins in non-survival");
            CollectionAssert.AreEqual(
                new[] { order[0], order[1], order[2], order[3] },
                results.Rankings.Select(r => r.UserId).ToArray());
            // Every active player drew a full era's worth of cards at the one Intermission.
            foreach (var p in state.GamePlayers.Values)
                Assert.AreEqual(state.Settings.ModifiersDealtPerEra, p.EngineBay.Count);
        }

        /// <summary>
        /// Drives a match to completion using debug turn advances for rounds and time-skips for
        /// intermissions (no submissions → optimization + sniper-ban both time out). Returns the
        /// number of Intermissions observed.
        /// </summary>
        internal static int DriveMatchToGameOver(AlphaChainGameEngine engine, AlphaChainGameState state)
        {
            var clock = DateTimeOffset.UtcNow;
            int intermissions = 0;
            int guard = 0;

            while (state.Phase != AlphaChainGamePhase.GameOver && guard++ < 500)
            {
                if (state.Phase == AlphaChainGamePhase.Intermission)
                {
                    intermissions++;
                    int innerGuard = 0;
                    while (state.Phase == AlphaChainGamePhase.Intermission && innerGuard++ < 10)
                    {
                        clock = clock.AddSeconds(100);
                        engine.Tick(state.Context!, clock);
                    }
                }
                else
                {
                    engine.AdvanceTurnAsync(state.TurnManager.CurrentPlayer!, state).GetAwaiter().GetResult();
                }
            }

            return intermissions;
        }
    }
}
