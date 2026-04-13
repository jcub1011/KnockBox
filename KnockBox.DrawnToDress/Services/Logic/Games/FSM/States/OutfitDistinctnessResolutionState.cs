using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.DrawnToDress.Services.State.Games;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Handles the case where two or more outfits share an item in the same clothing-type
    /// slot and <see cref="DrawnToDressConfig.RequireDistinctItemsPerSlot"/> is enabled.
    ///
    /// Each affected player must submit a <see cref="ResolveDistinctnessCommand"/> with a
    /// replacement item before voting can begin.
    ///
    /// Transition ownership:
    /// - All conflicts resolved → <see cref="VotingRoundSetupState"/>
    /// - <see cref="ResolveDistinctnessCommand"/> → resolves one conflict; may trigger advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// </summary>
    public sealed class OutfitDistinctnessResolutionState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.OutfitDistinctnessResolution);
            context.ResetReadyFlags();
            context.Logger.LogDebug("FSM → OutfitDistinctnessResolutionState");
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case ResolveDistinctnessCommand cmd:
                    return HandleResolveDistinctness(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);


                default:
                    context.Logger.LogWarning(
                        "OutfitDistinctnessResolutionState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleResolveDistinctness(
            DrawnToDressGameContext context, ResolveDistinctnessCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "ResolveDistinctness: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (player.SubmittedOutfit is null)
            {
                context.Logger.LogWarning(
                    "ResolveDistinctness: player [{id}] has no submitted outfit.", cmd.PlayerId);
                return null;
            }

            if (!context.ClothingPool.TryGetValue(cmd.ReplacementItemId, out var replacement))
            {
                context.Logger.LogWarning(
                    "ResolveDistinctness: replacement item [{itemId}] not found in pool.",
                    cmd.ReplacementItemId);
                return null;
            }

            // Swap out the conflicting item in the player's outfit for the chosen replacement.
            string typeId = replacement.ClothingTypeId;
            player.SubmittedOutfit.SelectedItemsByType[typeId] = replacement.Id;
            player.IsReady = true;

            context.Logger.LogDebug(
                "Player [{id}] resolved distinctness conflict: type [{type}] → item [{itemId}].",
                cmd.PlayerId, typeId, replacement.Id);

            if (context.AllPlayersReady())
            {
                context.Logger.LogDebug("All conflicts resolved. Moving to next outfit round pool reveal.");
                return new PoolRevealState(2);
            }

            return null;
        }
    }
}
