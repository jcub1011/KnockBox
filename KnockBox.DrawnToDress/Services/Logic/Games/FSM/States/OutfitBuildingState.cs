using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Timed phase in which players assemble their outfit by claiming items from the
    /// communal pool and selecting one per clothing type. Supports multiple outfit rounds
    /// via the <c>outfitRound</c> parameter.
    ///
    /// For round > 1, the pool is reset on entry (previous round picks removed) and
    /// submitted outfits are validated for distinctness against earlier outfits.
    /// </summary>
    public sealed class OutfitBuildingState(int outfitRound = 1) : ITimedDrawnToDressGameState
    {
        private readonly int _outfitRound = outfitRound;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            if (context.Config.EnableTimer)
            {
                context.State.PhaseDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitBuildingTimeSec);
            }

            context.State.SetPhase(GamePhase.OutfitBuilding);
            context.CurrentOutfitRound = _outfitRound;
            context.ResetReadyFlags();

            if (_outfitRound > 1)
            {
                context.ResetPoolForRound(_outfitRound);
                context.Logger.LogDebug(
                    "FSM → OutfitBuildingState (round {round}). Pool has {count} item(s) after previous picks removed. Deadline: {deadline}.",
                    _outfitRound, context.ClothingPool.Values.Count(i => i.IsInPool), context.State.PhaseDeadlineUtc);
            }
            else
            {
                context.Logger.LogDebug(
                    "FSM → OutfitBuildingState. Deadline: {deadline}.", context.State.PhaseDeadlineUtc);
            }

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
                    return HandleSubmitOutfit(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                default:
                    context.Logger.LogWarning(
                        "OutfitBuildingState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => context.State.PhaseDeadlineUtc is { } deadline
                ? deadline - now
                : new ResultError("No timer active.");

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (context.State.PhaseDeadlineUtc is not { } deadline || now < deadline) return null;

            context.Logger.LogDebug(
                "Outfit building timer expired (round {round}). Auto-filling incomplete outfits and moving to customization.",
                _outfitRound);
            AutoFillIncompleteOutfits(context);
            return new OutfitCustomizationState(_outfitRound);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleClaimPoolItem(
            DrawnToDressGameContext context, ClaimPoolItemCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (!context.ClothingPool.TryGetValue(cmd.ItemId, out var item))
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: item [{itemId}] not found in pool.", cmd.ItemId);
                return null;
            }

            if (_outfitRound > 1 && !item.IsInPool)
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: item [{itemId}] is not in the round {round} pool.", cmd.ItemId, _outfitRound);
                return null;
            }

            // Players may not claim items they drew themselves (unless the config allows it).
            if (!context.Config.AllowSelectOwnDrawings
                && string.Equals(item.CreatorPlayerId, cmd.PlayerId, StringComparison.Ordinal))
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: player [{id}] attempted to claim their own item [{itemId}].",
                    cmd.PlayerId, cmd.ItemId);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromError(
                    "You can't claim your own drawing.");
            }

            // First valid claim wins — reject if already taken by another player.
            if (item.ClaimedByPlayerId is not null)
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: item [{itemId}] is already claimed by [{claimer}].",
                    cmd.ItemId, item.ClaimedByPlayerId);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromError(
                    "This item has already been claimed by another player.");
            }

            item.ClaimedByPlayerId = cmd.PlayerId;
            player.OwnedClothingItemIds.Add(item.Id);

            context.Logger.LogDebug(
                "Player [{id}] claimed pool item [{itemId}].", cmd.PlayerId, item.Id);
            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleUnclaimPoolItem(
            DrawnToDressGameContext context, UnclaimPoolItemCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "UnclaimPoolItem: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (!context.ClothingPool.TryGetValue(cmd.ItemId, out var item))
            {
                context.Logger.LogWarning(
                    "UnclaimPoolItem: item [{itemId}] not found in pool.", cmd.ItemId);
                return null;
            }

            // Only the player who claimed the item may unclaim it.
            if (!string.Equals(item.ClaimedByPlayerId, cmd.PlayerId, StringComparison.Ordinal))
            {
                context.Logger.LogWarning(
                    "UnclaimPoolItem: player [{id}] does not own the claim on item [{itemId}].",
                    cmd.PlayerId, cmd.ItemId);
                return null;
            }

            item.ClaimedByPlayerId = null;
            player.OwnedClothingItemIds.Remove(item.Id);

            context.Logger.LogDebug(
                "Player [{id}] released claim on pool item [{itemId}].", cmd.PlayerId, item.Id);
            return null;
        }

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitOutfit(
            DrawnToDressGameContext context, SubmitOutfitCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitOutfit: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            // Validate every selected item.
            foreach (var (typeId, itemId) in cmd.SelectedItemsByType)
            {
                // The player must own the item (either drawn themselves or claimed from the pool).
                if (!player.OwnedClothingItemIds.Contains(itemId))
                {
                    context.Logger.LogWarning(
                        "SubmitOutfit: player [{id}] does not own item [{itemId}].",
                        cmd.PlayerId, itemId);
                    return null;
                }

                // The item must exist in the pool and its clothing type must match the slot key.
                if (!context.ClothingPool.TryGetValue(itemId, out var item))
                {
                    context.Logger.LogWarning(
                        "SubmitOutfit: item [{itemId}] not found in pool.", itemId);
                    return null;
                }

                if (!string.Equals(item.ClothingTypeId, typeId, StringComparison.Ordinal))
                {
                    context.Logger.LogWarning(
                        "SubmitOutfit: item [{itemId}] has type [{actual}] but was submitted for slot [{slot}].",
                        itemId, item.ClothingTypeId, typeId);
                    return null;
                }
            }

            // Distinctness check for round > 1.
            if (_outfitRound > 1)
            {
                int threshold = context.Config.Outfit2DistinctnessThreshold;
                if (threshold > 0)
                {
                    var candidate = new OutfitSubmission
                    {
                        PlayerId = cmd.PlayerId,
                        SelectedItemsByType = new Dictionary<string, Guid>(cmd.SelectedItemsByType),
                    };

                    var allPreviousOutfits = context.GamePlayers.Values
                        .SelectMany(p => p.SubmittedOutfits
                            .Where(kv => kv.Key < _outfitRound)
                            .Select(kv => kv.Value));

                    if (OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(candidate, allPreviousOutfits, threshold))
                    {
                        var worstOutfit = allPreviousOutfits
                            .OrderByDescending(o => OutfitDistinctnessEvaluator.CountSharedItems(o, candidate))
                            .First();
                        int shared = OutfitDistinctnessEvaluator.CountSharedItems(worstOutfit, candidate);

                        context.Logger.LogWarning(
                            "SubmitOutfit round {round}: player [{id}]'s outfit shares {count} item(s) with a previous outfit " +
                            "(owner: [{owner}]), which meets or exceeds the distinctness threshold of {threshold}. " +
                            "Submission rejected.",
                            _outfitRound, cmd.PlayerId, shared, worstOutfit.PlayerId, threshold);
                        return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromError(
                            $"Your outfit shares too many items ({shared}) with a previous outfit. Please swap some items.");
                    }
                }
            }

            player.SetOutfit(_outfitRound, new OutfitSubmission
            {
                PlayerId = cmd.PlayerId,
                SelectedItemsByType = new Dictionary<string, Guid>(cmd.SelectedItemsByType),
                SubmittedAt = DateTimeOffset.UtcNow,
            });

            context.Logger.LogDebug(
                "Player [{id}] submitted outfit round {round} ({count} items).",
                cmd.PlayerId, _outfitRound, cmd.SelectedItemsByType.Count);

            if (context.AllOutfitsSubmittedForRound(_outfitRound))
            {
                context.Logger.LogDebug(
                    "All outfits submitted for round {round}. Moving to customization.", _outfitRound);
                return new OutfitCustomizationState(_outfitRound);
            }

            return null;
        }

        /// <summary>
        /// For each player who has not yet submitted an outfit for the current round,
        /// builds a best-effort outfit from the items they own.
        /// </summary>
        private void AutoFillIncompleteOutfits(DrawnToDressGameContext context)
        {
            // Pre-index previously used items by (typeId, itemId) for O(1) conflict lookups.
            var previouslyUsedItems = new HashSet<(string typeId, Guid itemId)>();
            if (_outfitRound > 1)
            {
                foreach (var p in context.GamePlayers.Values)
                {
                    foreach (var (round, outfit) in p.SubmittedOutfits)
                    {
                        if (round >= _outfitRound) continue;
                        foreach (var (typeId, itemId) in outfit.SelectedItemsByType)
                            previouslyUsedItems.Add((typeId, itemId));
                    }
                }
            }

            var allPreviousOutfits = _outfitRound > 1
                ? [.. context.GamePlayers.Values
                    .SelectMany(p => p.SubmittedOutfits
                        .Where(kv => kv.Key < _outfitRound)
                        .Select(kv => kv.Value))]
                : new List<OutfitSubmission>();

            int threshold = context.Config.Outfit2DistinctnessThreshold;

            // Pre-index pool items by clothing type for efficient lookup.
            // Use dictionary key (not item.Id) since callers may store items with mismatched keys.
            var poolByType = new Dictionary<string, List<(Guid Key, DrawnClothingItem Item)>>();
            foreach (var (key, item) in context.ClothingPool)
            {
                if (!poolByType.TryGetValue(item.ClothingTypeId, out var list))
                {
                    list = [];
                    poolByType[item.ClothingTypeId] = list;
                }
                list.Add((key, item));
            }

            foreach (var player in context.GamePlayers.Values)
            {
                if (player.GetOutfit(_outfitRound) is not null) continue;

                var ownedSet = player.OwnedClothingItemIds.ToHashSet();
                var selectedItems = new Dictionary<string, Guid>();

                foreach (var clothingType in context.Config.ClothingTypes)
                {
                    // Gather all items of this type owned by the player using the pre-indexed pool.
                    if (!poolByType.TryGetValue(clothingType.Id, out var typeItems)) continue;
                    var candidates = typeItems
                        .Where(entry => ownedSet.Contains(entry.Key))
                        .Select(entry => entry.Item)
                        .ToList();

                    if (candidates.Count == 0) continue;

                    if (_outfitRound > 1 && previouslyUsedItems.Count > 0)
                    {
                        // Try to find an item that does not appear in any previous outfit in this slot.
                        var nonConflicting = candidates.FirstOrDefault(candidate =>
                            !previouslyUsedItems.Contains((clothingType.Id, candidate.Id)));
                        var chosen = nonConflicting ?? candidates[0];
                        selectedItems[clothingType.Id] = chosen.Id;
                    }
                    else
                    {
                        // Prefer items drawn by other players (claimed) over self-drawn items.
                        var preferred = candidates
                            .FirstOrDefault(i => !string.Equals(
                                i.CreatorPlayerId, player.PlayerId, StringComparison.Ordinal));
                        var chosen = preferred ?? candidates[0];
                        selectedItems[clothingType.Id] = chosen.Id;
                    }
                }

                if (selectedItems.Count == 0)
                {
                    context.Logger.LogWarning(
                        "Auto-fill: player [{id}] has no available items for round {round}; outfit left empty.",
                        player.PlayerId, _outfitRound);
                    continue;
                }

                var submission = new OutfitSubmission
                {
                    PlayerId = player.PlayerId,
                    SelectedItemsByType = selectedItems,
                    SubmittedAt = DateTimeOffset.UtcNow,
                };

                player.SetOutfit(_outfitRound, submission);

                if (_outfitRound > 1 && threshold > 0 &&
                    OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(submission, allPreviousOutfits, threshold))
                {
                    context.Logger.LogWarning(
                        "Auto-fill: player [{id}]'s outfit round {round} still violates distinctness " +
                        "(no distinct alternative found). Using best-effort outfit.",
                        player.PlayerId, _outfitRound);
                }
                else
                {
                    context.Logger.LogDebug(
                        "Auto-filled outfit round {round} for player [{id}] ({count} item(s)).",
                        _outfitRound, player.PlayerId, selectedItems.Count);
                }
            }
        }
    }
}
