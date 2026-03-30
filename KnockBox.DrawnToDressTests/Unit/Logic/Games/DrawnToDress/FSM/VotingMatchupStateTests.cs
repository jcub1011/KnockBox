using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using static KnockBox.Services.Logic.Games.DrawnToDress.FSM.DrawnToDressGameContext;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Tests for the voting matchup flow: eligibility enforcement, submit validation,
    /// late-vote marking, AllVotesCast criterion-level logic, and visibility-mode config.
    /// </summary>
    [TestClass]
    public class VotingMatchupStateTests
    {
        private Mock<ILogger<DrawnToDressGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private Mock<IRandomNumberService> _randomMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _host = new User("Host", "host1");
            _engine = new DrawnToDressGameEngine(_engineLoggerMock.Object, _stateLoggerMock.Object, _randomMock.Object);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a started game state and transitions it to VotingMatchupState with
        /// three players (pA, pB, pC) who all have submitted outfits.  pA and pB will
        /// be in the first generated matchup; pC is the eligible third-party voter.
        /// </summary>
        private async Task<(DrawnToDressGameState state, DrawnToDressGameContext context, SwissMatchup matchup, string outsider)>
            SetupVotingStateAsync(List<VotingCriterionDefinition>? criteria = null)
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;

            if (criteria is not null)
                state.Config.VotingCriteria = criteria;

            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };
            state.GamePlayers["pC"] = new() { PlayerId = "pC", SubmittedOutfit = new() { PlayerId = "pC" } };

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);

            var round = state.VotingRounds[state.CurrentVotingRoundIndex];
            var firstMatchup = round.Matchups[0];
            // Entrant IDs are now "playerId:round" format; extract player IDs for outsider lookup.
            var entrantAPlayer = DrawnToDressGameContext.GetPlayerIdFromEntrantId(firstMatchup.EntrantAId);
            var entrantBPlayer = DrawnToDressGameContext.GetPlayerIdFromEntrantId(firstMatchup.EntrantBId);
            string outsider = new[] { "pA", "pB", "pC" }
                .First(id => id != entrantAPlayer && id != entrantBPlayer);

            return (state, context, firstMatchup, outsider);
        }

        // ── Submit validation: unknown criterion ──────────────────────────────

        [TestMethod]
        public async Task CastVote_UnknownCriterionId_IsRejected()
        {
            var (state, context, matchup, outsider) = await SetupVotingStateAsync();
            int votesBefore = state.Votes.Count;

            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "not_a_real_criterion", matchup.PlayerAId));

            Assert.AreEqual(votesBefore, state.Votes.Count,
                "A vote with an unrecognised criterion ID must not be recorded.");
        }

        // ── Submit validation: invalid chosen player ──────────────────────────

        [TestMethod]
        public async Task CastVote_ChosenPlayerNotInMatchup_IsRejected()
        {
            var (state, context, matchup, outsider) = await SetupVotingStateAsync();
            int votesBefore = state.Votes.Count;

            // outsider is not a participant → not a valid choice.
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", outsider));

            Assert.AreEqual(votesBefore, state.Votes.Count,
                "A vote whose chosen player is not a matchup participant must not be recorded.");
        }

        // ── Submit validation: unknown matchup ────────────────────────────────

        [TestMethod]
        public async Task CastVote_MatchupNotInCurrentRound_IsRejected()
        {
            var (state, context, _, outsider) = await SetupVotingStateAsync();
            int votesBefore = state.Votes.Count;

            var nonExistentMatchupId = Guid.NewGuid();
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, nonExistentMatchupId, "creativity", "pA"));

            Assert.AreEqual(votesBefore, state.Votes.Count,
                "A vote referencing a matchup not in the current round must not be recorded.");
        }

        // ── Submit validation: unknown voter ──────────────────────────────────

        [TestMethod]
        public async Task CastVote_UnknownVoter_IsRejected()
        {
            var (state, context, matchup, _) = await SetupVotingStateAsync();
            int votesBefore = state.Votes.Count;

            _engine.ProcessCommand(context,
                new CastVoteCommand("pUnknown", matchup.Id, "creativity", matchup.PlayerAId));

            Assert.AreEqual(votesBefore, state.Votes.Count,
                "A vote from an unregistered player must not be recorded.");
        }

        // ── Duplicate vote override ───────────────────────────────────────────

        [TestMethod]
        public async Task CastVote_DuplicateCriterion_OverridesPreviousVote()
        {
            var (state, context, matchup, outsider) = await SetupVotingStateAsync();

            // First vote: outsider picks PlayerA on 'creativity'.
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerAId));
            Assert.AreEqual(1, state.Votes.Count,
                "First vote must be recorded.");

            // Second vote on the same criterion: outsider changes mind to PlayerB.
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerBId));

            Assert.AreEqual(1, state.Votes.Count,
                "Duplicate vote for the same matchup+criterion must replace, not add a second entry.");

            var recorded = state.Votes.Values.Single();
            Assert.AreEqual(matchup.PlayerBId, recorded.ChosenPlayerId,
                "The updated vote must reflect the new choice.");
        }

        [TestMethod]
        public async Task CastVote_DifferentCriteria_BothRecorded()
        {
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
                new() { Id = "theme_match", DisplayName = "Theme Match", Weight = 1.0 },
            };
            var (state, context, matchup, outsider) = await SetupVotingStateAsync(criteria);

            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerAId));
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "theme_match", matchup.PlayerAId));

            Assert.AreEqual(2, state.Votes.Count,
                "Votes on different criteria for the same matchup must all be recorded.");
        }

        // ── Late vote marking ─────────────────────────────────────────────────

        [TestMethod]
        public async Task CastVote_AfterDeadline_IsMarkedLate()
        {
            // Use a negative VotingTimeSec so OnEnter sets the deadline in the past.
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.VotingTimeSec = -10; // deadline will be 10 s in the past on entry
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };
            state.GamePlayers["pC"] = new() { PlayerId = "pC", SubmittedOutfit = new() { PlayerId = "pC" } };

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);

            var round = state.VotingRounds[state.CurrentVotingRoundIndex];
            var matchup = round.Matchups[0];
            var playerA = GetPlayerIdFromEntrantId(matchup.EntrantAId);
            var playerB = GetPlayerIdFromEntrantId(matchup.EntrantBId);
            string outsider = new[] { "pA", "pB", "pC" }
                .First(id => id != playerA && id != playerB);

            // Cast a vote — the deadline is already in the past so it must be marked late.
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerAId));

            Assert.AreEqual(1, state.Votes.Count,
                "A late vote must still be persisted.");
            var recorded = state.Votes.Values.Single();
            Assert.IsTrue(recorded.IsLate, "A vote submitted after the deadline must be marked late.");
        }

        [TestMethod]
        public async Task CastVote_BeforeDeadline_IsNotMarkedLate()
        {
            var (state, context, matchup, outsider) = await SetupVotingStateAsync();

            // Default OnEnter sets deadline well in the future.
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerAId));

            Assert.AreEqual(1, state.Votes.Count);
            var recorded = state.Votes.Values.Single();
            Assert.IsFalse(recorded.IsLate, "A vote submitted before the deadline must not be marked late.");
        }

        // ── AllVotesCast criterion-level logic ────────────────────────────────

        [TestMethod]
        public async Task AllVotesCast_OnlyPartialCriteriaVoted_DoesNotAdvanceEarly()
        {
            // Two criteria; outsider votes on only one → must NOT advance.
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
                new() { Id = "theme_match", DisplayName = "Theme Match", Weight = 1.0 },
            };
            var (state, context, matchup, outsider) = await SetupVotingStateAsync(criteria);

            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerAId));

            // Only one criterion voted; voting state must still be active.
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState,
                "VotingMatchupState must remain active until all criteria are voted on.");
        }

        [TestMethod]
        public async Task AllVotesCast_AllCriteriaVoted_AdvancesEarly()
        {
            // Single criterion; outsider votes on it → should advance immediately.
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            var (state, context, matchup, outsider) = await SetupVotingStateAsync(criteria);

            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerAId));

            // With one eligible voter and one criterion complete the round must advance.
            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState,
                "VotingMatchupState must advance once every eligible voter has voted on every criterion.");
        }

        [TestMethod]
        public async Task AllVotesCast_TwoEligibleVoters_OnlyOneVoted_DoesNotAdvanceEarly()
        {
            // 4 players → 2 matchups; players pA/pB fight pC/pD.
            // outsider vote on one matchup's only criterion — but there are multiple
            // eligible voters, so the round must not advance until all have voted.
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.VotingCriteria = criteria;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };
            state.GamePlayers["pC"] = new() { PlayerId = "pC", SubmittedOutfit = new() { PlayerId = "pC" } };
            state.GamePlayers["pD"] = new() { PlayerId = "pD", SubmittedOutfit = new() { PlayerId = "pD" } };

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);

            var round = state.VotingRounds[state.CurrentVotingRoundIndex];
            var firstMatchup = round.Matchups[0];

            // Cast a vote from only one of the eligible voters (there must be at least one more).
            var pA2 = GetPlayerIdFromEntrantId(firstMatchup.EntrantAId);
            var pB2 = GetPlayerIdFromEntrantId(firstMatchup.EntrantBId);
            string voter1 = new[] { "pA", "pB", "pC", "pD" }
                .First(id => id != pA2 && id != pB2);

            _engine.ProcessCommand(context,
                new CastVoteCommand(voter1, firstMatchup.Id, "creativity", firstMatchup.EntrantAId));

            // Not all eligible voters have voted → must remain in voting state.
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState,
                "VotingMatchupState must not advance until every eligible voter has voted on every criterion.");
        }

        // ── Timer expiry: missing votes not counted ───────────────────────────

        [TestMethod]
        public async Task TimerExpiry_MissingVotes_DoesNotPreventAdvance()
        {
            // No votes cast at all, but timer expires → must still advance.
            // With 0-0 ties on all criteria, the state transitions to CoinFlipState
            // for interactive tie resolution.
            var (_, context, _, _) = await SetupVotingStateAsync();

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsTrue(
                context.Fsm.CurrentState is VotingRoundResultsState or CoinFlipState,
                "Timer expiry must advance the round even when some votes are missing.");
        }

        [TestMethod]
        public async Task TimerExpiry_SomeMissingVotes_RemainingVotesArePreserved()
        {
            var (state, context, matchup, outsider) = await SetupVotingStateAsync();

            // outsider casts one vote before the timer expires.
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, matchup.Id, "creativity", matchup.PlayerAId));
            int voteCountBeforeTick = state.Votes.Count;

            // Timer expires.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // The vote that was cast must still be persisted even though the round ended.
            Assert.AreEqual(voteCountBeforeTick, state.Votes.Count,
                "Votes already cast must be preserved after the round ends via timer expiry.");
        }

        // ── Visibility mode config ────────────────────────────────────────────

        [TestMethod]
        public void VoteVisibilityMode_DefaultIsHidden()
        {
            var config = new DrawnToDressConfig();
            Assert.AreEqual(VoteVisibilityMode.Hidden, config.VoteVisibility,
                "Default VoteVisibility must be Hidden.");
        }

        [TestMethod]
        public void VoteVisibilityMode_CanBeSetToPercentagesOnly()
        {
            var config = new DrawnToDressConfig { VoteVisibility = VoteVisibilityMode.PercentagesOnly };
            Assert.AreEqual(VoteVisibilityMode.PercentagesOnly, config.VoteVisibility);
        }

        [TestMethod]
        public void VoteVisibilityMode_CanBeSetToIndividualVotes()
        {
            var config = new DrawnToDressConfig { VoteVisibility = VoteVisibilityMode.IndividualVotes };
            Assert.AreEqual(VoteVisibilityMode.IndividualVotes, config.VoteVisibility);
        }
    }
}
