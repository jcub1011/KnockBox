using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which players assemble their outfit by claiming items from the
    /// communal pool and selecting one per clothing type.
    ///
    /// Transition ownership:
    /// - Timer expiry → <see cref="OutfitCustomizationState"/> (auto-fills incomplete outfits first)
    /// - All players submit their outfit early → <see cref="OutfitCustomizationState"/>
    /// - <see cref="ClaimPoolItemCommand"/> → item claimed; no transition
    /// - <see cref="UnclaimPoolItemCommand"/> → claimed item released back to pool; no transition
    /// - <see cref="SubmitOutfitCommand"/> → outfit recorded; may trigger early advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class OutfitBuildingState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitBuildingTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.OutfitBuilding);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → OutfitBuildingState. Deadline: {deadline}.", _deadline);
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
                "Outfit building timer expired. Auto-filling incomplete outfits and moving to customization.");
            AutoFillIncompleteOutfits(context);
            return new OutfitCustomizationState();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleClaimPoolItem(
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

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitOutfit(
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

            player.SubmittedOutfit = new OutfitSubmission
            {
                PlayerId = cmd.PlayerId,
                SelectedItemsByType = new Dictionary<string, Guid>(cmd.SelectedItemsByType),
                SubmittedAt = DateTimeOffset.UtcNow,
            };

            context.Logger.LogInformation(
                "Player [{id}] submitted their outfit ({count} items).",
                cmd.PlayerId, cmd.SelectedItemsByType.Count);

            if (context.AllOutfitsSubmitted())
            {
                context.Logger.LogInformation(
                    "All outfits submitted. Moving to customization early.");
                return new OutfitCustomizationState();
            }

            return null;
        }

        /// <summary>
        /// For each player who has not yet submitted an outfit, builds a best-effort outfit
        /// from the items they own.  For each clothing type, prefers items drawn by other
        /// players (claimed items) over the player's own drawings so that self-drawn items
        /// are used only as a fallback.
        /// </summary>
        private static void AutoFillIncompleteOutfits(DrawnToDressGameContext context)
        {
            foreach (var player in context.GamePlayers.Values)
            {
                if (player.SubmittedOutfit is not null) continue;

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

                    // Prefer items drawn by other players to satisfy the self-drawn avoidance rule.
                    var preferred = candidates
                        .FirstOrDefault(i => !string.Equals(
                            i.CreatorPlayerId, player.PlayerId, StringComparison.Ordinal));

                    var chosen = preferred ?? candidates[0];
                    selectedItems[clothingType.Id] = chosen.Id;
                }

                if (selectedItems.Count == 0)
                {
                    context.Logger.LogWarning(
                        "Auto-fill: player [{id}] has no available items; outfit left empty.",
                        player.PlayerId);
                    continue;
                }

                player.SubmittedOutfit = new OutfitSubmission
                {
                    PlayerId = player.PlayerId,
                    SelectedItemsByType = selectedItems,
                    SubmittedAt = DateTimeOffset.UtcNow,
                };

                context.Logger.LogInformation(
                    "Auto-filled outfit for player [{id}] ({count} item(s)).",
                    player.PlayerId, selectedItems.Count);
            }
        }
    }
}
