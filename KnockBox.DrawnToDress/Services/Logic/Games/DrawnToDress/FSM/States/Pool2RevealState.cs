using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed countdown state shown between Outfit 1 customization and Outfit 2 building.
    ///
    /// On entry the Outfit 2 pool is prepared:
    /// <list type="bullet">
    ///   <item><description>
    ///     Items selected in any player's Outfit 1 are removed from the communal pool
    ///     (<see cref="DrawnClothingItem.IsInPool"/> set to <see langword="false"/>).
    ///   </description></item>
    ///   <item><description>
    ///     All existing claims are cleared so the pool starts fresh.
    ///   </description></item>
    ///   <item><description>
    ///     Each player's <see cref="DrawnToDressPlayerState.OwnedClothingItemIds"/> is
    ///     rebuilt from their self-drawn items that remain in the pool.
    ///   </description></item>
    ///   <item><description>
    ///     When <see cref="DrawnToDressConfig.CanReuseOutfit1Items"/> is
    ///     <see langword="true"/>, each player's own Outfit 1 picks are additionally
    ///     added back to their owned set.
    ///   </description></item>
    /// </list>
    ///
    /// Players may browse the reset pool and press Ready to advance early.
    /// Claiming items is explicitly rejected during this phase (view-only).
    ///
    /// Transition ownership:
    /// - Timer expiry → <see cref="Outfit2BuildingState"/>
    /// - All players mark ready early → <see cref="Outfit2BuildingState"/>
    /// - <see cref="ClaimPoolItemCommand"/> → rejected (view-only phase)
    /// - <see cref="MarkReadyCommand"/> → tracked; may trigger early advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class Pool2RevealState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.PoolRevealTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.Pool2Reveal);
            context.ResetReadyFlags();
            ResetPoolForOutfit2(context);
            context.Logger.LogInformation(
                "FSM → Pool2RevealState. Outfit 2 pool has {count} item(s). Deadline: {deadline}.",
                context.ClothingPool.Values.Count(i => i.IsInPool), _deadline);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            return Result.Success;
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => _deadline - now;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (now < _deadline) return null;

            context.Logger.LogInformation(
                "Outfit 2 pool reveal timer expired. Advancing to Outfit 2 building.");
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                new Outfit2BuildingState());
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case MarkReadyCommand cmd:
                    return HandleMarkReady(context, cmd);

                case ClaimPoolItemCommand:
                    context.Logger.LogWarning(
                        "ClaimPoolItem rejected: Outfit 2 pool reveal is view-only. Claims open in Outfit 2 building.");
                    return null;

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleMarkReady(
            DrawnToDressGameContext context, MarkReadyCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "Pool2Reveal MarkReady: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            player.IsReady = true;
            context.Logger.LogInformation(
                "Player [{id}] marked ready during Outfit 2 pool reveal.", cmd.PlayerId);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players ready during Outfit 2 pool reveal. Advancing early to Outfit 2 building.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                    new Outfit2BuildingState());
            }

            return null;
        }

        /// <summary>
        /// Resets the communal pool for Outfit 2: removes Outfit 1 picks, clears all
        /// claims, and rebuilds every player's owned-item set.
        /// </summary>
        internal static void ResetPoolForOutfit2(DrawnToDressGameContext context)
        {
            // Collect all item IDs that were selected in any Outfit 1 submission.
            var outfit1Picks = context.GamePlayers.Values
                .Where(p => p.SubmittedOutfit is not null)
                .SelectMany(p => p.SubmittedOutfit!.SelectedItemsByType.Values)
                .ToHashSet();

            // Update pool membership and clear all claims for Outfit 2.
            foreach (var item in context.ClothingPool.Values)
            {
                if (outfit1Picks.Contains(item.Id))
                    item.IsInPool = false;
                // Reset claim regardless (Outfit 2 starts with a fresh claim slate).
                item.ClaimedByPlayerId = null;
            }

            // Rebuild each player's owned-item set for Outfit 2.
            foreach (var player in context.GamePlayers.Values)
            {
                player.OwnedClothingItemIds.Clear();

                // Self-drawn items that are still in the pool are automatically owned.
                foreach (var item in context.ClothingPool.Values)
                {
                    if (item.IsInPool &&
                        string.Equals(item.CreatorPlayerId, player.PlayerId, StringComparison.Ordinal))
                    {
                        player.OwnedClothingItemIds.Add(item.Id);
                    }
                }

                // When reuse is permitted, add the player's own Outfit 1 picks back.
                if (context.Config.CanReuseOutfit1Items && player.SubmittedOutfit is not null)
                {
                    foreach (var itemId in player.SubmittedOutfit.SelectedItemsByType.Values)
                    {
                        if (!player.OwnedClothingItemIds.Contains(itemId))
                            player.OwnedClothingItemIds.Add(itemId);
                    }
                }
            }
        }
    }
}
