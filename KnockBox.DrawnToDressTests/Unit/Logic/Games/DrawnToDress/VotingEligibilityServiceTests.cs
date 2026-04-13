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
        // ── IsEligibleToVote ──────────────────────────────────────────────────

        [TestMethod]
        public void IsEligibleToVote_ThirdPartyPlayer_IsEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);

            bool eligible = VotingEligibilityService.IsEligibleToVote("pC", matchup);

            Assert.IsTrue(eligible, "A player not in the matchup should be eligible to vote.");
        }

        [TestMethod]
        public void IsEligibleToVote_PlayerA_IsNotEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);

            bool eligible = VotingEligibilityService.IsEligibleToVote("pA", matchup);

            Assert.IsFalse(eligible, "PlayerA must not vote on their own matchup.");
        }

        [TestMethod]
        public void IsEligibleToVote_PlayerB_IsNotEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);

            bool eligible = VotingEligibilityService.IsEligibleToVote("pB", matchup);

            Assert.IsFalse(eligible, "PlayerB must not vote on their own matchup.");
        }

        [TestMethod]
        public void IsEligibleToVote_UnknownPlayer_IsEligible()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);

            // A player not registered at all is still technically not a participant.
            bool eligible = VotingEligibilityService.IsEligibleToVote("pUnknown", matchup);

            Assert.IsTrue(eligible);
        }

        // ── GetEligibleVoterIds ───────────────────────────────────────────────

        [TestMethod]
        public void GetEligibleVoterIds_ExcludesMatchupParticipants()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);
            var allPlayers = new[] { "pA", "pB", "pC", "pD" };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            CollectionAssert.DoesNotContain(eligible.ToList(), "pA");
            CollectionAssert.DoesNotContain(eligible.ToList(), "pB");
        }

        [TestMethod]
        public void GetEligibleVoterIds_IncludesNonParticipants()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);
            var allPlayers = new[] { "pA", "pB", "pC", "pD" };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            CollectionAssert.Contains(eligible.ToList(), "pC");
            CollectionAssert.Contains(eligible.ToList(), "pD");
        }

        [TestMethod]
        public void GetEligibleVoterIds_TwoPlayerGame_NobodyIsEligible()
        {
            // In a two-player game the only matchup has both players in it,
            // leaving no eligible voters.
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);
            var allPlayers = new[] { "pA", "pB" };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            Assert.AreEqual(0, eligible.Count);
        }

        [TestMethod]
        public void GetEligibleVoterIds_ReturnsCorrectCount()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);
            var allPlayers = new[] { "pA", "pB", "pC", "pD", "pE" };

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayers);

            // 5 total − 2 participants = 3 eligible.
            Assert.AreEqual(3, eligible.Count);
        }

        [TestMethod]
        public void GetEligibleVoterIds_EmptyPlayerList_ReturnsEmpty()
        {
            var matchup = new SwissMatchup(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1);

            var eligible = VotingEligibilityService.GetEligibleVoterIds(matchup, []);

            Assert.AreEqual(0, eligible.Count);
        }
    }
}
