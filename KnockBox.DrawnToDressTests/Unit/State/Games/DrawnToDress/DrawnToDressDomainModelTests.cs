using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Tests.Unit.State.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressDomainModelTests
    {
        // ── DrawnClothingItem ─────────────────────────────────────────────────

        [TestMethod]
        public void DrawnClothingItem_DefaultConstruction_HasNewGuid()
        {
            var item = new DrawnClothingItem();
            Assert.AreNotEqual(Guid.Empty, item.Id);
        }

        [TestMethod]
        public void DrawnClothingItem_DefaultConstruction_IsNotInPool()
        {
            var item = new DrawnClothingItem();
            Assert.IsFalse(item.IsInPool);
            Assert.IsNull(item.ClaimedByPlayerId);
        }

        [TestMethod]
        public void DrawnClothingItem_Claim_UpdatesClaimMetadata()
        {
            var item = new DrawnClothingItem { IsInPool = true };
            item.ClaimedByPlayerId = "player1";

            Assert.AreEqual("player1", item.ClaimedByPlayerId);
        }

        // ── OutfitSubmission ──────────────────────────────────────────────────

        [TestMethod]
        public void OutfitSubmission_DefaultConstruction_HasEmptySelectedItems()
        {
            var submission = new OutfitSubmission();
            Assert.IsNotNull(submission.SelectedItemsByType);
            Assert.IsEmpty(submission.SelectedItemsByType);
        }

        [TestMethod]
        public void OutfitSubmission_DefaultConstruction_HasDefaultCustomization()
        {
            var submission = new OutfitSubmission();
            Assert.IsNotNull(submission.Customization);
            Assert.IsNull(submission.Customization.OutfitName);
        }

        [TestMethod]
        public void OutfitSubmission_SelectedItems_StoredByType()
        {
            var itemId = Guid.NewGuid();
            var submission = new OutfitSubmission
            {
                PlayerId = "player1",
                SelectedItemsByType = new Dictionary<ClothingType, Guid>
                {
                    [ClothingType.Hat] = itemId,
                }
            };

            Assert.AreEqual(itemId, submission.SelectedItemsByType[ClothingType.Hat]);
        }

        // ── ThemeDefinition ───────────────────────────────────────────────────

        [TestMethod]
        public void ThemeDefinition_Construction_PreservesFields()
        {
            var theme = new ThemeDefinition("beach", "Beach Party", "Sunny and sandy.");
            Assert.AreEqual("beach", theme.Id);
            Assert.AreEqual("Beach Party", theme.DisplayName);
            Assert.AreEqual("Sunny and sandy.", theme.Description);
        }

        [TestMethod]
        public void ThemeDefinition_WithoutDescription_DefaultsToNull()
        {
            var theme = new ThemeDefinition("casual", "Casual");
            Assert.IsNull(theme.Description);
        }

        // ── VotingCriterionDefinition ─────────────────────────────────────────

        [TestMethod]
        public void VotingCriterionDefinition_DefaultWeight_IsOne()
        {
            var criterion = new VotingCriterionDefinition { Id = "creativity", DisplayName = "Creativity" };
            Assert.AreEqual(1.0, criterion.Weight);
        }

        // ── SwissMatchup ──────────────────────────────────────────────────────

        [TestMethod]
        public void SwissMatchup_Construction_PreservesAllFields()
        {
            var id = Guid.NewGuid();
            var matchup = new SwissMatchup(id, new EntrantId("playerA", 1), new EntrantId("playerB", 1), 1);

            Assert.AreEqual(id, matchup.Id);
            Assert.AreEqual("playerA", matchup.PlayerAId);
            Assert.AreEqual("playerB", matchup.PlayerBId);
            Assert.AreEqual(1, matchup.RoundNumber);
        }

        // ── VotingRound ───────────────────────────────────────────────────────

        [TestMethod]
        public void VotingRound_DefaultConstruction_HasEmptyMatchups()
        {
            var round = new VotingRound();
            Assert.IsNotNull(round.Matchups);
            Assert.IsEmpty(round.Matchups);
        }

        // ── VoteSubmission ────────────────────────────────────────────────────

        [TestMethod]
        public void VoteSubmission_DefaultConstruction_IsNotLate()
        {
            var vote = new VoteSubmission();
            Assert.IsFalse(vote.IsLate);
        }

        [TestMethod]
        public void VoteSubmission_Construction_PreservesAllFields()
        {
            var matchupId = Guid.NewGuid();
            var before = DateTimeOffset.UtcNow;

            var vote = new VoteSubmission
            {
                VoterPlayerId = "voter1",
                MatchupId = matchupId,
                CriterionId = "creativity",
                ChosenEntrantId = new EntrantId("playerA", 1),
                IsLate = false,
            };

            Assert.AreEqual("voter1", vote.VoterPlayerId);
            Assert.AreEqual(matchupId, vote.MatchupId);
            Assert.AreEqual("creativity", vote.CriterionId);
            Assert.AreEqual("playerA", vote.ChosenPlayerId);
            Assert.IsFalse(vote.IsLate);
            Assert.IsGreaterThanOrEqualTo(before, vote.SubmittedAt);
        }

        // ── LeaderboardEntry ──────────────────────────────────────────────────

        [TestMethod]
        public void LeaderboardEntry_DefaultConstruction_ZeroScores()
        {
            var entry = new LeaderboardEntry();
            Assert.AreEqual(0, entry.Wins);
            Assert.AreEqual(0, entry.Losses);
            Assert.AreEqual(0.0, entry.TotalScore);
            Assert.AreEqual(0, entry.BonusPoints);
            Assert.AreEqual(0, entry.Rank);
        }

        // ── CoinFlipRequest ───────────────────────────────────────────────────

        [TestMethod]
        public void CoinFlipRequest_DefaultConstruction_HasNewGuid()
        {
            var request = new CoinFlipRequest();
            Assert.AreNotEqual(Guid.Empty, request.Id);
        }

        // ── CoinFlipResult ────────────────────────────────────────────────────

        [TestMethod]
        public void CoinFlipResult_Construction_PreservesAllFields()
        {
            var requestId = Guid.NewGuid();
            var matchupId = Guid.NewGuid();

            var result = new CoinFlipResult(requestId, matchupId, IsHeads: true, WinnerPlayerId: "playerA");

            Assert.AreEqual(requestId, result.RequestId);
            Assert.AreEqual(matchupId, result.MatchupId);
            Assert.IsTrue(result.IsHeads);
            Assert.AreEqual("playerA", result.WinnerPlayerId);
        }

        // ── DrawnToDressPlayerState ───────────────────────────────────────────

        [TestMethod]
        public void DrawnToDressPlayerState_DefaultConstruction_IsNotReady()
        {
            var state = new DrawnToDressPlayerState();
            Assert.IsFalse(state.IsReady);
        }

        [TestMethod]
        public void DrawnToDressPlayerState_DefaultConstruction_HasNoOwnedItems()
        {
            var state = new DrawnToDressPlayerState();
            Assert.IsNotNull(state.OwnedClothingItemIds);
            Assert.IsEmpty(state.OwnedClothingItemIds);
        }

        [TestMethod]
        public void DrawnToDressPlayerState_DefaultConstruction_HasNoSubmittedOutfit()
        {
            var state = new DrawnToDressPlayerState();
            Assert.IsNull(state.SubmittedOutfit);
        }

        [TestMethod]
        public void DrawnToDressPlayerState_DefaultConstruction_HasZeroBonusPoints()
        {
            var state = new DrawnToDressPlayerState();
            Assert.AreEqual(0, state.BonusPoints);
        }

        // ── ClothingTypeDefinition ────────────────────────────────────────────

        [TestMethod]
        public void ClothingTypeDefinition_Construction_PreservesAllFields()
        {
            var type = new ClothingTypeDefinition
            {
                Id = ClothingType.Hat,
                DisplayName = "Hat",
                AllowMultiple = false,
            };

            Assert.AreEqual(ClothingType.Hat, type.Id);
            Assert.AreEqual("Hat", type.DisplayName);
            Assert.IsFalse(type.AllowMultiple);
        }
    }
}
