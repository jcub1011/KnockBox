using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Tests.Unit.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Unit tests for <see cref="VotingEligibilityService"/>.
    /// Covers creator-voting exclusion rules.
    /// </summary>
    [TestClass]
    public class VotingEligibilityServiceTests
    {
        private static readonly Guid _pA = Guid.NewGuid();
        private static readonly Guid _pB = Guid.NewGuid();
        private static readonly Guid _pC = Guid.NewGuid();
        private static readonly Guid _pD = Guid.NewGuid();
        private static readonly Guid _pE = Guid.NewGuid();
        private static readonly Guid _pUnknown = Guid.NewGuid();

        // ── IsEligibleToVote ──────────────────────────────────────────────────

        [TestMethod]
        public void IsEligibleToVote_ThirdPartyPlayer_IsEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);

            bool eligible = VotingEligibilityService.IsEligibleToVote(_pC, matchup);

            Assert.IsTrue(eligible, "A player not in the matchup should be eligible to vote.");
        }

        [TestMethod]
        public void IsEligibleToVote_PlayerA_IsNotEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);

            bool eligible = VotingEligibilityService.IsEligibleToVote(_pA, matchup);

            Assert.IsFalse(eligible, "PlayerA must not vote on their own matchup.");
        }

        [TestMethod]
        public void IsEligibleToVote_PlayerB_IsNotEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);

            bool eligible = VotingEligibilityService.IsEligibleToVote(_pB, matchup);

            Assert.IsFalse(eligible, "PlayerB must not vote on their own matchup.");
        }

        [TestMethod]
        public void IsEligibleToVote_UnknownPlayer_IsEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);

            // A player not registered at all is still technically not a participant.
            bool eligible = VotingEligibilityService.IsEligibleToVote(_pUnknown, matchup);

            Assert.IsTrue(eligible);
        }

        // ── GetEligibleVoterIds ───────────────────────────────────────────────

        [TestMethod]
        public void GetEligibleVoterIds_ExcludesMatchupParticipants()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);
            var allPlayers = new[] { _pA, _pB, _pC, _pD };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            CollectionAssert.DoesNotContain(eligible.ToList(), _pA);
            CollectionAssert.DoesNotContain(eligible.ToList(), _pB);
        }

        [TestMethod]
        public void GetEligibleVoterIds_IncludesNonParticipants()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);
            var allPlayers = new[] { _pA, _pB, _pC, _pD };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            CollectionAssert.Contains(eligible.ToList(), _pC);
            CollectionAssert.Contains(eligible.ToList(), _pD);
        }

        [TestMethod]
        public void GetEligibleVoterIds_TwoPlayerGame_NobodyIsEligible()
        {
            // In a two-player game the only matchup has both players in it,
            // leaving no eligible voters.
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);
            var allPlayers = new[] { _pA, _pB };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            Assert.IsEmpty(eligible);
        }

        [TestMethod]
        public void GetEligibleVoterIds_ReturnsCorrectCount()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);
            var allPlayers = new[] { _pA, _pB, _pC, _pD, _pE };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            // 5 total − 2 participants = 3 eligible.
            Assert.HasCount(3, eligible);
        }

        [TestMethod]
        public void GetEligibleVoterIds_EmptyPlayerList_ReturnsEmpty()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId(_pA, 1), new EntrantId(_pB, 1), 1);

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, []);

            Assert.IsEmpty(eligible);
        }
    }
}
