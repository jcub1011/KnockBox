using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Unit tests for <see cref="SwissTournamentService"/>.
    /// Covers round-count calculation and Swiss pairing fixtures.
    /// </summary>
    [TestClass]
    public class SwissTournamentServiceTests
    {
        // ── CalculateRoundCount ───────────────────────────────────────────────

        [TestMethod]
        [DataRow(1, 1)]
        [DataRow(2, 1)]
        [DataRow(3, 2)]
        [DataRow(4, 2)]
        [DataRow(5, 3)]
        [DataRow(8, 3)]
        [DataRow(9, 4)]
        [DataRow(16, 4)]
        [DataRow(17, 5)]
        public void CalculateRoundCount_VariousEntrantCounts_MatchesExpectedFormula(
            int entrantCount, int expectedRounds)
        {
            int actual = SwissTournamentService.CalculateRoundCount(entrantCount);
            Assert.AreEqual(expectedRounds, actual,
                $"CalculateRoundCount({entrantCount}) should be {expectedRounds}.");
        }

        [TestMethod]
        public void CalculateRoundCount_Zero_ReturnsOne()
        {
            // Edge case: zero entrants should not throw and should return minimum of 1.
            int actual = SwissTournamentService.CalculateRoundCount(0);
            Assert.AreEqual(1, actual);
        }

        // ── ResolveRoundCount ─────────────────────────────────────────────────

        [TestMethod]
        public void ResolveRoundCount_PositiveConfig_UsesConfiguredValue()
        {
            // When a positive configured value is provided it takes priority.
            int actual = SwissTournamentService.ResolveRoundCount(entrantCount: 4, configuredRounds: 5);
            Assert.AreEqual(5, actual);
        }

        [TestMethod]
        public void ResolveRoundCount_ZeroConfig_FallsBackToAutoCalculation()
        {
            // VotingRounds = 0 means "auto"; 8 entrants → ceil(log2(8)) = 3.
            int actual = SwissTournamentService.ResolveRoundCount(entrantCount: 8, configuredRounds: 0);
            Assert.AreEqual(3, actual);
        }

        [TestMethod]
        public void ResolveRoundCount_ConfiguredOne_AlwaysReturnsOne()
        {
            int actual = SwissTournamentService.ResolveRoundCount(entrantCount: 100, configuredRounds: 1);
            Assert.AreEqual(1, actual);
        }

        // ── CalculateWins ─────────────────────────────────────────────────────

        [TestMethod]
        public void CalculateWins_NoVotes_ReturnsEmptyDictionary()
        {
            var rounds = new List<VotingRound>
            {
                new() { RoundNumber = 1, Matchups = [new(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1)] },
            };

            var wins = SwissTournamentService.CalculateWins(rounds, []);

            Assert.AreEqual(0, wins.Count);
        }

        [TestMethod]
        public void CalculateWins_PlayerAWins_ReturnsOneWinForA()
        {
            var matchupId = Guid.NewGuid();
            var rounds = new List<VotingRound>
            {
                new()
                {
                    RoundNumber = 1,
                    Matchups = [new(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
                },
            };
            var votes = new List<VoteSubmission>
            {
                new() { MatchupId = matchupId, ChosenEntrantId = new EntrantId("pA", 1), CriterionId = "creativity" },
                new() { MatchupId = matchupId, ChosenEntrantId = new EntrantId("pA", 1), CriterionId = "theme_match" },
                new() { MatchupId = matchupId, ChosenEntrantId = new EntrantId("pB", 1), CriterionId = "overall_look" },
            };

            var wins = SwissTournamentService.CalculateWins(rounds, votes);

            Assert.AreEqual(1.0, wins.GetValueOrDefault(new EntrantId("pA", 1), 0.0), "pA should have 1 win.");
            Assert.AreEqual(0.0, wins.GetValueOrDefault(new EntrantId("pB", 1), 0.0), "pB should have 0 wins.");
        }

        [TestMethod]
        public void CalculateWins_Tie_BothPlayersReceiveHalfWin()
        {
            var matchupId = Guid.NewGuid();
            var rounds = new List<VotingRound>
            {
                new()
                {
                    RoundNumber = 1,
                    Matchups = [new(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
                },
            };
            var votes = new List<VoteSubmission>
            {
                new() { MatchupId = matchupId, ChosenEntrantId = new EntrantId("pA", 1), CriterionId = "creativity" },
                new() { MatchupId = matchupId, ChosenEntrantId = new EntrantId("pB", 1), CriterionId = "theme_match" },
            };

            var wins = SwissTournamentService.CalculateWins(rounds, votes);

            Assert.AreEqual(0.5, wins.GetValueOrDefault(new EntrantId("pA", 1), 0.0));
            Assert.AreEqual(0.5, wins.GetValueOrDefault(new EntrantId("pB", 1), 0.0));
        }

        [TestMethod]
        public void CalculateWins_MultipleRounds_AccumulatesCorrectly()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            var rounds = new List<VotingRound>
            {
                new()
                {
                    RoundNumber = 1,
                    Matchups = [new(id1, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
                },
                new()
                {
                    RoundNumber = 2,
                    Matchups = [new(id2, new EntrantId("pA", 1), new EntrantId("pC", 1), 2)],
                },
            };

            // pA wins round 1, pC wins round 2.
            var votes = new List<VoteSubmission>
            {
                new() { MatchupId = id1, ChosenEntrantId = new EntrantId("pA", 1), CriterionId = "creativity" },
                new() { MatchupId = id1, ChosenEntrantId = new EntrantId("pA", 1), CriterionId = "theme_match" },
                new() { MatchupId = id2, ChosenEntrantId = new EntrantId("pC", 1), CriterionId = "creativity" },
                new() { MatchupId = id2, ChosenEntrantId = new EntrantId("pC", 1), CriterionId = "theme_match" },
            };

            var wins = SwissTournamentService.CalculateWins(rounds, votes);

            Assert.AreEqual(1.0, wins.GetValueOrDefault(new EntrantId("pA", 1), 0.0));
            Assert.AreEqual(0.0, wins.GetValueOrDefault(new EntrantId("pB", 1), 0.0));
            Assert.AreEqual(1.0, wins.GetValueOrDefault(new EntrantId("pC", 1), 0.0));
        }

        // ── GenerateRound – round 1 (no previous rounds) ─────────────────────

        [TestMethod]
        public void GenerateRound_Round1_TwoPlayers_ProducesOneMatchup()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1) };

            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            Assert.AreEqual(1, round.RoundNumber);
            Assert.AreEqual(1, round.Matchups.Count);
            var matchup = round.Matchups[0];
            Assert.IsTrue(
                (matchup.PlayerAId == "pA" && matchup.PlayerBId == "pB") ||
                (matchup.PlayerAId == "pB" && matchup.PlayerBId == "pA"),
                "The two players must be paired together.");
        }

        [TestMethod]
        public void GenerateRound_Round1_FourPlayers_ProducesTwoMatchups()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1), new("pD", 1) };

            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            Assert.AreEqual(2, round.Matchups.Count);
        }

        [TestMethod]
        public void GenerateRound_Round1_OddPlayerCount_AssignsBye()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1) };

            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            // One matchup (2 players); one player receives a bye.
            Assert.AreEqual(1, round.Matchups.Count);
            Assert.AreEqual(1, round.Byes.Count, "Exactly one entrant should receive a bye.");
        }

        [TestMethod]
        public void GenerateRound_Round1_NoSelfMatchups()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1), new("pD", 1) };

            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            foreach (var matchup in round.Matchups)
                Assert.AreNotEqual(matchup.PlayerAId, matchup.PlayerBId,
                    "Self-matchup detected.");
        }

        [TestMethod]
        public void GenerateRound_Round1_EachPlayerAppearsAtMostOnce()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1), new("pD", 1) };

            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            var allParticipants = round.Matchups
                .SelectMany(m => new[] { m.PlayerAId, m.PlayerBId })
                .ToList();

            Assert.AreEqual(allParticipants.Count, allParticipants.Distinct().Count(),
                "Each player must appear in at most one matchup per round.");
        }

        [TestMethod]
        public void GenerateRound_Round1_IsDeterministic()
        {
            var entrants = new List<EntrantId> { new("pC", 1), new("pA", 1), new("pD", 1), new("pB", 1) };

            var round1 = SwissTournamentService.GenerateRound(1, entrants, [], null);
            var round2 = SwissTournamentService.GenerateRound(1, entrants, [], null);

            // Same entrant list → same pairings.
            Assert.AreEqual(round1.Matchups.Count, round2.Matchups.Count);
            for (int i = 0; i < round1.Matchups.Count; i++)
            {
                Assert.AreEqual(round1.Matchups[i].PlayerAId, round2.Matchups[i].PlayerAId);
                Assert.AreEqual(round1.Matchups[i].PlayerBId, round2.Matchups[i].PlayerBId);
            }
        }

        // ── GenerateRound – round 2+ (Swiss by wins) ─────────────────────────

        [TestMethod]
        public void GenerateRound_Round2_AvoidsRematch_WhenPossible()
        {
            // Round 1: pA vs pB, pC vs pD.
            var round1MatchupAB = Guid.NewGuid();
            var round1MatchupCD = Guid.NewGuid();
            var previousRounds = new List<VotingRound>
            {
                new()
                {
                    RoundNumber = 1,
                    Matchups =
                    [
                        new(round1MatchupAB, new EntrantId("pA", 1), new EntrantId("pB", 1), 1),
                        new(round1MatchupCD, new EntrantId("pC", 1), new EntrantId("pD", 1), 1),
                    ],
                },
            };

            // All even wins; no preference from scores.
            var round2 = SwissTournamentService.GenerateRound(2, [new EntrantId("pA", 1), new EntrantId("pB", 1), new EntrantId("pC", 1), new EntrantId("pD", 1)], previousRounds, new Dictionary<EntrantId, double>());

            // pA must not face pB again; pC must not face pD again.
            foreach (var matchup in round2.Matchups)
            {
                bool isRematch =
                    (matchup.PlayerAId == "pA" && matchup.PlayerBId == "pB") ||
                    (matchup.PlayerAId == "pB" && matchup.PlayerBId == "pA") ||
                    (matchup.PlayerAId == "pC" && matchup.PlayerBId == "pD") ||
                    (matchup.PlayerAId == "pD" && matchup.PlayerBId == "pC");
                Assert.IsFalse(isRematch,
                    $"Rematch detected: {matchup.PlayerAId} vs {matchup.PlayerBId}");
            }
        }

        [TestMethod]
        public void GenerateRound_Round2_PairsHighWinnersTogetherFirst()
        {
            // Round 1: pA and pC both won; pB and pD both lost.
            var matchupId1 = Guid.NewGuid();
            var matchupId2 = Guid.NewGuid();
            var previousRounds = new List<VotingRound>
            {
                new()
                {
                    RoundNumber = 1,
                    Matchups =
                    [
                        new(matchupId1, new EntrantId("pA", 1), new EntrantId("pB", 1), 1), // pA wins
                        new(matchupId2, new EntrantId("pC", 1), new EntrantId("pD", 1), 1), // pC wins
                    ],
                },
            };

            var wins = new Dictionary<EntrantId, double> { [new EntrantId("pA", 1)] = 1.0, [new EntrantId("pC", 1)] = 1.0 };

            var round2 = SwissTournamentService.GenerateRound(2, [new EntrantId("pA", 1), new EntrantId("pB", 1), new EntrantId("pC", 1), new EntrantId("pD", 1)], previousRounds, wins);

            // The high-wins group (pA and pC) should be paired together.
            bool highWinnersPaired = round2.Matchups.Any(m =>
                (m.PlayerAId == "pA" && m.PlayerBId == "pC") ||
                (m.PlayerAId == "pC" && m.PlayerBId == "pA"));
            Assert.IsTrue(highWinnersPaired,
                "Players with equal win counts should be paired with each other (pA vs pC).");
        }

        [TestMethod]
        public void GenerateRound_MatchupIds_AreUnique()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1), new("pD", 1) };
            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            var ids = round.Matchups.Select(m => m.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "All matchup IDs must be unique.");
        }

        [TestMethod]
        public void GenerateRound_EmptyEntrantList_ProducesEmptyRound()
        {
            var round = SwissTournamentService.GenerateRound(1, new List<EntrantId>(), [], null);
            Assert.AreEqual(0, round.Matchups.Count);
        }

        [TestMethod]
        public void GenerateRound_SingleEntrant_ProducesEmptyRoundWithBye()
        {
            var round = SwissTournamentService.GenerateRound(1, [new EntrantId("pA", 1)], [], null);
            Assert.AreEqual(0, round.Matchups.Count);
            Assert.AreEqual(1, round.Byes.Count, "Single entrant should receive a bye.");
            Assert.AreEqual(new EntrantId("pA", 1), round.Byes[0]);
        }

        // ── Bye rotation ─────────────────────────────────────────────────────

        [TestMethod]
        public void GenerateRound_OddEntrants_ByeGoesToLowestRanked()
        {
            // With no previous wins, lowest-ranked = last alphabetically.
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1) };

            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            // pC is last alphabetically (lowest ranked with equal wins), should get bye.
            Assert.AreEqual(new EntrantId("pC", 1), round.Byes[0],
                "Bye should go to the lowest-ranked entrant.");
        }

        [TestMethod]
        public void GenerateRound_OddEntrants_ByeRotatesAcrossRounds()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1) };

            // Round 1: pC gets bye.
            var round1 = SwissTournamentService.GenerateRound(1, entrants, [], null);
            Assert.AreEqual(new EntrantId("pC", 1), round1.Byes[0]);

            // Round 2: pC already had a bye, so next lowest should get it.
            var round2 = SwissTournamentService.GenerateRound(2, entrants, [round1], new Dictionary<EntrantId, double>());
            Assert.AreNotEqual(new EntrantId("pC", 1), round2.Byes[0],
                "Bye should rotate to a different entrant in round 2.");
        }

        // ── Bye win accounting ───────────────────────────────────────────────

        [TestMethod]
        public void CalculateWins_ByeEntrant_ReceivesOneWin()
        {
            var rounds = new List<VotingRound>
            {
                new()
                {
                    RoundNumber = 1,
                    Matchups = [new(Guid.NewGuid(), new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
                    Byes = [new EntrantId("pC", 1)],
                },
            };

            var wins = SwissTournamentService.CalculateWins(rounds, []);

            Assert.AreEqual(1, wins.GetValueOrDefault(new EntrantId("pC", 1), 0),
                "Bye entrant should have 1 win.");
        }

        // ── Even entrants: no bye ────────────────────────────────────────────

        [TestMethod]
        public void GenerateRound_EvenEntrants_NoByes()
        {
            var entrants = new List<EntrantId> { new("pA", 1), new("pB", 1), new("pC", 1), new("pD", 1) };

            var round = SwissTournamentService.GenerateRound(1, entrants, [], null);

            Assert.AreEqual(2, round.Matchups.Count);
            Assert.AreEqual(0, round.Byes.Count, "Even entrant count should produce no byes.");
        }
    }
}
