using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress.FSM
{
    [TestClass]
    public class CoinFlipStateTests
    {
        private Mock<ILogger<DrawnToDressGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _host = new User("Host", "host1");
            _engine = new DrawnToDressGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private async Task<(DrawnToDressGameState state, DrawnToDressGameContext context)> CreateGameAsync()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            state.GamePlayers["pA"] = new() { PlayerId = "pA", DisplayName = "Player A" };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", DisplayName = "Player B" };
            state.VotingRounds.Add(new() { RoundNumber = 1 });
            return (state, state.Context!);
        }

        // ── Empty queue chains immediately ──────────────────────────────────

        [TestMethod]
        public async Task EmptyQueue_ChainsToReturnStateImmediately()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue = [];

            var returnState = new VotingRoundResultsState();
            context.Fsm.TransitionTo(context, new CoinFlipState(returnState));

            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);
        }

        // ── OnEnter sets caller ─────────────────────────────────────────────

        [TestMethod]
        public async Task OnEnter_SetsCallerAsOneOfTheAffectedPlayers_CriterionTie()
        {
            var (state, context) = await CreateGameAsync();
            var matchupId = Guid.NewGuid();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = matchupId,
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));

            Assert.IsInstanceOfType<CoinFlipState>(context.Fsm.CurrentState);
            var flip = state.PendingCoinFlipQueue[0];
            Assert.IsTrue(flip.CallerPlayerId == "pA" || flip.CallerPlayerId == "pB",
                "Caller must be one of the two affected players.");
        }

        [TestMethod]
        public async Task OnEnter_SetsCallerAsOneOfTheAffectedPlayers_FinalStandingsTie()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.FinalStandingsTie,
                    PlayerAId = "pA",
                    PlayerBId = "pB",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new FinalResultsDisplayState()));

            Assert.IsInstanceOfType<CoinFlipState>(context.Fsm.CurrentState);
            var flip = state.PendingCoinFlipQueue[0];
            Assert.IsTrue(flip.CallerPlayerId == "pA" || flip.CallerPlayerId == "pB",
                "Caller must be one of the two tied players.");
        }

        // ── CoinFlipCallCommand from caller resolves flip ───────────────────

        [TestMethod]
        public async Task CoinFlipCallCommand_FromCaller_ResolvesFlip()
        {
            var (state, context) = await CreateGameAsync();
            var matchupId = Guid.NewGuid();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = matchupId,
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));
            var caller = state.PendingCoinFlipQueue[0].CallerPlayerId;

            _engine.ProcessCommand(context, new CoinFlipCallCommand(caller, true));

            // Single flip resolved → chains to return state.
            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);
            Assert.IsTrue(state.PendingCoinFlipQueue[0].IsResolved);
            Assert.IsTrue(!string.IsNullOrEmpty(state.PendingCoinFlipQueue[0].WinnerPlayerId));
        }

        // ── CoinFlipCallCommand from non-caller is rejected ─────────────────

        [TestMethod]
        public async Task CoinFlipCallCommand_FromNonCaller_IsRejected()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));
            var caller = state.PendingCoinFlipQueue[0].CallerPlayerId;
            var nonCaller = caller == "pA" ? "pB" : "pA";

            _engine.ProcessCommand(context, new CoinFlipCallCommand(nonCaller, true));

            // Still in CoinFlipState, flip not resolved.
            Assert.IsInstanceOfType<CoinFlipState>(context.Fsm.CurrentState);
            Assert.IsFalse(state.PendingCoinFlipQueue[0].IsResolved);
        }

        // ── Timer expiry auto-resolves ──────────────────────────────────────

        [TestMethod]
        public async Task TimerExpiry_AutoResolvesWithRandomSelection()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));
            Assert.IsInstanceOfType<CoinFlipState>(context.Fsm.CurrentState);

            // Fast-forward past deadline.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddMinutes(1));

            // Should auto-resolve and chain to return state.
            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);
            Assert.IsTrue(state.PendingCoinFlipQueue[0].IsResolved);
            Assert.IsTrue(state.PendingCoinFlipQueue[0].IsAutoResolved,
                "Auto-resolved flip should have IsAutoResolved set to true.");
        }

        // ── Manual call does not set IsAutoResolved ──────────────────────────

        [TestMethod]
        public async Task ManualCall_DoesNotSetIsAutoResolved()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));
            var caller = state.PendingCoinFlipQueue[0].CallerPlayerId;
            _engine.ProcessCommand(context, new CoinFlipCallCommand(caller, true));

            Assert.IsTrue(state.PendingCoinFlipQueue[0].IsResolved);
            Assert.IsFalse(state.PendingCoinFlipQueue[0].IsAutoResolved,
                "Manually called flip should not have IsAutoResolved set.");
        }

        // ── Sequential flips advance correctly ──────────────────────────────

        [TestMethod]
        public async Task SequentialFlips_AdvanceCorrectly()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                },
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "theme_match",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                },
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "overall_look",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                },
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));

            // Resolve flip 1.
            var caller1 = state.PendingCoinFlipQueue[0].CallerPlayerId;
            _engine.ProcessCommand(context, new CoinFlipCallCommand(caller1, true));
            Assert.IsInstanceOfType<CoinFlipState>(context.Fsm.CurrentState);
            Assert.AreEqual(1, state.CurrentCoinFlipIndex);

            // Resolve flip 2.
            var caller2 = state.PendingCoinFlipQueue[1].CallerPlayerId;
            _engine.ProcessCommand(context, new CoinFlipCallCommand(caller2, false));
            Assert.IsInstanceOfType<CoinFlipState>(context.Fsm.CurrentState);
            Assert.AreEqual(2, state.CurrentCoinFlipIndex);

            // Resolve flip 3 → chains to return state.
            var caller3 = state.PendingCoinFlipQueue[2].CallerPlayerId;
            _engine.ProcessCommand(context, new CoinFlipCallCommand(caller3, true));
            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);

            // All three should be resolved.
            Assert.IsTrue(state.PendingCoinFlipQueue.All(f => f.IsResolved));
        }

        // ── Criterion flip results stored ───────────────────────────────────

        [TestMethod]
        public async Task CriterionFlip_StoresResultInCriterionCoinFlipResults()
        {
            var (state, context) = await CreateGameAsync();
            var matchupId = Guid.NewGuid();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = matchupId,
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));
            var caller = state.PendingCoinFlipQueue[0].CallerPlayerId;
            _engine.ProcessCommand(context, new CoinFlipCallCommand(caller, true));

            Assert.AreEqual(1, state.CriterionCoinFlipResults.Count);
            var result = state.CriterionCoinFlipResults[0];
            Assert.AreEqual(matchupId, result.MatchupId);
            Assert.AreEqual("creativity", result.CriterionId);
            Assert.IsTrue(result.WinnerEntrantId == "pA:1" || result.WinnerEntrantId == "pB:1");
        }

        // ── Final standings flip results ────────────────────────────────────

        [TestMethod]
        public async Task FinalStandingsFlip_SetsWinnerPlayerId()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.FinalStandingsTie,
                    PlayerAId = "pA",
                    PlayerBId = "pB",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new FinalResultsDisplayState()));
            var caller = state.PendingCoinFlipQueue[0].CallerPlayerId;
            _engine.ProcessCommand(context, new CoinFlipCallCommand(caller, false));

            Assert.IsInstanceOfType<FinalResultsDisplayState>(context.Fsm.CurrentState);
            var flip = state.PendingCoinFlipQueue[0];
            Assert.IsTrue(flip.IsResolved);
            Assert.IsTrue(flip.WinnerPlayerId == "pA" || flip.WinnerPlayerId == "pB");
        }

        // ── Pause / Abandon ─────────────────────────────────────────────────

        [TestMethod]
        public async Task PauseGameCommand_TransitionsToPausedState()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));
            _engine.ProcessCommand(context, new PauseGameCommand("host1"));

            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task AbandonGameCommand_TransitionsToAbandonedState()
        {
            var (state, context) = await CreateGameAsync();
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.CriterionTie,
                    MatchupId = Guid.NewGuid(),
                    CriterionId = "creativity",
                    EntrantAId = "pA:1",
                    EntrantBId = "pB:1",
                }
            ];

            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));
            _engine.ProcessCommand(context, new AbandonGameCommand("host1"));

            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
        }
    }
}
