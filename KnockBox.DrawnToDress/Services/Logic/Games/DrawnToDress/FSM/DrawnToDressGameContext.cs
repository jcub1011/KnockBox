using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;

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

        // ── Timer helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns all game participants (host + registered players).
        /// </summary>
        public IReadOnlyList<User> AllParticipants =>
            [State.Host, .. State.Players];

        /// <summary>
        /// For every participant that has not yet locked their outfit in the current round,
        /// fills any empty slots with random available items they did not draw, then locks
        /// the outfit. Called when the outfit-building timer expires.
        /// </summary>
        public void AutoFillAndLockIncompleteOutfits()
        {
            foreach (var participant in AllParticipants)
            {
                var outfit = GetOrCreatePendingOutfit(participant.Id, participant.Name);
                if (outfit.IsLocked) continue;

                // Fill any empty slot with a random available item of that type
                foreach (var type in Settings.ClothingTypes)
                {
                    if (outfit.Items.ContainsKey(type) && outfit.Items[type] is not null) continue;

                    var candidates = State.AvailablePool
                        .Where(i => i.Type == type && i.CreatorId != participant.Id)
                        .ToList();

                    if (candidates.Count == 0) continue;

                    int randomIndex = Rng.GetRandomInt(0, candidates.Count, RandomType.Fast);
                    var claimed = State.ClaimItem(candidates[randomIndex].Id);
                    if (claimed is not null)
                        outfit.Items[type] = claimed;
                }

                outfit.IsLocked = true;
            }
        }

        /// <summary>
        /// For any Outfit 2 that fails the distinctness check, swaps the conflicting items
        /// with random available items from the pool (Case 5: timer expiry swap).
        /// </summary>
        public void FixDistinctnessViolations()
        {
            if (State.CurrentOutfitRound < 2) return;

            foreach (var outfit2 in State.Outfits.Values
                .Where(o => o.OutfitNumber == 2 && !o.IsSubmitted))
            {
                // Repeat until distinct or no more swaps are possible
                for (int attempt = 0; attempt < Settings.ClothingTypes.Count; attempt++)
                {
                    var (isDistinct, _, _) = State.CheckDistinctnessWithDetails(outfit2);
                    if (isDistinct) break;

                    // Find all Outfit-1 item IDs that this outfit also contains
                    var outfit1Ids = State.Outfits.Values
                        .Where(o => o.OutfitNumber == 1)
                        .SelectMany(o => o.ItemIds)
                        .ToHashSet();

                    bool swapped = false;
                    foreach (var type in Settings.ClothingTypes)
                    {
                        var current = outfit2.Items.ContainsKey(type) ? outfit2.Items[type] : null;
                        if (current is null || !outfit1Ids.Contains(current.Id)) continue;

                        // Return the conflicting item and claim a different one
                        State.ReturnItem(current);
                        outfit2.Items[type] = null;

                        var candidates = State.AvailablePool
                            .Where(i => i.Type == type && i.CreatorId != outfit2.PlayerId)
                            .ToList();

                        if (candidates.Count > 0)
                        {
                            int randomIndex = Rng.GetRandomInt(0, candidates.Count, RandomType.Fast);
                            var claimed = State.ClaimItem(candidates[randomIndex].Id);
                            if (claimed is not null)
                                outfit2.Items[type] = claimed;
                        }

                        swapped = true;
                        break; // Re-check distinctness after each swap
                    }

                    if (!swapped) break; // No more swappable items
                }
            }
        }
    }
}
