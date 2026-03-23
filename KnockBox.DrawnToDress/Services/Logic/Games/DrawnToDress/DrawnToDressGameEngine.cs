using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    public class DrawnToDressGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<DrawnToDressGameEngine> logger,
        ILogger<DrawnToDressGameState> stateLogger) : AbstractGameEngine
    {
        // ------------------------------------------------------------------
        // AbstractGameEngine overrides
        // ------------------------------------------------------------------

        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.",
                    $"Parameter {nameof(host)} was null.");

            var state = new DrawnToDressGameState(host, stateLogger);
            state.UpdateJoinableStatus(true);
            logger.LogInformation(
                "Created DrawnToDress game state with host [{hostId}].", host.Id);
            return state;
        }

        public override async Task<Result> StartAsync(
            User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not DrawnToDressGameState gameState)
                return Result.FromError(
                    "Error starting game.",
                    $"State type [{state?.GetType().Name ?? "null"}] is not {nameof(DrawnToDressGameState)}.");

            if (host != gameState.Host)
                return Result.FromError("Only the host can start the game.");

            if (gameState.Players.Count < gameState.Settings.MinPlayers)
                return Result.FromError(
                    $"At least {gameState.Settings.MinPlayers} players are required to start.");

            return gameState.Execute(() =>
            {
                gameState.UpdateJoinableStatus(false);
                gameState.SetPhase(GamePhase.Drawing);
                gameState.ResetDrawingTypeIndex();
            });
        }

        // ------------------------------------------------------------------
        // Drawing phase
        // ------------------------------------------------------------------

        /// <summary>
        /// Submits a drawn clothing item to the shared pool.
        /// Enforces the per-player per-type item limit.
        /// </summary>
        public Result SubmitDrawing(User player, DrawnToDressGameState state, string svgData)
        {
            if (state.CurrentPhase != GamePhase.Drawing)
                return Result.FromError("Cannot submit drawings outside of the drawing phase.");

            return state.Execute(() =>
            {
                int existing = state.AllDrawings
                    .Count(d => d.CreatorId == player.Id && d.Type == state.CurrentDrawingType);

                if (existing >= state.Settings.MaxItemsPerType)
                    throw new InvalidOperationException(
                        $"Maximum of {state.Settings.MaxItemsPerType} items per type already reached.");

                var item = new ClothingItem
                {
                    CreatorId = player.Id,
                    CreatorName = player.Name,
                    Type = state.CurrentDrawingType,
                    SvgData = svgData,
                };

                state.AddDrawing(item);
            });
        }

        /// <summary>
        /// Host advances to the next clothing type, or ends the drawing phase when all types are done.
        /// </summary>
        public Result AdvanceDrawingRound(User host, DrawnToDressGameState state)
        {
            if (host != state.Host)
                return Result.FromError("Only the host can advance the drawing round.");

            if (state.CurrentPhase != GamePhase.Drawing)
                return Result.FromError("Not in the drawing phase.");

            return state.Execute(() =>
            {
                if (state.IsLastDrawingType)
                {
                    // All clothing types done → transition to outfit 1 building
                    state.SetCurrentOutfitRound(1);
                    state.SetPhase(GamePhase.OutfitBuilding);
                    BuildAvailablePool(state, outfitRound: 1);
                }
                else
                {
                    state.AdvanceDrawingType();
                }
            });
        }

        // ------------------------------------------------------------------
        // Outfit building phase
        // ------------------------------------------------------------------

        /// <summary>
        /// A player claims an item from the shared pool. First claim wins.
        /// Players cannot claim items they drew themselves.
        /// </summary>
        public Result ClaimItem(User player, DrawnToDressGameState state, Guid itemId)
        {
            if (state.CurrentPhase != GamePhase.OutfitBuilding)
                return Result.FromError("Cannot claim items outside of the outfit building phase.");

            // Validate the item exists and doesn't belong to the player (no lock needed – creator never changes)
            var itemMeta = state.AllDrawings.FirstOrDefault(d => d.Id == itemId);
            if (itemMeta is null)
                return Result.FromError("Item not found.");

            if (itemMeta.CreatorId == player.Id)
                return Result.FromError("You cannot claim your own drawing.");

            return state.Execute(() =>
            {
                var outfit = GetOrCreatePendingOutfit(player, state);

                // Remember what was in that slot before
                var existing = outfit.Items[itemMeta.Type];

                // Attempt to claim the item (removes from available pool atomically)
                var claimed = state.ClaimItem(itemId);
                if (claimed is null)
                    throw new InvalidOperationException("Item has already been claimed by another player.");

                // Return the previously held item to the pool
                if (existing is not null)
                    state.ReturnItem(existing);

                outfit.Items[itemMeta.Type] = claimed;
            });
        }

        /// <summary>
        /// A player locks their outfit. Once locked, picks cannot be changed.
        /// </summary>
        public Result LockOutfit(User player, DrawnToDressGameState state)
        {
            if (state.CurrentPhase != GamePhase.OutfitBuilding)
                return Result.FromError("Cannot lock outfit outside of outfit building phase.");

            return state.Execute(() =>
            {
                var outfit = state.GetPlayerOutfit(player.Id, state.CurrentOutfitRound);
                if (outfit is null)
                    throw new InvalidOperationException("No outfit in progress.");

                if (!outfit.IsComplete)
                    throw new InvalidOperationException(
                        "Outfit must have one item of each clothing type before locking.");

                outfit.IsLocked = true;
            });
        }

        /// <summary>
        /// Host ends the outfit building phase and moves all players to customization.
        /// Any incomplete outfits are auto-submitted with whatever items are selected.
        /// </summary>
        public Result EndOutfitBuilding(User host, DrawnToDressGameState state)
        {
            if (host != state.Host)
                return Result.FromError("Only the host can end the outfit building phase.");

            if (state.CurrentPhase != GamePhase.OutfitBuilding)
                return Result.FromError("Not in the outfit building phase.");

            return state.Execute(() =>
            {
                // Auto-lock any unlocked complete outfits
                foreach (var outfit in state.Outfits.Values
                    .Where(o => o.OutfitNumber == state.CurrentOutfitRound && !o.IsLocked))
                {
                    if (outfit.IsComplete)
                        outfit.IsLocked = true;
                }

                state.SetPhase(GamePhase.OutfitCustomization);
            });
        }

        // ------------------------------------------------------------------
        // Outfit customization phase
        // ------------------------------------------------------------------

        /// <summary>
        /// A player submits their outfit with a name (and optional sketch).
        /// For Outfit 2, the distinctness rule is checked before accepting.
        /// </summary>
        public Result SubmitOutfit(
            User player,
            DrawnToDressGameState state,
            string name,
            string? sketchData = null)
        {
            if (state.CurrentPhase != GamePhase.OutfitCustomization)
                return Result.FromError("Cannot submit outfit outside of the customization phase.");

            if (string.IsNullOrWhiteSpace(name))
                return Result.FromError("An outfit name is required.");

            return state.Execute(() =>
            {
                var outfit = state.GetPlayerOutfit(player.Id, state.CurrentOutfitRound);
                if (outfit is null)
                    throw new InvalidOperationException("No outfit found for this player and round.");

                if (!outfit.IsComplete)
                    throw new InvalidOperationException("Outfit is incomplete. All clothing slots must be filled.");

                if (outfit.IsSubmitted)
                    throw new InvalidOperationException("Outfit has already been submitted.");

                // Distinctness check for Outfit 2
                if (state.CurrentOutfitRound == 2 && !state.IsDistinctFromAllOutfit1s(outfit))
                    throw new InvalidOperationException(
                        "Your second outfit is too similar to another player's first outfit. " +
                        "Please swap at least 2 items.");

                outfit.Name = name.Trim();
                outfit.SketchData = sketchData;
                outfit.IsSubmitted = true;

                // Ensure a score entry exists for this player
                var score = state.GetOrAddPlayerScore(player.Id, player.Name);
                if (!score.OutfitIds.Contains(outfit.Id))
                    score.OutfitIds.Add(outfit.Id);
            });
        }

        /// <summary>
        /// Host ends the customization phase. If all outfit rounds are done, begins voting;
        /// otherwise begins the next outfit building round.
        /// </summary>
        public Result EndCustomizationPhase(User host, DrawnToDressGameState state)
        {
            if (host != state.Host)
                return Result.FromError("Only the host can advance the game phase.");

            if (state.CurrentPhase != GamePhase.OutfitCustomization)
                return Result.FromError("Not in the customization phase.");

            return state.Execute(() =>
            {
                if (state.CurrentOutfitRound < state.Settings.NumOutfitRounds)
                {
                    // Move to next outfit round
                    int nextRound = state.CurrentOutfitRound + 1;
                    state.SetCurrentOutfitRound(nextRound);
                    state.SetPhase(GamePhase.OutfitBuilding);
                    BuildAvailablePool(state, nextRound);
                }
                else
                {
                    // All outfit rounds done → start voting tournament
                    InitializeVotingTournament(state);
                }
            });
        }

        // ------------------------------------------------------------------
        // Voting phase
        // ------------------------------------------------------------------

        /// <summary>
        /// Casts a vote for each criterion in the current voting round's matchup.
        /// <paramref name="votes"/> maps criterion → true if voting for Outfit A, false for Outfit B.
        /// </summary>
        public Result CastVote(
            User player,
            DrawnToDressGameState state,
            Guid matchupId,
            Dictionary<VotingCriterion, bool> votes)
        {
            if (state.CurrentPhase != GamePhase.Voting)
                return Result.FromError("Cannot vote outside of the voting phase.");

            return state.Execute(() =>
            {
                var matchup = state.VotingMatchups.FirstOrDefault(m => m.Id == matchupId);
                if (matchup is null)
                    throw new InvalidOperationException("Matchup not found.");

                if (matchup.IsComplete)
                    throw new InvalidOperationException("Voting for this matchup has already closed.");

                // Creators of the outfits cannot vote
                var outfitA = state.Outfits.GetValueOrDefault(matchup.OutfitAId);
                var outfitB = state.Outfits.GetValueOrDefault(matchup.OutfitBId);
                if (outfitA?.PlayerId == player.Id || outfitB?.PlayerId == player.Id)
                    throw new InvalidOperationException("You cannot vote on an outfit you created.");

                if (matchup.VotedPlayerIds.Contains(player.Id))
                    throw new InvalidOperationException("You have already voted on this matchup.");

                // Record votes for each criterion
                foreach (var criterion in state.Settings.VotingCriteria)
                {
                    if (!votes.TryGetValue(criterion, out bool voteForA))
                        throw new InvalidOperationException($"Missing vote for criterion '{criterion}'.");

                    if (!matchup.CriterionVotes.ContainsKey(criterion))
                        matchup.CriterionVotes[criterion] = (0, 0);

                    var (a, b) = matchup.CriterionVotes[criterion];
                    matchup.CriterionVotes[criterion] = voteForA ? (a + 1, b) : (a, b + 1);
                }

                matchup.VotedPlayerIds.Add(player.Id);
            });
        }

        /// <summary>
        /// Host finalizes the current voting round: tallies points, awards bonuses, and
        /// either advances to the next round or moves to the results phase.
        /// </summary>
        public Result FinalizeVotingRound(User host, DrawnToDressGameState state)
        {
            if (host != state.Host)
                return Result.FromError("Only the host can finalize a voting round.");

            if (state.CurrentPhase != GamePhase.Voting)
                return Result.FromError("Not in the voting phase.");

            return state.Execute(() =>
            {
                FinalizeCurrentRoundMatchups(state);

                if (state.CurrentVotingRound >= state.TotalVotingRounds)
                {
                    AwardTournamentBonus(state);
                    state.SetPhase(GamePhase.Results);
                }
                else
                {
                    // Generate next round pairings (Swiss: pair by current points)
                    state.AdvanceVotingRound();
                    GenerateSwissPairings(state);
                }
            });
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns or creates the in-progress outfit for a player in the current outfit round.
        /// </summary>
        private static Outfit GetOrCreatePendingOutfit(User player, DrawnToDressGameState state)
        {
            var existing = state.GetPlayerOutfit(player.Id, state.CurrentOutfitRound);
            if (existing is not null) return existing;

            var outfit = new Outfit
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                OutfitNumber = state.CurrentOutfitRound,
            };
            state.TryAddOutfit(outfit);
            return outfit;
        }

        /// <summary>
        /// Rebuilds the available pool for the given outfit round, respecting settings.
        /// </summary>
        private static void BuildAvailablePool(DrawnToDressGameState state, int outfitRound)
        {
            IEnumerable<Guid> excluded = Enumerable.Empty<Guid>();

            if (outfitRound == 2 && !state.Settings.CanReuseOutfit1Items)
            {
                // Exclude items used in any player's Outfit 1
                excluded = state.Outfits.Values
                    .Where(o => o.OutfitNumber == 1)
                    .SelectMany(o => o.ItemIds);
            }

            state.RebuildAvailablePool(excluded);
        }

        /// <summary>
        /// Calculates the number of Swiss rounds for a given outfit count.
        /// </summary>
        private static int CalculateSwissRounds(int outfitCount) =>
            outfitCount <= 1 ? 1 : (int)Math.Ceiling(Math.Log2(outfitCount));

        /// <summary>
        /// Sets up the first voting round.
        /// </summary>
        private void InitializeVotingTournament(DrawnToDressGameState state)
        {
            // Ensure all submitted outfits have score entries
            foreach (var outfit in state.Outfits.Values.Where(o => o.IsSubmitted))
                state.GetOrAddPlayerScore(outfit.PlayerId, outfit.PlayerName);

            int outfitCount = state.Outfits.Values.Count(o => o.IsSubmitted);
            int rounds = CalculateSwissRounds(outfitCount);

            state.SetTotalVotingRounds(rounds);
            state.SetPhase(GamePhase.Voting);
            state.AdvanceVotingRound(); // moves from 0 → 1

            GenerateSwissPairings(state);
        }

        /// <summary>
        /// Generates matchup pairings for the current voting round using the Swiss system.
        /// Round 1 pairs randomly; subsequent rounds pair by accumulated points (closest scores meet).
        /// </summary>
        private void GenerateSwissPairings(DrawnToDressGameState state)
        {
            var submittedOutfits = state.Outfits.Values
                .Where(o => o.IsSubmitted)
                .ToList();

            // Sort by points descending for Swiss, shuffle equally-scored groups
            var ordered = submittedOutfits
                .OrderByDescending(o =>
                    state.PlayerScores.TryGetValue(o.PlayerId, out var s) ? s.TotalPoints : 0)
                .ThenBy(_ => randomNumberService.GetRandomInt(0, int.MaxValue, RandomType.Fast))
                .ToList();

            // Pair adjacent outfits; if odd count the last outfit gets a bye (no matchup this round)
            for (int i = 0; i + 1 < ordered.Count; i += 2)
            {
                var matchup = new VotingMatchup
                {
                    OutfitAId = ordered[i].Id,
                    OutfitBId = ordered[i + 1].Id,
                    VotingRound = state.CurrentVotingRound,
                };

                // Pre-populate criterion vote counters
                foreach (var criterion in state.Settings.VotingCriteria)
                    matchup.CriterionVotes[criterion] = (0, 0);

                state.AddVotingMatchup(matchup);
            }
        }

        /// <summary>
        /// Tallies votes and awards points for all matchups in the current voting round.
        /// </summary>
        private void FinalizeCurrentRoundMatchups(DrawnToDressGameState state)
        {
            var roundMatchups = state.VotingMatchups
                .Where(m => m.VotingRound == state.CurrentVotingRound && !m.IsComplete)
                .ToList();

            // Track matchup wins per outfit for the round-win bonus
            var roundWins = new Dictionary<Guid, int>();

            foreach (var matchup in roundMatchups)
            {
                var outfitA = state.Outfits.GetValueOrDefault(matchup.OutfitAId);
                var outfitB = state.Outfits.GetValueOrDefault(matchup.OutfitBId);
                if (outfitA is null || outfitB is null) continue;

                int totalA = 0, totalB = 0;

                foreach (var criterion in state.Settings.VotingCriteria)
                {
                    int weight = state.Settings.CriterionWeights.TryGetValue(criterion, out var w) ? w : 5;

                    matchup.CriterionVotes.TryGetValue(criterion, out var cvotes);
                    var (pointsA, pointsB) = ResolveCriterionPoints(cvotes.VotesA, cvotes.VotesB, weight);

                    matchup.CriterionPoints[criterion] = (pointsA, pointsB);
                    totalA += pointsA;
                    totalB += pointsB;
                }

                state.AddPoints(outfitA.PlayerId, totalA);
                state.AddPoints(outfitB.PlayerId, totalB);

                // Determine which outfit won this matchup overall
                Guid? matchupWinnerId = totalA > totalB ? matchup.OutfitAId
                    : totalB > totalA ? matchup.OutfitBId
                    : (Guid?)null; // tie

                if (matchupWinnerId.HasValue)
                    roundWins[matchupWinnerId.Value] = roundWins.GetValueOrDefault(matchupWinnerId.Value) + 1;

                matchup.IsComplete = true;
            }

            // Award round-win bonus to the outfit with the most wins in this round
            if (state.Settings.RoundWinBonus > 0 && roundWins.Count > 0)
            {
                int maxWins = roundWins.Values.Max();
                var bonusRecipients = roundWins
                    .Where(kv => kv.Value == maxWins)
                    .Select(kv => kv.Key)
                    .ToList();

                // Only award if there is a sole leader
                if (bonusRecipients.Count == 1)
                {
                    var winnerOutfit = state.Outfits.GetValueOrDefault(bonusRecipients[0]);
                    if (winnerOutfit is not null)
                        state.AddPoints(winnerOutfit.PlayerId, state.Settings.RoundWinBonus);
                }
            }
        }

        /// <summary>
        /// Resolves points for a single criterion given raw vote counts.
        /// A coin flip (random) decides ties.
        /// </summary>
        private (int pointsA, int pointsB) ResolveCriterionPoints(int votesA, int votesB, int weight)
        {
            if (votesA > votesB) return (weight, 0);
            if (votesB > votesA) return (0, weight);

            // Tie: coin flip
            bool aWins = randomNumberService.GetRandomInt(0, 2, RandomType.Fast) == 0;
            return aWins ? (weight, 0) : (0, weight);
        }

        /// <summary>Awards the tournament-win bonus to the player with the highest total points.</summary>
        private static void AwardTournamentBonus(DrawnToDressGameState state)
        {
            if (state.Settings.TournamentWinBonus <= 0) return;
            if (state.PlayerScores.Count == 0) return;

            int maxPoints = state.PlayerScores.Values.Max(s => s.TotalPoints);
            var winners = state.PlayerScores.Values
                .Where(s => s.TotalPoints == maxPoints)
                .ToList();

            // Only award if there is a sole leader
            if (winners.Count == 1)
                state.AddPoints(winners[0].PlayerId, state.Settings.TournamentWinBonus);
        }
    }
}
