using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which players add a custom name and finalize their outfit.
    /// Supports multiple outfit rounds via the <c>outfitRound</c> parameter.
    ///
    /// After round 1 customization, the engine checks for outfit distinctness conflicts.
    /// If conflicts exist, moves to <see cref="OutfitDistinctnessResolutionState"/>;
    /// otherwise proceeds to the next round's pool reveal or voting.
    /// </summary>
    public sealed class OutfitCustomizationState : ITimedDrawnToDressGameState
    {
        /// <summary>
        /// Default canvas width when no clothing types are configured.
        /// Must match the client-side default in OutfitCustomizationPhase.razor.
        /// </summary>
        private const int DefaultCanvasWidth = 600;

        /// <summary>
        /// Horizontal padding added to the max clothing-type canvas width to produce
        /// the composite canvas width. Must match the client-side value in OutfitCustomizationPhase.razor.
        /// </summary>
        private const int CanvasWidthPadding = 100;

        /// <summary>
        /// Scale factor applied to each clothing type's canvas height when computing the
        /// composite total height. Must match the client-side value in OutfitCustomizationPhase.razor.
        /// </summary>
        private const double HeightScaleFactor = 0.8;

        public bool IsTimerOptional => true;

        private readonly int _outfitRound;
        private DateTimeOffset _deadline;

        public OutfitCustomizationState(int outfitRound = 1)
        {
            _outfitRound = outfitRound;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitCustomizationTimeSec);
            if (context.Config.EnableTimer)
            {
                context.State.PhaseDeadlineUtc = _deadline;
            }

            context.State.SetPhase(GamePhase.OutfitCustomization);
            context.CurrentOutfitRound = _outfitRound;
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → OutfitCustomizationState (round {round}). Deadline: {deadline}.", _outfitRound, _deadline);
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
                case SubmitCustomizationCommand cmd:
                    return HandleSubmitCustomization(context, cmd);

                case UpdateDraftOutfitNameCommand cmd:
                    return HandleUpdateDraftOutfitName(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                default:
                    context.Logger.LogWarning(
                        "OutfitCustomizationState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
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

            context.Logger.LogInformation("Customization timer expired (round {round}).", _outfitRound);
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitCustomization(
            DrawnToDressGameContext context, SubmitCustomizationCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            var outfit = player.GetOutfit(_outfitRound);
            if (outfit is null)
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: player [{id}] has no submitted outfit for round {round}.",
                    cmd.PlayerId, _outfitRound);
                return null;
            }

            if (string.IsNullOrWhiteSpace(cmd.OutfitName))
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: player [{id}] submitted with no outfit name.", cmd.PlayerId);
                return null;
            }

            if (context.Config.SketchingRequired && string.IsNullOrWhiteSpace(cmd.SketchSvgContent))
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: player [{id}] submitted without a required sketch.", cmd.PlayerId);
                return null;
            }

            outfit.Customization.OutfitName = cmd.OutfitName.Trim();
            outfit.Customization.SketchSvgContent = string.IsNullOrWhiteSpace(cmd.SketchSvgContent)
                ? null
                : cmd.SketchSvgContent;

            if (cmd.ItemPositionOverrides is { Count: > 0 })
            {
                // Match the client composite-canvas dimensions exactly:
                // width = max clothing type canvas width + 100 padding
                // height = sum of each type's canvas height scaled to 80%
                int canvasWidth = (context.Config.ClothingTypes.Any()
                    ? context.Config.ClothingTypes.Max(ct => ct.CanvasWidth)
                    : DefaultCanvasWidth) + CanvasWidthPadding;
                int totalHeight = context.Config.ClothingTypes.Sum(ct => (int)(ct.CanvasHeight * HeightScaleFactor));

                var clothingTypeById = context.Config.ClothingTypes.ToDictionary(ct => ct.Id);

                foreach (var kvp in cmd.ItemPositionOverrides)
                {
                    if (!clothingTypeById.TryGetValue(kvp.Key, out var ct))
                    {
                        context.Logger.LogWarning(
                            "SubmitCustomization: player [{id}] submitted position override for unknown clothing type \"{typeId}\". Skipping.",
                            cmd.PlayerId, kvp.Key);
                        continue;
                    }

                    // Allow up to 50% of the item off each edge, matching the client drag/input bounds.
                    kvp.Value.X = Math.Clamp(kvp.Value.X, -ct.CanvasWidth / 2.0, canvasWidth - ct.CanvasWidth / 2.0);
                    kvp.Value.Y = Math.Clamp(kvp.Value.Y, -ct.CanvasHeight / 2.0, totalHeight - ct.CanvasHeight / 2.0);
                }

                outfit.Customization.ItemPositionOverrides = cmd.ItemPositionOverrides;
            }

            player.IsReady = true;

            context.Logger.LogInformation(
                "Player [{id}] submitted customization (round {round}). Outfit name: \"{name}\". Has sketch: {hasSketch}.",
                cmd.PlayerId, _outfitRound, cmd.OutfitName, cmd.SketchSvgContent is not null);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players submitted customization (round {round}). Advancing.", _outfitRound);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
            }

            return null;
        }

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleUpdateDraftOutfitName(
            DrawnToDressGameContext context, UpdateDraftOutfitNameCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is not null)
            {
                player.DraftOutfitName = cmd.DraftName;
                context.State.StateChangedEventManager.Notify();
            }
            return null;
        }

        private IGameState<DrawnToDressGameContext, DrawnToDressCommand> ChooseNextState(DrawnToDressGameContext context)
        {
            // Apply draft names for any player who hasn't manually submitted yet,
            // then clear the draft so it cannot leak into subsequent outfit rounds.
            foreach (var player in context.GamePlayers.Values)
            {
                if (!player.IsReady)
                {
                    var outfit = player.GetOutfit(_outfitRound);
                    if (outfit is not null && !string.IsNullOrWhiteSpace(player.DraftOutfitName))
                    {
                        outfit.Customization.OutfitName = player.DraftOutfitName.Trim();
                        context.Logger.LogInformation(
                            "Applying draft name \"{name}\" for player [{id}] (timer expired).",
                            outfit.Customization.OutfitName, player.PlayerId);
                    }
                }

                // Clear any draft name at the end of this round to avoid reusing it in later rounds.
                player.DraftOutfitName = string.Empty;
            }

            if (_outfitRound < context.Config.NumOutfitRounds)
            {
                // More outfit rounds to go — check distinctness, then proceed to next round.
                if (_outfitRound == 1 && context.Config.RequireDistinctItemsPerSlot && HasDistinctnessConflict(context))
                {
                    context.Logger.LogInformation(
                        "Distinctness conflict detected. Moving to resolution state.");
                    return new OutfitDistinctnessResolutionState();
                }

                return new PoolRevealState(_outfitRound + 1);
            }

            // Last outfit round — proceed to voting.
            return new VotingRoundSetupState();
        }

        /// <summary>
        /// Returns <see langword="true"/> when any two outfits share the same item ID in
        /// the same clothing-type slot.
        /// </summary>
        private static bool HasDistinctnessConflict(DrawnToDressGameContext context)
        {
            var seenItems = new HashSet<(string typeId, Guid itemId)>();
            foreach (var player in context.GamePlayers.Values)
            {
                if (player.SubmittedOutfit is null) continue;
                foreach (var (typeId, itemId) in player.SubmittedOutfit.SelectedItemsByType)
                {
                    if (!seenItems.Add((typeId, itemId)))
                        return true;
                }
            }
            return false;
        }
    }
}
