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
        private WordSource _wordSource = default!;
        private SequentialRng _rng = default!;
        private User _host = default!;
        private LinkedListGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<LinkedListGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<LinkedListGameState>>();
            _wordSource = new WordSource(new FakeWordListService());
            _rng = new SequentialRng(0);
            _host = UserFactory.Create("Host", Guid.NewGuid());

            _engine = new LinkedListGameEngine(
                _wordSource,
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
                state.RegisterPlayer(UserFactory.Create($"P{i}", Guid.NewGuid()));
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
            Assert.AreNotEqual(Guid.Empty, state.AuditorPlayerId);
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

        [TestMethod]
        public async Task StartAsync_WithHostPlaying_SeatsHostAsParticipant()
        {
            var state = await CreateWithPlayersAsync(3);
            state.UpdateSettings(s => s with { HostPlays = true });

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue(startResult.IsSuccess);
            // 3 registered players + the host.
            Assert.AreEqual(4, state.GamePlayers.Count);
            Assert.AreEqual(4, state.TurnManager.TurnOrder.Count);
            CollectionAssert.Contains(state.ParticipantOrder, _host.Id);
            Assert.IsTrue(state.GamePlayers.ContainsKey(_host.Id));
        }

        [TestMethod]
        public async Task StartAsync_WithHostNotPlaying_OmitsHostFromParticipants()
        {
            var state = await CreateWithPlayersAsync(3);

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue(startResult.IsSuccess);
            Assert.AreEqual(3, state.GamePlayers.Count);
            CollectionAssert.DoesNotContain(state.ParticipantOrder, _host.Id);
            Assert.IsFalse(state.GamePlayers.ContainsKey(_host.Id));
        }

        // ── Core gameplay loop (Milestone 2) ─────────────────────────────────

        /// <summary>
        /// Starts a 3-player game with fixed start/destination words so tests can
        /// drive the loop deterministically. With HostPlays off the turn order
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
            => UserFactory.Create("Submitter", state.TurnManager.CurrentPlayer!.Value);

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
        public async Task Reject_LogsAttempt_IncrementsCounters()
        {
            var state = await StartedGameAsync();
            var submitter = SubmitterOf(state);
            _engine.SubmitPair(submitter, state, "boat");

            Assert.IsTrue(_engine.Reject(AuditorOf(state), state).IsSuccess);

            Assert.AreEqual(1, state.RejectionLog.Count);
            Assert.AreEqual("boat", state.RejectionLog[0].AttemptedWord);
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
                Assert.IsTrue(_engine.Reject(AuditorOf(state), state).IsSuccess);
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
                Assert.IsTrue(_engine.Reject(AuditorOf(state), state).IsSuccess);
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

        // ── Host "end round now" escape hatch (§10.3) ────────────────────────

        [TestMethod]
        public async Task EndRound_ByHost_FinalizesFromPartialProgress_ToRoundOver()
        {
            var state = await StartedGameAsync(start: "START", destination: "FINISH");
            SubmitAndApprove(state, "boat"); // one accepted pair; destination not reached

            Assert.IsTrue(_engine.EndRound(_host, state).IsSuccess);

            Assert.AreEqual(LinkedListGamePhase.RoundOver, state.Phase);
            Assert.IsFalse(state.DestinationReached);
            Assert.IsNotNull(state.LastRoundResult);
            Assert.AreEqual(1, state.LastRoundResult!.Guesses);
            Assert.IsFalse(state.LastRoundResult.DestinationReached);
        }

        [TestMethod]
        public async Task EndRound_ClearsPendingSubmission()
        {
            var state = await StartedGameAsync();
            Assert.IsTrue(_engine.SubmitPair(SubmitterOf(state), state, "boat").IsSuccess);
            Assert.IsNotNull(state.PendingSubmission);

            Assert.IsTrue(_engine.EndRound(_host, state).IsSuccess);

            Assert.AreEqual(LinkedListGamePhase.RoundOver, state.Phase);
            Assert.IsNull(state.PendingSubmission);
        }

        [TestMethod]
        public async Task EndRound_ByNonHost_Fails_RoundContinues()
        {
            var state = await StartedGameAsync();
            var player = SubmitterOf(state); // a participant, not the host

            Assert.IsTrue(_engine.EndRound(player, state).IsFailure);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
        }

        [TestMethod]
        public async Task EndRound_WhenNotPlaying_Fails()
        {
            var state = await CreateWithPlayersAsync(3); // still in Setup

            Assert.IsTrue(_engine.EndRound(_host, state).IsFailure);
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
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
                Assert.IsTrue(_engine.Reject(AuditorOf(state), state).IsSuccess);
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
            Assert.IsTrue(_engine.Reject(AuditorOf(state), state, now: T0.AddSeconds(20)).IsSuccess);
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

        // ── Milestone 4: auditor rotation, persona, reactions, match flow ────

        [TestMethod]
        public async Task RotateAuditorAndStartRound_AdvancesAuditorByOne_AndWraps()
        {
            var state = await StartedGameAsync();
            var order = state.TurnManager.TurnOrder;
            // Auto-assigned first Auditor is order[1] (first id that isn't submitter order[0]).
            Assert.AreEqual(order[1], state.AuditorPlayerId);
            Assert.AreEqual(1, state.AuditorRotationIndex);
            Assert.AreEqual(1, state.RoundNumber);

            Assert.IsTrue(_engine.RotateAuditorAndStartRound(state).IsSuccess);
            Assert.AreEqual(order[2], state.AuditorPlayerId);
            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
            // New Auditor is excluded from submitting that round.
            Assert.AreNotEqual(state.AuditorPlayerId, state.TurnManager.CurrentPlayer);

            Assert.IsTrue(_engine.RotateAuditorAndStartRound(state).IsSuccess);
            Assert.AreEqual(order[0], state.AuditorPlayerId); // wrapped to the front
            Assert.AreEqual(3, state.RoundNumber);
            Assert.AreNotEqual(state.AuditorPlayerId, state.TurnManager.CurrentPlayer);
        }

        [TestMethod]
        public async Task RotateAuditorAndStartRound_ResetsRoundData_PreservesMatchAccumulators()
        {
            var state = await StartedGameAsync(start: "HOUSE");
            var firstSubmitter = SubmitterOf(state);
            SubmitAndApprove(state, "boat"); // accepted pair → accumulator + chain link
            Assert.AreEqual(1, state.Chain.Count);
            Assert.AreEqual(1, state.GamePlayers[firstSubmitter.Id].AcceptedPairs);

            Assert.IsTrue(_engine.RotateAuditorAndStartRound(state).IsSuccess);

            Assert.AreEqual(0, state.Chain.Count);                // round data reset
            Assert.AreEqual(0, state.RejectionsThisTurn);
            Assert.IsFalse(state.DestinationReached);
            Assert.IsNull(state.PendingSubmission);
            Assert.AreEqual(state.StartWord, state.CarriedWord);
            // Match accumulator survives the rotation.
            Assert.AreEqual(1, state.GamePlayers[firstSubmitter.Id].AcceptedPairs);
        }

        [TestMethod]
        public async Task EndMatch_SetsGameOver()
        {
            var state = await StartedGameAsync();

            Assert.IsTrue(_engine.EndMatch(state).IsSuccess);

            Assert.AreEqual(LinkedListGamePhase.GameOver, state.Phase);
        }

        [TestMethod]
        public async Task RotateAuditorAndStartRound_AutoEndsAtRoundsPerMatch()
        {
            var state = await StartedGameAsync();
            state.UpdateSettings(s => s with { RoundsPerMatch = 2 });

            // Round 1 → 2 (1 < 2, so a real rotation).
            Assert.IsTrue(_engine.RotateAuditorAndStartRound(state).IsSuccess);
            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);

            // Round 2 has been played; the next rotate auto-ends the match.
            Assert.IsTrue(_engine.RotateAuditorAndStartRound(state).IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.GameOver, state.Phase);
            Assert.AreEqual(2, state.RoundNumber); // not incremented past the limit
        }

        [TestMethod]
        public async Task Superlatives_FewestGuesses_PickCorrectWinners()
        {
            var state = await StartedGameAsync();
            var order = state.TurnManager.TurnOrder;
            var pA = state.GamePlayers[order[0]];
            var pB = state.GamePlayers[order[1]];
            var pC = state.GamePlayers[order[2]];

            state.Execute(() =>
            {
                pA.AcceptedPairs = 3; pA.RejectionsReceived = 5;             // Most Rejected
                pB.AcceptedPairs = 4; pB.RejectionsReceived = 0;             // Speed Demon + Smooth Operator
                pC.AcceptedPairs = 1; pC.RejectionsReceived = 1; pC.LoopPairsMade = 2; // Loop Lord
            });

            Assert.IsTrue(_engine.EndMatch(state).IsSuccess);
            var sup = state.Superlatives;

            Assert.AreEqual(pA.PlayerId, sup.First(s => s.Title == "Most Rejected").PlayerId);
            Assert.AreEqual(pB.PlayerId, sup.First(s => s.Title == "Speed Demon").PlayerId);
            Assert.AreEqual(pC.PlayerId, sup.First(s => s.Title == "Loop Lord").PlayerId);
            Assert.AreEqual(pB.PlayerId, sup.First(s => s.Title == "Smooth Operator").PlayerId);
        }

        [TestMethod]
        public async Task Superlatives_FastestTime_SpeedDemonIsFastestContribution()
        {
            var state = await StartedGameAsync();
            state.UpdateSettings(s => s with { ScoringMode = ScoringMode.FastestTime });
            var order = state.TurnManager.TurnOrder;
            var pA = state.GamePlayers[order[0]];
            var pB = state.GamePlayers[order[1]];

            state.Execute(() =>
            {
                pA.AcceptedPairs = 2; pA.FastestContribution = TimeSpan.FromSeconds(10);
                pB.AcceptedPairs = 1; pB.FastestContribution = TimeSpan.FromSeconds(3);
            });

            Assert.IsTrue(_engine.EndMatch(state).IsSuccess);

            Assert.AreEqual(pB.PlayerId, state.Superlatives.First(s => s.Title == "Speed Demon").PlayerId);
        }

        [TestMethod]
        public async Task Superlatives_TiesBreakByAscendingPlayerId()
        {
            var state = await StartedGameAsync();
            var order = state.TurnManager.TurnOrder;
            var pA = state.GamePlayers[order[0]];
            var pC = state.GamePlayers[order[2]];

            // Two players tie on rejections; the ordinally-smaller id must win.
            state.Execute(() =>
            {
                pA.AcceptedPairs = 1; pA.RejectionsReceived = 3;
                pC.AcceptedPairs = 1; pC.RejectionsReceived = 3;
            });

            Assert.IsTrue(_engine.EndMatch(state).IsSuccess);

            var expected = pA.PlayerId.CompareTo(pC.PlayerId) <= 0 ? pA.PlayerId : pC.PlayerId;
            Assert.AreEqual(expected, state.Superlatives.First(s => s.Title == "Most Rejected").PlayerId);
        }

        // ── Milestone 5: Groups (competitive) ────────────────────────────────

        /// <summary>
        /// Starts a Groups match with <paramref name="groupCount"/> teams of
        /// <paramref name="perGroup"/> players each, fixed words, and a deterministic
        /// Auditor (the last participant). Group ids are "g0", "g1", … in order.
        /// </summary>
        private async Task<LinkedListGameState> StartedGroupsGameAsync(
            int perGroup = 2, int groupCount = 2,
            ScoringMode mode = ScoringMode.FewestGuesses,
            string start = "START", string destination = "FINISH")
        {
            int total = perGroup * groupCount;
            var state = await CreateWithPlayersAsync(total);
            var ids = state.Participants.Select(p => p.User.Id).ToList();

            var teams = new List<List<Guid>>();
            for (int i = 0; i < groupCount; i++)
                teams.Add(ids.Skip(i * perGroup).Take(perGroup).ToList());

            state.UpdateSettings(s => s with
            {
                PlayerStructure = PlayerStructure.Groups,
                ScoringMode = mode,
                RejectionCap = 3,
            });
            state.Execute(() =>
            {
                state.GroupAssignments = teams;
                state.StartWord = start;
                state.DestinationWord = destination;
                state.AuditorPlayerId = ids[^1]; // deterministic Auditor (last player)
            });
            await _engine.StartAsync(_host, state);
            return state;
        }

        private static User SubmitterOfGroup(ChainState g)
            => UserFactory.Create("Submitter", g.TurnManager.CurrentPlayer!.Value);

        private static void SeedChain(ChainState g, int count)
        {
            g.Chain.Clear();
            for (int i = 0; i < count; i++)
                g.Chain.Add(new ChainLink($"W{i}", $"W{i + 1}", Guid.NewGuid(), "P", false));
        }

        [TestMethod]
        public async Task Collective_RunsThroughASingleGroup()
        {
            var state = await StartedGameAsync(start: "HOUSE");

            Assert.AreEqual(1, state.Groups.Count);
            Assert.AreEqual("Everyone", state.Groups[0].GroupName);
            CollectionAssert.AreEquivalent(
                state.Participants.Select(p => p.User.Id).ToList(),
                state.Groups[0].MemberIds);
            // The single-chain accessors delegate to the only group.
            Assert.AreSame(state.Groups[0].Chain, state.Chain);
            Assert.AreEqual(state.Groups[0].CarriedWord, state.CarriedWord);
        }

        [TestMethod]
        public async Task Groups_Start_BuildsOneChainPerTeam()
        {
            var state = await StartedGroupsGameAsync(perGroup: 2, groupCount: 2, start: "HOUSE");

            Assert.AreEqual(2, state.Groups.Count);
            Assert.AreEqual("g0", state.Groups[0].GroupId);
            Assert.AreEqual("g1", state.Groups[1].GroupId);
            foreach (var g in state.Groups)
            {
                Assert.AreEqual(2, g.MemberIds.Count);
                Assert.AreEqual("HOUSE", g.CarriedWord);
                Assert.AreEqual(0, g.Chain.Count);
                // The seated submitter is never the Auditor.
                Assert.AreNotEqual(state.AuditorPlayerId, g.TurnManager.CurrentPlayer);
            }
        }

        [TestMethod]
        public async Task Groups_Approve_IsIsolatedToTheActingGroup()
        {
            var state = await StartedGroupsGameAsync(start: "HOUSE");
            var a = state.Groups[0];
            var b = state.Groups[1];

            Assert.IsTrue(_engine.SubmitPair(SubmitterOfGroup(a), state, "boat").IsSuccess);
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);

            // Group A advanced…
            Assert.AreEqual(1, a.Chain.Count);
            Assert.AreEqual("boat", a.CarriedWord);
            // …Group B is completely untouched.
            Assert.AreEqual(0, b.Chain.Count);
            Assert.AreEqual("HOUSE", b.CarriedWord);
            Assert.AreEqual(0, b.RejectionsThisTurn);
            Assert.IsNull(b.PendingSubmission);
            Assert.IsFalse(b.DestinationReached);
        }

        [TestMethod]
        public async Task Groups_RejectionCap_IsIsolatedPerGroup()
        {
            var state = await StartedGroupsGameAsync();
            var a = state.Groups[0];
            var b = state.Groups[1];

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(_engine.SubmitPair(SubmitterOfGroup(a), state, $"try{i}").IsSuccess);
                Assert.IsTrue(_engine.Reject(AuditorOf(state), state).IsSuccess);
            }

            Assert.AreEqual(0, a.RejectionsThisTurn); // forfeited and reset
            Assert.AreEqual(3, a.RejectionLog.Count);
            // Group B never saw a rejection.
            Assert.AreEqual(0, b.RejectionsThisTurn);
            Assert.AreEqual(0, b.RejectionLog.Count);
            Assert.AreEqual(0, b.Chain.Count);
        }

        [TestMethod]
        public async Task Groups_AuditQueue_ResolvesGroupsFifo()
        {
            var state = await StartedGroupsGameAsync(start: "HOUSE");
            var a = state.Groups[0];
            var b = state.Groups[1];

            // A submits, then B submits → FIFO queue [A, B]; A is at the front.
            Assert.IsTrue(_engine.SubmitPair(SubmitterOfGroup(a), state, "boat").IsSuccess);
            Assert.IsTrue(_engine.SubmitPair(SubmitterOfGroup(b), state, "raft").IsSuccess);
            Assert.AreEqual(2, state.AuditQueue.Count);
            Assert.AreEqual(a.GroupId, state.AuditingGroupId);

            // Resolving the front (A) advances the queue to B.
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);
            Assert.AreEqual(1, a.Chain.Count);
            Assert.AreEqual(b.GroupId, state.AuditingGroupId);

            // Resolving B drains the queue.
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);
            Assert.AreEqual(1, b.Chain.Count);
            Assert.AreEqual(0, state.AuditQueue.Count);
            Assert.IsNull(state.AuditingGroupId);
        }

        [TestMethod]
        public async Task Groups_RoundEnds_OnlyWhenEveryGroupReachesDestination()
        {
            var state = await StartedGroupsGameAsync(start: "HOUSE", destination: "OMEGA");
            var a = state.Groups[0];
            var b = state.Groups[1];

            // Group A reaches the destination first.
            Assert.IsTrue(_engine.SubmitPair(SubmitterOfGroup(a), state, "omega").IsSuccess);
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);
            Assert.IsTrue(a.DestinationReached);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase); // round still live

            // A finished group can't submit anymore.
            Assert.IsTrue(_engine.SubmitPair(SubmitterOfGroup(a), state, "extra").IsFailure);

            // When B also reaches it, the round ends.
            Assert.IsTrue(_engine.SubmitPair(SubmitterOfGroup(b), state, "omega").IsSuccess);
            Assert.IsTrue(_engine.Approve(AuditorOf(state), state).IsSuccess);
            Assert.IsTrue(b.DestinationReached);
            Assert.AreEqual(LinkedListGamePhase.RoundOver, state.Phase);
            Assert.AreEqual(2, state.LastStandings.Count);
        }

        [TestMethod]
        public async Task Groups_TieBreak_FewestGuesses_LowerTimeWins()
        {
            var state = await StartedGroupsGameAsync(mode: ScoringMode.FewestGuesses);
            var a = state.Groups[0];
            var b = state.Groups[1];

            state.Execute(() =>
            {
                SeedChain(a, 2);
                SeedChain(b, 2); // equal guess counts → tie on the primary metric
                a.DestinationReached = true; a.ElapsedThinkingTime = TimeSpan.FromSeconds(30); a.ThinkingSegmentStartedUtc = null;
                b.DestinationReached = true; b.ElapsedThinkingTime = TimeSpan.FromSeconds(10); b.ThinkingSegmentStartedUtc = null;
            });

            Assert.IsTrue(_engine.EndMatch(state).IsSuccess);

            var standings = state.LastStandings;
            Assert.AreEqual(b.GroupId, standings[0].GroupId); // less time breaks the guess tie
            Assert.AreEqual(1, standings[0].Rank);
            Assert.IsTrue(standings[0].IsTieBreakWinner);
            Assert.AreEqual(a.GroupId, standings[1].GroupId);
        }

        [TestMethod]
        public async Task Groups_TieBreak_FastestTime_FewerGuessesWins()
        {
            var state = await StartedGroupsGameAsync(mode: ScoringMode.FastestTime);
            var a = state.Groups[0];
            var b = state.Groups[1];

            state.Execute(() =>
            {
                SeedChain(a, 5);
                SeedChain(b, 3);
                a.DestinationReached = true; a.ElapsedThinkingTime = TimeSpan.FromSeconds(10); a.ThinkingSegmentStartedUtc = null;
                b.DestinationReached = true; b.ElapsedThinkingTime = TimeSpan.FromSeconds(10); b.ThinkingSegmentStartedUtc = null;
            });

            Assert.IsTrue(_engine.EndMatch(state).IsSuccess);

            var standings = state.LastStandings;
            Assert.AreEqual(b.GroupId, standings[0].GroupId); // fewer guesses breaks the time tie
            Assert.AreEqual(1, standings[0].Rank);
            Assert.IsTrue(standings[0].IsTieBreakWinner);
        }

        [TestMethod]
        public async Task Groups_Start_RejectsFewerThanTwoGroups()
        {
            var state = await CreateWithPlayersAsync(4);
            var ids = state.Participants.Select(p => p.User.Id).ToList();
            state.UpdateSettings(s => s with { PlayerStructure = PlayerStructure.Groups });
            state.Execute(() =>
            {
                state.GroupAssignments = [ids]; // everyone in one group
                state.StartWord = "ALPHA";
                state.DestinationWord = "OMEGA";
            });

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
            Assert.IsTrue(state.IsJoinable);
        }

        [TestMethod]
        public async Task Groups_Start_RejectsGroupWithFewerThanTwoMembers()
        {
            var state = await CreateWithPlayersAsync(4);
            var ids = state.Participants.Select(p => p.User.Id).ToList();
            state.UpdateSettings(s => s with { PlayerStructure = PlayerStructure.Groups });
            state.Execute(() =>
            {
                // 3 + 1 → the second group is too small.
                state.GroupAssignments = [ids.Take(3).ToList(), ids.Skip(3).ToList()];
                state.StartWord = "ALPHA";
                state.DestinationWord = "OMEGA";
            });

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
        }

        [TestMethod]
        public void AutoBalanceGroups_RoundRobinsPlayers()
        {
            var p1 = Guid.NewGuid();
            var p2 = Guid.NewGuid();
            var p3 = Guid.NewGuid();
            var p4 = Guid.NewGuid();
            var p5 = Guid.NewGuid();
            var teams = LinkedListGameEngine.AutoBalanceGroups([p1, p2, p3, p4, p5], 2);

            Assert.AreEqual(2, teams.Count);
            Assert.AreEqual(3, teams[0].Count); // p1, p3, p5
            Assert.AreEqual(2, teams[1].Count); // p2, p4
            CollectionAssert.AreEqual(new[] { p1, p3, p5 }, teams[0]);
            CollectionAssert.AreEqual(new[] { p2, p4 }, teams[1]);
        }

        // ── ReturnToLobby ─────────────────────────────────────────────────────

        [TestMethod]
        public async Task ReturnToLobby_NonHost_ReturnsError()
        {
            var state = await CreateWithPlayersAsync(3);
            await _engine.StartAsync(_host, state);
            state.SetPhase(LinkedListGamePhase.GameOver);
            var nonHost = UserFactory.Create("NotHost", Guid.NewGuid());

            var result = _engine.ReturnToLobby(nonHost, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task ReturnToLobby_BeforeGameOver_ReturnsError()
        {
            var state = await CreateWithPlayersAsync(3);
            await _engine.StartAsync(_host, state);
            // Phase is Playing, not GameOver — the replay path is rejected.

            var result = _engine.ReturnToLobby(_host, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task ReturnToLobby_AfterGameOver_ReturnsToJoinableSetup()
        {
            var state = await CreateWithPlayersAsync(3);
            await _engine.StartAsync(_host, state);
            state.SetPhase(LinkedListGamePhase.GameOver);

            var result = _engine.ReturnToLobby(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
            Assert.IsTrue(state.IsJoinable);
            Assert.IsEmpty(state.GamePlayers);
            Assert.IsEmpty(state.Groups);
            Assert.AreEqual(0, state.RoundNumber);
        }
    }
}
