using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using KnockBox.LinkedList.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.LinkedList.Tests.Unit.Logic
{
    [TestClass]
    public class LinkedListGameEngineTests
    {
        private Mock<ILogger<LinkedListGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<LinkedListGameState>> _stateLoggerMock = default!;
        private WordPairSource _wordPairSource = default!;
        private SequentialRng _rng = default!;
        private User _host = default!;
        private LinkedListGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<LinkedListGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<LinkedListGameState>>();
            _wordPairSource = new WordPairSource();
            _rng = new SequentialRng(0);
            _host = UserFactory.Create("Host", "host1");

            _engine = new LinkedListGameEngine(
                _wordPairSource,
                _rng,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private async Task<LinkedListGameState> CreateWithPlayersAsync(int playerCount)
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (LinkedListGameState)result.Value!;
            for (int i = 0; i < playerCount; i++)
            {
                state.RegisterPlayer(UserFactory.Create($"P{i}", $"p{i}"));
            }
            return state;
        }

        [TestMethod]
        public async Task CreateStateAsync_ReturnsJoinableSetupState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue(result.IsSuccess);
            var state = (LinkedListGameState)result.Value!;
            Assert.IsTrue(state.IsJoinable);
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
        }

        [TestMethod]
        public async Task PlayerBounds_AreThreeToTen()
        {
            await Task.CompletedTask;
            Assert.AreEqual(3, _engine.MinPlayerCount);
            Assert.AreEqual(10, _engine.MaxPlayerCount);
        }

        [TestMethod]
        public async Task StartAsync_WithThreePlayers_AdvancesToPlaying()
        {
            var state = await CreateWithPlayersAsync(3);

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue(startResult.IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
            Assert.IsFalse(state.IsJoinable);
            Assert.AreEqual(3, state.TurnManager.TurnOrder.Count);
            Assert.AreEqual(3, state.GamePlayers.Count);
            Assert.IsFalse(string.IsNullOrEmpty(state.StartWord));
            Assert.IsFalse(string.IsNullOrEmpty(state.DestinationWord));
            Assert.AreEqual(state.StartWord, state.CarriedWord);
            Assert.IsFalse(string.IsNullOrEmpty(state.AuditorPlayerId));
        }

        [TestMethod]
        public async Task StartAsync_AssignsAuditorThatIsNotTheCurrentSubmitter()
        {
            var state = await CreateWithPlayersAsync(3);

            await _engine.StartAsync(_host, state);

            Assert.AreNotEqual(state.TurnManager.CurrentPlayer, state.AuditorPlayerId);
            CollectionAssert.Contains(state.TurnManager.TurnOrder, state.AuditorPlayerId);
        }

        [TestMethod]
        public async Task StartAsync_HonorsHostChosenWords()
        {
            var state = await CreateWithPlayersAsync(3);
            state.Execute(() =>
            {
                state.StartWord = "ALPHA";
                state.DestinationWord = "OMEGA";
            });

            await _engine.StartAsync(_host, state);

            Assert.AreEqual("ALPHA", state.StartWord);
            Assert.AreEqual("OMEGA", state.DestinationWord);
            Assert.AreEqual("ALPHA", state.CarriedWord);
        }

        [TestMethod]
        public async Task CanStartAsync_FailsWithTooFewPlayers()
        {
            var state = await CreateWithPlayersAsync(2);

            Assert.IsFalse(await _engine.CanStartAsync(state));
        }

        [TestMethod]
        public async Task StartAsync_WithTooFewPlayers_StaysInSetup()
        {
            // StartAsyncCore itself runs (host-authorized), but with < min players the
            // lobby is not startable; verify CanStartAsync gates it.
            var state = await CreateWithPlayersAsync(2);

            Assert.IsFalse(await _engine.CanStartAsync(state));
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
            Assert.IsTrue(state.IsJoinable);
        }

        // ── Core gameplay loop (Milestone 2) ─────────────────────────────────

        /// <summary>
        /// Starts a 3-player game with fixed start/destination words so tests can
        /// drive the loop deterministically. With HostPlaysGame off the turn order
        /// is [p0, p1, p2], the first submitter is p0, and the auto-assigned
        /// Auditor is p1 (first id that isn't the submitter).
        /// </summary>
        private async Task<LinkedListGameState> StartedGameAsync(string start = "START", string destination = "FINISH")
        {
            var state = await CreateWithPlayersAsync(3);
            state.Execute(() =>
            {
                state.StartWord = start;
                state.DestinationWord = destination;
            });
            await _engine.StartAsync(_host, state);
            return state;
        }

        private static User SubmitterOf(LinkedListGameState state)
            => UserFactory.Create("Submitter", state.TurnManager.CurrentPlayer!);

        private static User AuditorOf(LinkedListGameState state)
            => UserFactory.Create("Auditor", state.AuditorPlayerId);

        [TestMethod]
        public async Task SubmitThenApprove_AdvancesCarriedWord_AppendsLink_IncrementsAcceptedPairs()
        {
            var state = await StartedGameAsync(start: "HOUSE");
            var submitter = SubmitterOf(state);

            Assert.IsTrue(_engine.SubmitPair(submitter, state, "boat").IsSuccess);
            Assert.IsNotNull(state.PendingSubmission);

            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);

            Assert.AreEqual("boat", state.CarriedWord);
            Assert.AreEqual(1, state.Chain.Count);
            Assert.AreEqual("HOUSE", state.Chain[0].FromWord);
            Assert.AreEqual("boat", state.Chain[0].ToWord);
            Assert.IsFalse(state.Chain[0].IsLoop);
            Assert.IsNull(state.PendingSubmission);
            Assert.AreEqual(1, state.GamePlayers[submitter.Id].AcceptedPairs);
        }

        [TestMethod]
        public async Task Approve_WhenProposedIsDestination_EndsRound()
        {
            var state = await StartedGameAsync(start: "HOUSE", destination: "OMEGA");
            var submitter = SubmitterOf(state);

            _engine.SubmitPair(submitter, state, "omega"); // case-insensitive match
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);

            Assert.IsTrue(state.DestinationReached);
            Assert.AreEqual(LinkedListGamePhase.RoundOver, state.Phase);
            Assert.AreEqual(1, state.Chain.Count);
        }

        [TestMethod]
        public async Task Reject_RequiresReason()
        {
            var state = await StartedGameAsync();
            var submitter = SubmitterOf(state);
            _engine.SubmitPair(submitter, state, "boat");

            var result = _engine.Reject(AuditorOf(state), state, "   ");

            Assert.IsTrue(result.IsFailure);
            Assert.IsNotNull(state.PendingSubmission); // not consumed by a failed reject
        }

        [TestMethod]
        public async Task Reject_LogsInfo_SetsLastReason_IncrementsCounters()
        {
            var state = await StartedGameAsync();
            var submitter = SubmitterOf(state);
            _engine.SubmitPair(submitter, state, "boat");

            Assert.IsTrue(_engine.Reject(AuditorOf(state), state, "not a real pair").IsSuccess);

            Assert.AreEqual(1, state.RejectionLog.Count);
            Assert.AreEqual("boat", state.RejectionLog[0].AttemptedWord);
            Assert.AreEqual("not a real pair", state.RejectionLog[0].Reason);
            Assert.AreEqual("not a real pair", state.LastRejectionReason);
            Assert.AreEqual(1, state.RejectionsThisTurn);
            Assert.AreEqual(1, state.GamePlayers[submitter.Id].RejectionsReceived);
            Assert.IsNull(state.PendingSubmission);
        }

        [TestMethod]
        public async Task RejectionCap_ForfeitsTurn_ResetsCounter_AdvancesSubmitter_ChainUnchanged()
        {
            var state = await StartedGameAsync();
            state.UpdateSettings(s => s with { RejectionCap = 3 });
            var submitter = SubmitterOf(state);

            for (int i = 0; i < 3; i++)
            {
                _engine.SubmitPair(submitter, state, $"try{i}");
                Assert.IsTrue(_engine.Reject(AuditorOf(state), state, "nope").IsSuccess);
            }

            Assert.AreEqual(0, state.RejectionsThisTurn);          // reset on forfeit
            Assert.AreEqual(0, state.Chain.Count);                 // chain stays put
            Assert.AreNotEqual(submitter.Id, state.TurnManager.CurrentPlayer); // turn advanced
            Assert.AreNotEqual(state.AuditorPlayerId, state.TurnManager.CurrentPlayer); // not the Auditor
        }

        [TestMethod]
        public async Task RejectionCap_Zero_NeverForfeits()
        {
            var state = await StartedGameAsync();
            state.UpdateSettings(s => s with { RejectionCap = 0 });
            var submitter = SubmitterOf(state);

            for (int i = 0; i < 5; i++)
            {
                _engine.SubmitPair(submitter, state, $"try{i}");
                Assert.IsTrue(_engine.Reject(AuditorOf(state), state, "nope").IsSuccess);
            }

            Assert.AreEqual(5, state.RejectionsThisTurn);
            Assert.AreEqual(submitter.Id, state.TurnManager.CurrentPlayer); // same submitter
        }

        [TestMethod]
        public async Task SubmitPair_RejectedWhenNotActiveSubmitter()
        {
            var state = await StartedGameAsync();
            // A non-active, non-auditor player attempts to submit.
            var intruderId = state.TurnManager.TurnOrder
                .First(id => id != state.TurnManager.CurrentPlayer && id != state.AuditorPlayerId);
            var intruder = UserFactory.Create("Intruder", intruderId);

            Assert.IsTrue(_engine.SubmitPair(intruder, state, "boat").IsFailure);
            Assert.IsNull(state.PendingSubmission);
        }

        [TestMethod]
        public async Task SubmitPair_RejectedWhenCallerIsAuditor()
        {
            var state = await StartedGameAsync();

            Assert.IsTrue(_engine.SubmitPair(AuditorOf(state), state, "boat").IsFailure);
            Assert.IsNull(state.PendingSubmission);
        }

        [TestMethod]
        public async Task SubmitPair_RejectedWhenSubmissionAlreadyPending()
        {
            var state = await StartedGameAsync();
            var submitter = SubmitterOf(state);
            Assert.IsTrue(_engine.SubmitPair(submitter, state, "boat").IsSuccess);

            Assert.IsTrue(_engine.SubmitPair(submitter, state, "raft").IsFailure);
            Assert.AreEqual("boat", state.PendingSubmission!.ProposedWord);
        }

        [TestMethod]
        public async Task NoImmediateRepeat_Off_AppendsLoopLink()
        {
            var state = await StartedGameAsync(start: "DOG");
            // Build DOG→HOUSE, HOUSE→DOG so DOG→HOUSE becomes a loop on the third pair.
            SubmitAndApprove(state, "HOUSE");
            SubmitAndApprove(state, "DOG");

            var submitter = SubmitterOf(state);
            Assert.IsTrue(_engine.SubmitPair(submitter, state, "HOUSE").IsSuccess);
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);

            var lastLink = state.Chain[^1];
            Assert.AreEqual("DOG", lastLink.FromWord);
            Assert.AreEqual("HOUSE", lastLink.ToWord);
            Assert.IsTrue(lastLink.IsLoop);
        }

        [TestMethod]
        public async Task NoImmediateRepeat_On_BlocksLoopPair()
        {
            var state = await StartedGameAsync(start: "DOG");
            SubmitAndApprove(state, "HOUSE");
            SubmitAndApprove(state, "DOG");
            state.UpdateSettings(s => s with { NoImmediateRepeat = true });

            var submitter = SubmitterOf(state);
            Assert.IsTrue(_engine.SubmitPair(submitter, state, "HOUSE").IsFailure);
            Assert.IsNull(state.PendingSubmission);
            Assert.AreEqual(2, state.Chain.Count); // no new link appended
        }

        [TestMethod]
        public async Task ApproveAdvance_SkipsTheAuditor()
        {
            var state = await StartedGameAsync();
            var firstSubmitter = state.TurnManager.CurrentPlayer;

            SubmitAndApprove(state, "boat");

            Assert.AreNotEqual(firstSubmitter, state.TurnManager.CurrentPlayer);
            Assert.AreNotEqual(state.AuditorPlayerId, state.TurnManager.CurrentPlayer);
        }

        private void SubmitAndApprove(LinkedListGameState state, string word)
        {
            Assert.IsTrue(_engine.SubmitPair(SubmitterOf(state), state, word).IsSuccess);
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);
        }

        // ── Milestone 3: scoring modes & timers ──────────────────────────────

        private static readonly DateTimeOffset T0 =
            new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Starts a 3-player game in Fastest Time mode (timers on) with fixed words.
        /// The first thinking segment is reset to a controlled <see cref="T0"/> so
        /// accrual is fully deterministic regardless of wall-clock time.
        /// </summary>
        private async Task<LinkedListGameState> StartedFastestTimeGameAsync(
            string start = "START", string destination = "FINISH")
        {
            var state = await CreateWithPlayersAsync(3);
            state.UpdateSettings(s => s with { ScoringMode = ScoringMode.FastestTime });
            state.Execute(() =>
            {
                state.StartWord = start;
                state.DestinationWord = destination;
            });
            await _engine.StartAsync(_host, state);
            state.Execute(() =>
            {
                state.ElapsedThinkingTime = TimeSpan.Zero;
                state.ThinkingSegmentStartedUtc = T0; // pin the running segment's start
            });
            return state;
        }

        [TestMethod]
        public async Task GuessCount_EqualsAcceptedPairs_IgnoringRejections()
        {
            var state = await StartedGameAsync(); // Fewest Guesses (default)
            state.UpdateSettings(s => s with { RejectionCap = 0 }); // never forfeit

            SubmitAndApprove(state, "alpha"); // accepted pair #1

            // Several rejections under the (unlimited) cap — these are free.
            var submitter = SubmitterOf(state);
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(_engine.SubmitPair(submitter, state, $"bad{i}").IsSuccess);
                Assert.IsTrue(_engine.Reject(AuditorOf(state), state, "nope").IsSuccess);
            }

            SubmitAndApprove(state, "beta"); // accepted pair #2

            Assert.AreEqual(2, state.GuessCount);
            Assert.AreEqual(2, state.Chain.Count);
            Assert.AreEqual(4, state.RejectionLog.Count); // rejections tracked but not scored
        }

        [TestMethod]
        public async Task FastestTime_BanksThinkingSegments_ExcludesAuditGaps()
        {
            var state = await StartedFastestTimeGameAsync(start: "HOUSE", destination: "OMEGA");
            var submitter = SubmitterOf(state);

            // Think 10s, then submit → banks 10s and pauses for auditing.
            Assert.IsTrue(_engine.SubmitPair(submitter, state, "boat", now: T0.AddSeconds(10)).IsSuccess);
            Assert.AreEqual(TimeSpan.FromSeconds(10), state.ElapsedThinkingTime);
            Assert.IsFalse(state.ClockRunning); // paused during audit

            // Auditor deliberates 30s (no accrual), approves at T0+40 → next submitter thinking.
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state, now: T0.AddSeconds(40)).IsSuccess);
            Assert.AreEqual(TimeSpan.FromSeconds(10), state.ElapsedThinkingTime); // audit gap excluded
            Assert.IsTrue(state.ClockRunning);

            // Pin the new segment's start to T0+40 (the engine set it; assert and continue).
            // Next submitter thinks 15s and submits the destination at T0+55.
            var nextSubmitter = SubmitterOf(state);
            Assert.IsTrue(_engine.SubmitPair(nextSubmitter, state, "omega", now: T0.AddSeconds(55)).IsSuccess);
            Assert.AreEqual(TimeSpan.FromSeconds(25), state.ElapsedThinkingTime); // 10 + 15

            // Auditor approves the destination at T0+70 → round finalizes.
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state, now: T0.AddSeconds(70)).IsSuccess);

            Assert.AreEqual(LinkedListGamePhase.RoundOver, state.Phase);
            Assert.IsNotNull(state.LastRoundResult);
            Assert.AreEqual(ScoringMode.FastestTime, state.LastRoundResult!.Mode);
            Assert.AreEqual(TimeSpan.FromSeconds(25), state.LastRoundResult.Elapsed); // only thinking time
            Assert.IsTrue(state.LastRoundResult.DestinationReached);
            Assert.IsFalse(state.ClockRunning);
        }

        [TestMethod]
        public async Task FastestTime_RejectedAttempt_CostsTheTimeSpentThinking()
        {
            var state = await StartedFastestTimeGameAsync();
            state.UpdateSettings(s => s with { RejectionCap = 0 }); // keep the same submitter
            // Re-pin: UpdateSettings ran an Execute but didn't touch the clock; segment start is still T0.
            var submitter = SubmitterOf(state);

            // Think 8s on a doomed word, submit at T0+8 → banks 8s, pauses.
            Assert.IsTrue(_engine.SubmitPair(submitter, state, "bad", now: T0.AddSeconds(8)).IsSuccess);
            Assert.AreEqual(TimeSpan.FromSeconds(8), state.ElapsedThinkingTime);

            // Auditor deliberates 12s then rejects at T0+20 → same submitter resumes.
            Assert.IsTrue(_engine.Reject(AuditorOf(state), state, "not a pair", now: T0.AddSeconds(20)).IsSuccess);
            Assert.AreEqual(TimeSpan.FromSeconds(8), state.ElapsedThinkingTime); // rejected time NOT refunded
            Assert.IsTrue(state.ClockRunning);                                   // clock resumed for retry
            Assert.AreEqual(submitter.Id, state.TurnManager.CurrentPlayer);      // still their turn

            // Think 5 more seconds, submit a good word at T0+25 → banks 5s → total 13s.
            Assert.IsTrue(_engine.SubmitPair(submitter, state, "boat", now: T0.AddSeconds(25)).IsSuccess);
            Assert.AreEqual(TimeSpan.FromSeconds(13), state.ElapsedThinkingTime); // 8 (rejected) + 5 (accepted)
        }

        [TestMethod]
        public async Task RoundResult_Par_BeatWhenAtOrUnder()
        {
            var state = await StartedGameAsync(start: "HOUSE", destination: "OMEGA");
            state.UpdateSettings(s => s with { Par = 1 });
            var submitter = SubmitterOf(state);

            _engine.SubmitPair(submitter, state, "omega");
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);

            Assert.IsNotNull(state.LastRoundResult);
            Assert.AreEqual(ScoringMode.FewestGuesses, state.LastRoundResult!.Mode);
            Assert.AreEqual(1, state.LastRoundResult.Guesses);
            Assert.AreEqual(1, state.LastRoundResult.Par);
            Assert.IsTrue(state.LastRoundResult.BeatPar);
        }

        [TestMethod]
        public async Task RoundResult_Par_NotBeatWhenOver()
        {
            var state = await StartedGameAsync(start: "HOUSE", destination: "OMEGA");
            state.UpdateSettings(s => s with { Par = 1 });

            SubmitAndApprove(state, "boat"); // guess #1 (advances submitter)
            // guess #2 reaches the destination → 2 > par 1.
            var submitter = SubmitterOf(state);
            _engine.SubmitPair(submitter, state, "omega");
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);

            Assert.AreEqual(2, state.LastRoundResult!.Guesses);
            Assert.AreEqual(1, state.LastRoundResult.Par);
            Assert.IsFalse(state.LastRoundResult.BeatPar);
        }

        [TestMethod]
        public async Task RoundResult_NullPar_NeverBeatsPar()
        {
            var state = await StartedGameAsync(start: "HOUSE", destination: "OMEGA");
            // Par left null (default).
            var submitter = SubmitterOf(state);

            _engine.SubmitPair(submitter, state, "omega");
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);

            Assert.IsNull(state.LastRoundResult!.Par);
            Assert.IsFalse(state.LastRoundResult.BeatPar);
        }
    }
}
