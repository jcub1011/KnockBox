using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which players assemble their outfit by claiming items from the
    /// communal pool and selecting one per clothing type. Supports multiple outfit rounds
    /// via the <c>outfitRound</c> parameter.
    ///
    /// For round > 1, the pool is reset on entry (previous round picks removed) and
    /// submitted outfits are validated for distinctness against earlier outfits.
    /// </summary>
    public sealed class OutfitBuildingState : ITimedDrawnToDressGameState
    {
        public bool IsTimerOptional => true;

        private readonly int _outfitRound;
        private DateTimeOffset _deadline;

        public OutfitBuildingState(int outfitRound = 1)
        {
            _outfitRound = outfitRound;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitBuildingTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.OutfitBuilding);
            context.CurrentOutfitRound = _outfitRound;
            context.ResetReadyFlags();

            if (_outfitRound > 1)
            {
                context.ResetPoolForRound(_outfitRound);
                context.Logger.LogInformation(
                    "FSM → OutfitBuildingState (round {round}). Pool has {count} item(s) after previous picks removed. Deadline: {deadline}.",
                    _outfitRound, context.ClothingPool.Values.Count(i => i.IsInPool), _deadline);
            }
            else
            {
                context.Logger.LogInformation(
                    "FSM → OutfitBuildingState. Deadline: {deadline}.", _deadline);
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

            // Players may not claim items they drew themselves.
            if (string.Equals(item.CreatorPlayerId, cmd.PlayerId, StringComparison.Ordinal))
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: player [{id}] attempted to claim their own item [{itemId}].",
                    cmd.PlayerId, cmd.ItemId);
                return null;
            }

            // First valid claim wins — reject if already taken by another player.
            if (item.ClaimedByPlayerId is not null)
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: item [{itemId}] is already claimed by [{claimer}].",
                    cmd.ItemId, item.ClaimedByPlayerId);
                return null;
            }

            item.ClaimedByPlayerId = cmd.PlayerId;
            player.OwnedClothingItemIds.Add(item.Id);

            context.Logger.LogInformation(
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

            context.Logger.LogInformation(
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
                        return null;
                    }
                }
            }

            player.SetOutfit(_outfitRound, new OutfitSubmission
            {
                PlayerId = cmd.PlayerId,
                SelectedItemsByType = new Dictionary<string, Guid>(cmd.SelectedItemsByType),
                SubmittedAt = DateTimeOffset.UtcNow,
            });

            context.Logger.LogInformation(
                "Player [{id}] submitted outfit round {round} ({count} items).",
                cmd.PlayerId, _outfitRound, cmd.SelectedItemsByType.Count);

            if (context.AllOutfitsSubmittedForRound(_outfitRound))
            {
                context.Logger.LogInformation(
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
            var allPreviousOutfits = _outfitRound > 1
                ? context.GamePlayers.Values
                    .SelectMany(p => p.SubmittedOutfits
                        .Where(kv => kv.Key < _outfitRound)
                        .Select(kv => kv.Value))
                    .ToList()
                : new List<OutfitSubmission>();

            int threshold = context.Config.Outfit2DistinctnessThreshold;

            foreach (var player in context.GamePlayers.Values)
            {
                if (player.GetOutfit(_outfitRound) is not null) continue;

                var selectedItems = new Dictionary<string, Guid>();

                foreach (var clothingType in context.Config.ClothingTypes)
                {
                    // Gather all items of this type owned by the player.
                    var candidates = player.OwnedClothingItemIds
                        .Where(id => context.ClothingPool.TryGetValue(id, out var poolItem)
                                     && poolItem.ClothingTypeId == clothingType.Id)
                        .Select(id => context.ClothingPool[id])
                        .ToList();

                    if (candidates.Count == 0) continue;

                    if (_outfitRound > 1 && allPreviousOutfits.Count > 0)
                    {
                        // Try to find an item that does not appear in any previous outfit in this slot.
                        var nonConflicting = candidates.FirstOrDefault(candidate =>
                            !allPreviousOutfits.Any(o =>
                                o.SelectedItemsByType.TryGetValue(clothingType.Id, out var oItem)
                                && oItem == candidate.Id));
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
                    context.Logger.LogInformation(
                        "Auto-filled outfit round {round} for player [{id}] ({count} item(s)).",
                        _outfitRound, player.PlayerId, selectedItems.Count);
                }
            }
        }
    }
}
