using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Per-game context that holds shared data and helpers used by DrawnToDress FSM states.
    /// Created when the game starts and stored on <see cref="DrawnToDressGameState.Context"/>.
    /// </summary>
    public class DrawnToDressGameContext
    {
        public DrawnToDressGameContext(
            DrawnToDressGameState state,
            IRandomNumberService rng,
            ILogger logger)
        {
            State = state;
            Rng = rng;
            Logger = logger;
        }

        // ── Core references ───────────────────────────────────────────────────

        /// <summary>The underlying game state for this session.</summary>
        public DrawnToDressGameState State { get; }

        public IRandomNumberService Rng { get; }
        public ILogger Logger { get; }

        /// <summary>The FSM that manages phase transitions for this game.</summary>
        public IFininteStateMachine<DrawnToDressGameContext, DrawnToDressCommand> Fsm { get; set; } = null!;

        // ── Convenience accessors ─────────────────────────────────────────────

        public DrawnToDressSettings Settings => State.Settings;

        public bool IsHost(string playerId) => State.Host.Id == playerId;

        // ── Drawing helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns how many items of the current clothing type the given player has already drawn.
        /// </summary>
        public int DrawingCountForPlayer(string playerId) =>
            State.AllDrawings.Count(d => d.CreatorId == playerId && d.Type == State.CurrentDrawingType);

        // ── Pool helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the available pool for the given outfit round, excluding
        /// Outfit 1 items when <see cref="DrawnToDressSettings.CanReuseOutfit1Items"/> is false.
        /// </summary>
        public void BuildAvailablePool(int outfitRound)
        {
            IEnumerable<Guid> excluded = Enumerable.Empty<Guid>();

            if (outfitRound >= 2 && !Settings.CanReuseOutfit1Items)
            {
                excluded = State.Outfits.Values
                    .Where(o => o.OutfitNumber == 1)
                    .SelectMany(o => o.ItemIds);
            }

            State.RebuildAvailablePool(excluded);
        }

        // ── Outfit helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the in-progress outfit for a player in the current outfit round,
        /// creating it if it doesn't yet exist.
        /// </summary>
        public Outfit GetOrCreatePendingOutfit(string playerId, string playerName)
        {
            var existing = State.GetPlayerOutfit(playerId, State.CurrentOutfitRound);
            if (existing is not null) return existing;

            var outfit = new Outfit
            {
                PlayerId = playerId,
                PlayerName = playerName,
                OutfitNumber = State.CurrentOutfitRound,
            };
            State.TryAddOutfit(outfit);
            return outfit;
        }

        // ── Voting / Swiss helpers ────────────────────────────────────────────

        /// <summary>Calculates the number of Swiss rounds for the given outfit count.</summary>
        public static int CalculateSwissRounds(int outfitCount) =>
            outfitCount <= 1 ? 1 : (int)Math.Ceiling(Math.Log2(outfitCount));

        /// <summary>
        /// Generates Swiss pairings for the current voting round, sorts by accumulated points
        /// (with random tie-breaking) and pairs adjacent outfits.
        /// </summary>
        public void GenerateSwissPairings()
        {
            var submittedOutfits = State.Outfits.Values
                .Where(o => o.IsSubmitted)
                .OrderByDescending(o =>
                    State.PlayerScores.TryGetValue(o.PlayerId, out var s) ? s.TotalPoints : 0)
                .ThenBy(_ => Rng.GetRandomInt(0, int.MaxValue, RandomType.Fast))
                .ToList();

            for (int i = 0; i + 1 < submittedOutfits.Count; i += 2)
            {
                var matchup = new VotingMatchup
                {
                    OutfitAId = submittedOutfits[i].Id,
                    OutfitBId = submittedOutfits[i + 1].Id,
                    VotingRound = State.CurrentVotingRound,
                };

                foreach (var criterion in Settings.VotingCriteria)
                    matchup.CriterionVotes[criterion] = (0, 0);

                State.AddVotingMatchup(matchup);
            }
        }

        /// <summary>
        /// Tallies votes for all incomplete matchups in the current round,
        /// awards per-criterion points, and applies the round-win bonus.
        /// </summary>
        public void FinalizeCurrentRoundMatchups()
        {
            var roundMatchups = State.VotingMatchups
                .Where(m => m.VotingRound == State.CurrentVotingRound && !m.IsComplete)
                .ToList();

            var roundWins = new Dictionary<Guid, int>();

            foreach (var matchup in roundMatchups)
            {
                var outfitA = State.Outfits.GetValueOrDefault(matchup.OutfitAId);
                var outfitB = State.Outfits.GetValueOrDefault(matchup.OutfitBId);
                if (outfitA is null || outfitB is null) continue;

                int totalA = 0, totalB = 0;

                foreach (var criterion in Settings.VotingCriteria)
                {
                    int weight = Settings.CriterionWeights.TryGetValue(criterion, out var w) ? w : 5;
                    matchup.CriterionVotes.TryGetValue(criterion, out var cvotes);
                    var (pointsA, pointsB) = ResolveCriterionPoints(cvotes.VotesA, cvotes.VotesB, weight);

                    matchup.CriterionPoints[criterion] = (pointsA, pointsB);
                    totalA += pointsA;
                    totalB += pointsB;
                }

                State.AddPoints(outfitA.PlayerId, totalA);
                State.AddPoints(outfitB.PlayerId, totalB);

                Guid? winnerId = totalA > totalB ? matchup.OutfitAId
                    : totalB > totalA ? matchup.OutfitBId
                    : (Guid?)null;

                if (winnerId.HasValue)
                    roundWins[winnerId.Value] = roundWins.GetValueOrDefault(winnerId.Value) + 1;

                matchup.IsComplete = true;
            }

            if (Settings.RoundWinBonus > 0 && roundWins.Count > 0)
            {
                int maxWins = roundWins.Values.Max();
                var bonusRecipients = roundWins.Where(kv => kv.Value == maxWins).Select(kv => kv.Key).ToList();

                if (bonusRecipients.Count == 1)
                {
                    var winnerOutfit = State.Outfits.GetValueOrDefault(bonusRecipients[0]);
                    if (winnerOutfit is not null)
                        State.AddPoints(winnerOutfit.PlayerId, Settings.RoundWinBonus);
                }
            }
        }

        /// <summary>Awards the tournament-win bonus to the sole highest scorer (if any).</summary>
        public void AwardTournamentBonus()
        {
            if (Settings.TournamentWinBonus <= 0 || State.PlayerScores.Count == 0) return;

            int maxPoints = State.PlayerScores.Values.Max(s => s.TotalPoints);
            var winners = State.PlayerScores.Values.Where(s => s.TotalPoints == maxPoints).ToList();

            if (winners.Count == 1)
                State.AddPoints(winners[0].PlayerId, Settings.TournamentWinBonus);
        }

        // ── Criterion helper ──────────────────────────────────────────────────

        /// <summary>
        /// Resolves points for a single criterion given raw vote counts.
        /// Ties are broken by a coin flip.
        /// </summary>
        public (int pointsA, int pointsB) ResolveCriterionPoints(int votesA, int votesB, int weight)
        {
            if (votesA > votesB) return (weight, 0);
            if (votesB > votesA) return (0, weight);
            bool aWins = Rng.GetRandomInt(0, 2, RandomType.Fast) == 0;
            return aWins ? (weight, 0) : (0, weight);
        }
    }
}
