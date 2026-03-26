using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Second timed outfit-building phase. Players assemble Outfit 2 from the pool that
    /// remains after all Outfit 1 picks have been removed.
    ///
    /// Pool-reset rules applied on entry:
    /// <list type="bullet">
    ///   <item><description>
    ///     Items selected in any player's Outfit 1 are removed from the communal pool
    ///     (<see cref="DrawnClothingItem.IsInPool"/> set to <see langword="false"/>) and
    ///     all claims are cleared.
    ///   </description></item>
    ///   <item><description>
    ///     Every player's <see cref="DrawnToDressPlayerState.OwnedClothingItemIds"/> is
    ///     reset and then re-populated with their self-drawn items that remain in the pool.
    ///   </description></item>
    ///   <item><description>
    ///     When <see cref="DrawnToDressConfig.CanReuseOutfit1Items"/> is
    ///     <see langword="true"/>, each player's own Outfit 1 picks are additionally added
    ///     back to their owned set so they may be reused.
    ///   </description></item>
    /// </list>
    ///
    /// Submission validation:
    /// <list type="bullet">
    ///   <item><description>
    ///     All standard ownership and clothing-type checks apply (same as Outfit 1 Building).
    ///   </description></item>
    ///   <item><description>
    ///     When <see cref="DrawnToDressConfig.Outfit2DistinctnessThreshold"/> is greater
    ///     than zero, an Outfit 2 that shares that many items with <em>any</em> player's
    ///     Outfit 1 is rejected with actionable log feedback.
    ///   </description></item>
    /// </list>
    ///
    /// Transition ownership:
    /// - Timer expiry → auto-fills incomplete Outfit 2s then → <see cref="VotingRoundSetupState"/>
    /// - All players submit Outfit 2 early → <see cref="VotingRoundSetupState"/>
    /// - <see cref="ClaimPoolItemCommand"/> → item claimed; no transition
    /// - <see cref="UnclaimPoolItemCommand"/> → claimed item released back to pool; no transition
    /// - <see cref="SubmitOutfitCommand"/> → Outfit 2 recorded; may trigger early advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class Outfit2BuildingState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitBuildingTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.Outfit2Building);
            context.ResetReadyFlags();
            // Perform the pool reset here as well so that tests and any paths that enter
            // Outfit2BuildingState directly (e.g. after a resume) start in a consistent state.
            // In the normal FSM flow Pool2RevealState already ran this on its entry, making
            // this call a safe no-op.
            Pool2RevealState.ResetPoolForOutfit2(context);
            context.Logger.LogInformation(
                "FSM → Outfit2BuildingState. Pool has {count} item(s) after Outfit 1 picks removed. Deadline: {deadline}.",
                context.ClothingPool.Values.Count(i => i.IsInPool), _deadline);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            return Result.Success;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case ClaimPoolItemCommand cmd:
                    return HandleClaimPoolItem(context, cmd);

                case UnclaimPoolItemCommand cmd:
                    return HandleUnclaimPoolItem(context, cmd);

                case SubmitOutfitCommand cmd:
                    return HandleSubmitOutfit2(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => _deadline - now;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (now < _deadline) return null;

            context.Logger.LogInformation(
                "Outfit 2 building timer expired. Auto-filling incomplete Outfit 2s and moving to voting.");
            AutoFillIncompleteOutfit2s(context);
            return new VotingRoundSetupState();
        }

        // ── Claim / unclaim ───────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleClaimPoolItem(
            DrawnToDressGameContext context, ClaimPoolItemCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "Outfit2 ClaimPoolItem: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (!context.ClothingPool.TryGetValue(cmd.ItemId, out var item))
            {
                context.Logger.LogWarning(
                    "Outfit2 ClaimPoolItem: item [{itemId}] not found in pool.", cmd.ItemId);
                return null;
            }

            if (!item.IsInPool)
            {
                context.Logger.LogWarning(
                    "Outfit2 ClaimPoolItem: item [{itemId}] is not in the Outfit 2 pool.", cmd.ItemId);
                return null;
            }

            // Players may not claim items they drew themselves.
            if (string.Equals(item.CreatorPlayerId, cmd.PlayerId, StringComparison.Ordinal))
            {
                context.Logger.LogWarning(
                    "Outfit2 ClaimPoolItem: player [{id}] attempted to claim their own item [{itemId}].",
                    cmd.PlayerId, cmd.ItemId);
                return null;
            }

            // First valid claim wins.
            if (item.ClaimedByPlayerId is not null)
            {
                context.Logger.LogWarning(
                    "Outfit2 ClaimPoolItem: item [{itemId}] is already claimed by [{claimer}].",
                    cmd.ItemId, item.ClaimedByPlayerId);
                return null;
            }

            item.ClaimedByPlayerId = cmd.PlayerId;
            player.OwnedClothingItemIds.Add(item.Id);

            context.Logger.LogInformation(
                "Player [{id}] claimed Outfit 2 pool item [{itemId}].", cmd.PlayerId, item.Id);
            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleUnclaimPoolItem(
            DrawnToDressGameContext context, UnclaimPoolItemCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "Outfit2 UnclaimPoolItem: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (!context.ClothingPool.TryGetValue(cmd.ItemId, out var item))
            {
                context.Logger.LogWarning(
                    "Outfit2 UnclaimPoolItem: item [{itemId}] not found in pool.", cmd.ItemId);
                return null;
            }

            // Only the player who claimed the item may unclaim it.
            if (!string.Equals(item.ClaimedByPlayerId, cmd.PlayerId, StringComparison.Ordinal))
            {
                context.Logger.LogWarning(
                    "Outfit2 UnclaimPoolItem: player [{id}] does not own the claim on item [{itemId}].",
                    cmd.PlayerId, cmd.ItemId);
                return null;
            }

            item.ClaimedByPlayerId = null;
            player.OwnedClothingItemIds.Remove(item.Id);

            context.Logger.LogInformation(
                "Player [{id}] released claim on Outfit 2 pool item [{itemId}].", cmd.PlayerId, item.Id);
            return null;
        }

        // ── Submit Outfit 2 ───────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitOutfit2(
            DrawnToDressGameContext context, SubmitOutfitCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "Outfit2 SubmitOutfit: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            // Validate every selected item (ownership and type-match).
            foreach (var (typeId, itemId) in cmd.SelectedItemsByType)
            {
                if (!player.OwnedClothingItemIds.Contains(itemId))
                {
                    context.Logger.LogWarning(
                        "Outfit2 SubmitOutfit: player [{id}] does not own item [{itemId}].",
                        cmd.PlayerId, itemId);
                    return null;
                }

                if (!context.ClothingPool.TryGetValue(itemId, out var item))
                {
                    context.Logger.LogWarning(
                        "Outfit2 SubmitOutfit: item [{itemId}] not found in pool.", itemId);
                    return null;
                }

                if (!string.Equals(item.ClothingTypeId, typeId, StringComparison.Ordinal))
                {
                    context.Logger.LogWarning(
                        "Outfit2 SubmitOutfit: item [{itemId}] has type [{actual}] but was submitted for slot [{slot}].",
                        itemId, item.ClothingTypeId, typeId);
                    return null;
                }
            }

            // Distinctness check: Outfit 2 must not be too similar to any Outfit 1.
            int threshold = context.Config.Outfit2DistinctnessThreshold;
            if (threshold > 0)
            {
                var candidate = new OutfitSubmission
                {
                    PlayerId = cmd.PlayerId,
                    SelectedItemsByType = new Dictionary<string, Guid>(cmd.SelectedItemsByType),
                };

                var allOutfit1s = context.GamePlayers.Values
                    .Where(p => p.SubmittedOutfit is not null)
                    .Select(p => p.SubmittedOutfit!);

                if (OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(candidate, allOutfit1s, threshold))
                {
                    // Find the most similar Outfit 1 for feedback.
                    var worstOutfit1 = allOutfit1s
                        .OrderByDescending(o1 => OutfitDistinctnessEvaluator.CountSharedItems(o1, candidate))
                        .First();
                    int shared = OutfitDistinctnessEvaluator.CountSharedItems(worstOutfit1, candidate);

                    context.Logger.LogWarning(
                        "Outfit2 SubmitOutfit: player [{id}]'s Outfit 2 shares {count} item(s) with Outfit 1 " +
                        "(owner: [{outfit1Owner}]), which meets or exceeds the distinctness threshold of {threshold}. " +
                        "Submission rejected. Please swap at least {needed} item(s) to make Outfit 2 sufficiently different.",
                        cmd.PlayerId, shared, worstOutfit1.PlayerId, threshold, shared - threshold + 1);
                    return null;
                }
            }

            player.SubmittedOutfit2 = new OutfitSubmission
            {
                PlayerId = cmd.PlayerId,
                SelectedItemsByType = new Dictionary<string, Guid>(cmd.SelectedItemsByType),
                SubmittedAt = DateTimeOffset.UtcNow,
            };

            context.Logger.LogInformation(
                "Player [{id}] submitted Outfit 2 ({count} items).",
                cmd.PlayerId, cmd.SelectedItemsByType.Count);

            if (AllOutfit2sSubmitted(context))
            {
                context.Logger.LogInformation(
                    "All Outfit 2s submitted. Moving to voting early.");
                return new VotingRoundSetupState();
            }

            return null;
        }

        // ── Auto-fill ─────────────────────────────────────────────────────────

        /// <summary>
        /// For each player who has not yet submitted an Outfit 2, builds a best-effort
        /// outfit from owned items, preferring selections that satisfy the distinctness
        /// rule.  If no fully-distinct outfit can be assembled the closest possible
        /// selection is used.
        /// </summary>
        private static void AutoFillIncompleteOutfit2s(DrawnToDressGameContext context)
        {
            var allOutfit1s = context.GamePlayers.Values
                .Where(p => p.SubmittedOutfit is not null)
                .Select(p => p.SubmittedOutfit!)
                .ToList();

            int threshold = context.Config.Outfit2DistinctnessThreshold;

            foreach (var player in context.GamePlayers.Values)
            {
                if (player.SubmittedOutfit2 is not null) continue;

                var selectedItems = BuildBestEffortOutfit2(context, player, allOutfit1s, threshold);

                if (selectedItems.Count == 0)
                {
                    context.Logger.LogWarning(
                        "Outfit2 auto-fill: player [{id}] has no available items; Outfit 2 left empty.",
                        player.PlayerId);
                    continue;
                }

                player.SubmittedOutfit2 = new OutfitSubmission
                {
                    PlayerId = player.PlayerId,
                    SelectedItemsByType = selectedItems,
                    SubmittedAt = DateTimeOffset.UtcNow,
                };

                bool violates = threshold > 0 && OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(
                    player.SubmittedOutfit2, allOutfit1s, threshold);

                if (violates)
                {
                    context.Logger.LogWarning(
                        "Outfit2 auto-fill: player [{id}]'s Outfit 2 still violates distinctness " +
                        "(no distinct alternative found). Using best-effort outfit.",
                        player.PlayerId);
                }
                else
                {
                    context.Logger.LogInformation(
                        "Auto-filled Outfit 2 for player [{id}] ({count} item(s)).",
                        player.PlayerId, selectedItems.Count);
                }
            }
        }

        /// <summary>
        /// Builds the best-effort Outfit 2 for a single player by iterating over clothing
        /// types and selecting the least-conflicting owned item for each slot.
        /// </summary>
        private static Dictionary<string, Guid> BuildBestEffortOutfit2(
            DrawnToDressGameContext context,
            DrawnToDressPlayerState player,
            IReadOnlyList<OutfitSubmission> allOutfit1s,
            int threshold)
        {
            var selected = new Dictionary<string, Guid>();

            foreach (var clothingType in context.Config.ClothingTypes)
            {
                // Candidates: owned items of the correct type.
                var candidates = player.OwnedClothingItemIds
                    .Where(id => context.ClothingPool.TryGetValue(id, out var pi)
                                 && pi.ClothingTypeId == clothingType.Id)
                    .Select(id => context.ClothingPool[id])
                    .ToList();

                if (candidates.Count == 0) continue;

                // Try to find an item that does not appear in any Outfit 1 in this slot.
                var nonConflicting = candidates.FirstOrDefault(candidate =>
                    !allOutfit1s.Any(o1 =>
                        o1.SelectedItemsByType.TryGetValue(clothingType.Id, out var o1Item)
                        && o1Item == candidate.Id));

                // Fall back to the first candidate if all conflict.
                var chosen = nonConflicting ?? candidates[0];
                selected[clothingType.Id] = chosen.Id;
            }

            return selected;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool AllOutfit2sSubmitted(DrawnToDressGameContext context)
            => context.GamePlayers.Count > 0
               && context.GamePlayers.Values.All(p => p.SubmittedOutfit2 is not null);
    }
}
