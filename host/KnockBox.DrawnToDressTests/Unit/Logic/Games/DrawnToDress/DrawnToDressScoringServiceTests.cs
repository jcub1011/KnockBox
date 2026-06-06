using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Tests.Unit.Logic.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressScoringServiceTests
    {
        // ── Stable per-test player IDs ───────────────────────────────────────────
        // These are created once and shared across all test methods via ThreadLocal
        // or simply as static readonly — scoring tests don't mutate them.
        private static readonly Guid PA = Guid.NewGuid();
        private static readonly Guid PB = Guid.NewGuid();
        private static readonly Guid PC = Guid.NewGuid();
        private static readonly Guid PD = Guid.NewGuid();

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static SwissMatchup MakeMatchup(EntrantId entrantA, EntrantId entrantB, int round = 1)
            => new(Guid.NewGuid(), entrantA, entrantB, round);

        private static VoteSubmission Vote(Guid matchupId, string criterionId, EntrantId chosenEntrantId, Guid voterId = default)
            => new()
            {
                VoterPlayerId = voterId == default ? Guid.NewGuid() : voterId,
                MatchupId = matchupId,
                CriterionId = criterionId,
                ChosenEntrantId = chosenEntrantId,
            };

        private static List<VotingCriterionDefinition> DefaultCriteria() =>
        [
            new() { Id = "creativity",   DisplayName = "Creativity",   Weight = 1.0 },
            new() { Id = "theme_match",  DisplayName = "Theme Match",  Weight = 1.0 },
            new() { Id = "overall_look", DisplayName = "Overall Look", Weight = 1.0 },
        ];

        private static Dictionary<Guid, DrawnToDressPlayerState> MakePlayers(params Guid[] ids) =>
            ids.ToDictionary(id => id, id => new DrawnToDressPlayerState
            {
                PlayerId = id,
                DisplayName = id.ToString(),
                SubmittedOutfits = new() { [1] = new() { PlayerId = id } },
            });

        // ── CalculateCriterionScores ────────────────────────────────────────────

        [TestMethod]
        public void CriterionScores_BasicVoting_ReturnsVoteCountTimesWeight()
        {
            // 3 voters, weight=1. A gets 2 votes, B gets 1.
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
            };

            var (aScore, bScore) = DrawnToDressScoringService.CalculateCriterionScores(
                matchup, "creativity", 1.0, votes);

            Assert.AreEqual(2.0, aScore);
            Assert.AreEqual(1.0, bScore);
        }

        [TestMethod]
        public void CriterionScores_WeightedScoring_MultipliesVotesByWeight()
        {
            // weight=2 on creativity: A gets 3 votes, B gets 1.
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
            };

            var (aScore, bScore) = DrawnToDressScoringService.CalculateCriterionScores(
                matchup, "creativity", 2.0, votes);

            Assert.AreEqual(6.0, aScore);
            Assert.AreEqual(2.0, bScore);
        }

        [TestMethod]
        public void CriterionScores_ProportionalScoring_BothOutfitsEarnPoints()
        {
            // Both entrants should earn points in the same matchup.
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
            };

            var (aScore, bScore) = DrawnToDressScoringService.CalculateCriterionScores(
                matchup, "creativity", 1.0, votes);

            Assert.AreEqual(1.0, aScore, "Entrant A should still earn points even when losing.");
            Assert.AreEqual(2.0, bScore);
        }

        [TestMethod]
        public void CriterionScores_IgnoresOtherMatchupsCriteriaAndStrayEntrants()
        {
            // Locks in the single-pass tally: votes for a different matchup, a different
            // criterion, or an entrant that is neither A nor B must all be skipped.
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var otherMatchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var votes = new List<VoteSubmission>
            {
                // Counted: this matchup + this criterion.
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
                // Ignored: same criterion, different matchup.
                Vote(otherMatchup.Id, "creativity", new EntrantId(PA, 1)),
                // Ignored: same matchup, different criterion.
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                // Ignored: right matchup + criterion, but the chosen entrant is in neither slot.
                Vote(matchup.Id, "creativity", new EntrantId(PC, 1)),
            };

            var (aScore, bScore) = DrawnToDressScoringService.CalculateCriterionScores(
                matchup, "creativity", 1.0, votes);

            Assert.AreEqual(1.0, aScore, "Only the single in-scope A vote should count.");
            Assert.AreEqual(1.0, bScore, "Only the single in-scope B vote should count.");
        }

        // ── FindTiedCriteria ────────────────────────────────────────────────────

        [TestMethod]
        public void FindTiedCriteria_NoTies_ReturnsEmpty()
        {
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var round = new VotingRound { RoundNumber = 1, Matchups = [matchup] };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
            };

            var ties = DrawnToDressScoringService.FindTiedCriteria(round, criteria, votes);

            Assert.IsEmpty(ties);
        }

        [TestMethod]
        public void FindTiedCriteria_OneTie_ReturnsSingleEntry()
        {
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var round = new VotingRound { RoundNumber = 1, Matchups = [matchup] };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
            };

            var ties = DrawnToDressScoringService.FindTiedCriteria(round, criteria, votes);

            Assert.HasCount(1, ties);
            Assert.AreEqual(matchup.Id, ties[0].MatchupId);
            Assert.AreEqual("creativity", ties[0].CriterionId);
        }

        [TestMethod]
        public void FindTiedCriteria_MultipleTies_ReturnsAll()
        {
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var round = new VotingRound { RoundNumber = 1, Matchups = [matchup] };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
                new() { Id = "theme_match", DisplayName = "Theme Match", Weight = 1.0 },
            };
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PB, 1)),
            };

            var ties = DrawnToDressScoringService.FindTiedCriteria(round, criteria, votes);

            Assert.HasCount(2, ties);
        }

        [TestMethod]
        public void FindTiedCriteria_ZeroZeroAllAbstain_IsTreatedAsTie()
        {
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var round = new VotingRound { RoundNumber = 1, Matchups = [matchup] };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            var votes = new List<VoteSubmission>(); // No votes at all.

            var ties = DrawnToDressScoringService.FindTiedCriteria(round, criteria, votes);

            Assert.HasCount(1, ties, "0-0 (all abstain) should be treated as a tie.");
        }

        [TestMethod]
        public void FindTiedCriteria_AlreadyResolved_ExcludesExistingFlipResults()
        {
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var round = new VotingRound { RoundNumber = 1, Matchups = [matchup] };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
            };
            var existingFlips = new List<CriterionCoinFlipResult>
            {
                new(matchup.Id, "creativity", new EntrantId(PA, 1)),
            };

            var ties = DrawnToDressScoringService.FindTiedCriteria(round, criteria, votes, existingFlips);

            Assert.IsEmpty(ties, "Already-resolved ties should not appear.");
        }

        // ── CalculateMatchupTotals ──────────────────────────────────────────────

        [TestMethod]
        public void MatchupTotals_CoinFlipBonus_AddsFlatOneToWinner()
        {
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            // Tied votes: 1-1.
            var votes = new List<VoteSubmission>
            {
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
            };
            // Coin flip: A wins.
            var flips = new List<CriterionCoinFlipResult>
            {
                new(matchup.Id, "creativity", new EntrantId(PA, 1)),
            };

            var (aTotal, bTotal) = DrawnToDressScoringService.CalculateMatchupTotals(
                matchup, criteria, votes, flips);

            Assert.AreEqual(2.0, aTotal, "A should get 1 (vote) + 1 (coin flip bonus) = 2.");
            Assert.AreEqual(1.0, bTotal, "B should get 1 (vote) only.");
        }

        [TestMethod]
        public void MatchupTotals_WeightedCriteriaWithCoinFlip_CorrectTotal()
        {
            // Spec Example 2: 3 criteria (weights 2,1,1), 4 voters.
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity",   DisplayName = "Creativity",   Weight = 2.0 },
                new() { Id = "theme_match",  DisplayName = "Theme Match",  Weight = 1.0 },
                new() { Id = "overall_look", DisplayName = "Overall Look", Weight = 1.0 },
            };

            // Creativity (x2): A=3, B=1
            // Theme Match (x1): A=2, B=2 (tie → coin flip → B wins)
            // Overall Look (x1): A=3, B=1
            var votes = new List<VoteSubmission>
            {
                // Creativity
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
                // Theme Match (tied)
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PB, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PB, 1)),
                // Overall Look
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PB, 1)),
            };

            // Coin flip: B wins theme_match tie.
            var flips = new List<CriterionCoinFlipResult>
            {
                new(matchup.Id, "theme_match", new EntrantId(PB, 1)),
            };

            var (aTotal, bTotal) = DrawnToDressScoringService.CalculateMatchupTotals(
                matchup, criteria, votes, flips);

            // A: creativity=3×2=6, theme=2×1=2, overall=3×1=3 = 11
            // B: creativity=1×2=2, theme=2×1=2+1(flip)=3, overall=1×1=1 = 6
            Assert.AreEqual(11.0, aTotal, "Spec example 2: A total should be 11.");
            Assert.AreEqual(6.0, bTotal, "Spec example 2: B total should be 6.");
        }

        // ── CalculateRoundScores ────────────────────────────────────────────────

        [TestMethod]
        public void RoundScores_MultipleMatchups_SumsCorrectly()
        {
            var m1 = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var m2 = MakeMatchup(new EntrantId(PC, 1), new EntrantId(PD, 1));
            var round = new VotingRound { RoundNumber = 1, Matchups = [m1, m2] };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            var votes = new List<VoteSubmission>
            {
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PB, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PC, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PD, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PD, 1)),
            };

            var scores = DrawnToDressScoringService.CalculateRoundScores(
                round, criteria, votes, []);

            Assert.AreEqual(2.0, scores[new EntrantId(PA, 1)]);
            Assert.AreEqual(1.0, scores[new EntrantId(PB, 1)]);
            Assert.AreEqual(1.0, scores[new EntrantId(PC, 1)]);
            Assert.AreEqual(2.0, scores[new EntrantId(PD, 1)]);
        }

        // ── GetRoundLeaders ─────────────────────────────────────────────────────

        [TestMethod]
        public void GetRoundLeaders_SingleLeader_ReturnsOne()
        {
            var scores = new Dictionary<EntrantId, double>
            {
                [new EntrantId(PA, 1)] = 5.0, [new EntrantId(PB, 1)] = 3.0, [new EntrantId(PC, 1)] = 2.0,
            };

            var leaders = DrawnToDressScoringService.GetRoundLeaders(scores);

            Assert.HasCount(1, leaders);
            Assert.Contains(new EntrantId(PA, 1), leaders);
        }

        [TestMethod]
        public void GetRoundLeaders_TiedLeaders_ReturnsBoth()
        {
            var scores = new Dictionary<EntrantId, double>
            {
                [new EntrantId(PA, 1)] = 5.0, [new EntrantId(PB, 1)] = 5.0, [new EntrantId(PC, 1)] = 2.0,
            };

            var leaders = DrawnToDressScoringService.GetRoundLeaders(scores);

            Assert.HasCount(2, leaders);
            Assert.Contains(new EntrantId(PA, 1), leaders);
            Assert.Contains(new EntrantId(PB, 1), leaders);
        }

        // ── CalculateMatchupWins ────────────────────────────────────────────────

        [TestMethod]
        public void MatchupWins_WinTieLoss_CorrectValues()
        {
            var m1 = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var m2 = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PC, 1));
            var rounds = new List<VotingRound>
            {
                new() { RoundNumber = 1, Matchups = [m1] },
                new() { RoundNumber = 2, Matchups = [m2] },
            };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            // m1: A wins (2-1), m2: tied (1-1)
            var votes = new List<VoteSubmission>
            {
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PB, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PC, 1)),
            };

            var wins = DrawnToDressScoringService.CalculateMatchupWins(
                rounds, criteria, votes, []);

            Assert.AreEqual(1.5, wins[new EntrantId(PA, 1)], "A: win(1.0) + tie(0.5) = 1.5");
            Assert.AreEqual(0.0, wins[new EntrantId(PB, 1)], "B: loss(0.0)");
            Assert.AreEqual(0.5, wins[new EntrantId(PC, 1)], "C: tie(0.5)");
        }

        // ── CalculatePlayerTotals ───────────────────────────────────────────────

        [TestMethod]
        public void PlayerTotals_TwoOutfitsPerPlayer_SumsCorrectly()
        {
            // Player PA has two entrants: PA:1 and PA:2.
            var m1 = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var m2 = MakeMatchup(new EntrantId(PA, 2), new EntrantId(PC, 1));
            var rounds = new List<VotingRound>
            {
                new() { RoundNumber = 1, Matchups = [m1, m2] },
            };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            // m1: PA:1 gets 2 votes, PB:1 gets 1.
            // m2: PA:2 gets 3 votes, PC:1 gets 0.
            var votes = new List<VoteSubmission>
            {
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PB, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PA, 2)),
                Vote(m2.Id, "creativity", new EntrantId(PA, 2)),
                Vote(m2.Id, "creativity", new EntrantId(PA, 2)),
            };

            var players = new Dictionary<Guid, DrawnToDressPlayerState>
            {
                [PA] = new() { PlayerId = PA, BonusPoints = 2 },
                [PB] = new() { PlayerId = PB, BonusPoints = 0 },
                [PC] = new() { PlayerId = PC, BonusPoints = 1 },
            };

            var totals = DrawnToDressScoringService.CalculatePlayerTotals(
                rounds, criteria, votes, [], players, new DrawnToDressSettings());

            // PA: outfit1=2 + outfit2=3 + bonus=2 = 7
            Assert.AreEqual(7.0, totals[PA], "PA: 2 + 3 + 2 bonus = 7.");
            // PB: 1 + 0 bonus = 1
            Assert.AreEqual(1.0, totals[PB]);
            // PC: 0 + 1 bonus = 1
            Assert.AreEqual(1.0, totals[PC]);
        }

        // ── BuildLeaderboard ────────────────────────────────────────────────────

        [TestMethod]
        public void BuildLeaderboard_RankedByTotalPoints_ThenMatchupWins()
        {
            var m1 = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var m2 = MakeMatchup(new EntrantId(PC, 1), new EntrantId(PA, 1));
            var rounds = new List<VotingRound>
            {
                new() { RoundNumber = 1, Matchups = [m1] },
                new() { RoundNumber = 2, Matchups = [m2] },
            };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            // m1: A=3, B=1. m2: C=2, A=2 (tie).
            var votes = new List<VoteSubmission>
            {
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PB, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PC, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PC, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m2.Id, "creativity", new EntrantId(PA, 1)),
            };

            var players = MakePlayers(PA, PB, PC);

            var (entries, _) = DrawnToDressScoringService.BuildLeaderboard(
                rounds, criteria, votes, [], players, new DrawnToDressSettings());

            // PA: round1=3 + round2=2 = 5 total, matchup wins=1.5 (win+tie)
            // PC: round2=2, matchup wins=0.5 (tie)
            // PB: round1=1, matchup wins=0.0 (loss)
            Assert.AreEqual(PA, entries[0].PlayerId);
            Assert.AreEqual(1, entries[0].Rank);
            Assert.AreEqual(PC, entries[1].PlayerId);
            Assert.AreEqual(2, entries[1].Rank);
            Assert.AreEqual(PB, entries[2].PlayerId);
            Assert.AreEqual(3, entries[2].Rank);
        }

        [TestMethod]
        public void BuildLeaderboard_TiedPlayers_ShareRank()
        {
            var m1 = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var rounds = new List<VotingRound>
            {
                new() { RoundNumber = 1, Matchups = [m1] },
            };
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
            };
            // Tied: A=1, B=1.
            var votes = new List<VoteSubmission>
            {
                Vote(m1.Id, "creativity", new EntrantId(PA, 1)),
                Vote(m1.Id, "creativity", new EntrantId(PB, 1)),
            };

            var players = MakePlayers(PA, PB);

            var (entries, tiedPairs) = DrawnToDressScoringService.BuildLeaderboard(
                rounds, criteria, votes, [], players, new DrawnToDressSettings());

            Assert.AreEqual(entries[0].Rank, entries[1].Rank, "Tied players should share the same rank.");
            Assert.HasCount(1, tiedPairs, "Tied pair should be reported.");
        }

        // ── Spec fixture verification ───────────────────────────────────────────

        [TestMethod]
        public void SpecFixture1_ThreeCriteriaDefaultWeight_CorrectScores()
        {
            // Spec Example 1: 3 criteria (weights 1,1,1), 4 eligible voters.
            // Creativity:   A=3, B=1
            // Theme Match:  A=2, B=2 (tie → coin flip → A wins)
            // Overall Look: A=3, B=1
            // A total: 3+2+1(flip)+3 = 9, B total: 1+2+1 = 4
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var criteria = DefaultCriteria();

            var votes = new List<VoteSubmission>
            {
                // Creativity: A=3, B=1
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
                // Theme Match: A=2, B=2
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PB, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PB, 1)),
                // Overall Look: A=3, B=1
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PB, 1)),
            };

            // Coin flip: A wins theme_match tie.
            var flips = new List<CriterionCoinFlipResult>
            {
                new(matchup.Id, "theme_match", new EntrantId(PA, 1)),
            };

            var (aTotal, bTotal) = DrawnToDressScoringService.CalculateMatchupTotals(
                matchup, criteria, votes, flips);

            Assert.AreEqual(9.0, aTotal, "Spec example 1: A should have 9 points.");
            Assert.AreEqual(4.0, bTotal, "Spec example 1: B should have 4 points.");
        }

        [TestMethod]
        public void SpecFixture2_WeightedCreativity_CorrectScores()
        {
            // Spec Example 2: 3 criteria (weights 2,1,1), 4 eligible voters.
            // Creativity (×2): A=3, B=1 → A=6, B=2
            // Theme Match (×1): A=2, B=2 (tie → coin flip → B wins) → A=2, B=2+1=3
            // Overall Look (×1): A=3, B=1 → A=3, B=1
            // A total: 6+2+3 = 11, B total: 2+3+1 = 6
            var matchup = MakeMatchup(new EntrantId(PA, 1), new EntrantId(PB, 1));
            var criteria = new List<VotingCriterionDefinition>
            {
                new() { Id = "creativity",   DisplayName = "Creativity",   Weight = 2.0 },
                new() { Id = "theme_match",  DisplayName = "Theme Match",  Weight = 1.0 },
                new() { Id = "overall_look", DisplayName = "Overall Look", Weight = 1.0 },
            };

            var votes = new List<VoteSubmission>
            {
                // Creativity: A=3, B=1
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PA, 1)),
                Vote(matchup.Id, "creativity", new EntrantId(PB, 1)),
                // Theme Match: A=2, B=2
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PA, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PB, 1)),
                Vote(matchup.Id, "theme_match", new EntrantId(PB, 1)),
                // Overall Look: A=3, B=1
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PA, 1)),
                Vote(matchup.Id, "overall_look", new EntrantId(PB, 1)),
            };

            // Coin flip: B wins theme_match tie.
            var flips = new List<CriterionCoinFlipResult>
            {
                new(matchup.Id, "theme_match", new EntrantId(PB, 1)),
            };

            var (aTotal, bTotal) = DrawnToDressScoringService.CalculateMatchupTotals(
                matchup, criteria, votes, flips);

            Assert.AreEqual(11.0, aTotal, "Spec example 2: A should have 11 points.");
            Assert.AreEqual(6.0, bTotal, "Spec example 2: B should have 6 points.");
        }

        // ── ApplyCoinFlipTiebreaks ──────────────────────────────────────────

        [TestMethod]
        public void ApplyCoinFlipTiebreaks_ReRanksTiedEntries()
        {
            var entries = new List<LeaderboardEntry>
            {
                new() { PlayerId = PA, DisplayName = "A", TotalScore = 10, MatchupWins = 2, Rank = 1 },
                new() { PlayerId = PB, DisplayName = "B", TotalScore = 10, MatchupWins = 2, Rank = 1 },
            };

            var flips = new List<PendingCoinFlipEntry>
            {
                new()
                {
                    Context = CoinFlipContext.FinalStandingsTie,
                    PlayerAId = PA,
                    PlayerBId = PB,
                    WinnerPlayerId = PB,
                    IsResolved = true,
                }
            };

            DrawnToDressScoringService.ApplyCoinFlipTiebreaks(entries, flips);

            var pB = entries.First(e => e.PlayerId == PB);
            var pA = entries.First(e => e.PlayerId == PA);
            Assert.IsLessThan(pA.Rank, pB.Rank, "Coin flip winner PB should rank higher.");
            Assert.AreEqual("coin_flip", pB.TiebreakMethod);
            Assert.AreEqual("coin_flip", pA.TiebreakMethod);
        }

        [TestMethod]
        public void SetMatchupWinsTiebreakMethod_SetsMethodForMatchupWinsBreaks()
        {
            var entries = new List<LeaderboardEntry>
            {
                new() { PlayerId = PA, DisplayName = "A", TotalScore = 10, MatchupWins = 3, Rank = 1 },
                new() { PlayerId = PB, DisplayName = "B", TotalScore = 10, MatchupWins = 2, Rank = 2 },
                new() { PlayerId = PC, DisplayName = "C", TotalScore = 5, MatchupWins = 1, Rank = 3 },
            };

            DrawnToDressScoringService.SetMatchupWinsTiebreakMethod(entries);

            Assert.AreEqual("matchup_wins", entries[0].TiebreakMethod);
            Assert.AreEqual("matchup_wins", entries[1].TiebreakMethod);
            Assert.IsNull(entries[2].TiebreakMethod);
        }

        [TestMethod]
        public void SetMatchupWinsTiebreakMethod_NoTies_LeavesNull()
        {
            var entries = new List<LeaderboardEntry>
            {
                new() { PlayerId = PA, DisplayName = "A", TotalScore = 10, MatchupWins = 3, Rank = 1 },
                new() { PlayerId = PB, DisplayName = "B", TotalScore = 5, MatchupWins = 2, Rank = 2 },
            };

            DrawnToDressScoringService.SetMatchupWinsTiebreakMethod(entries);

            Assert.IsNull(entries[0].TiebreakMethod);
            Assert.IsNull(entries[1].TiebreakMethod);
        }
    }
}
