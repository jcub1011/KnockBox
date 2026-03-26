using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which players add a custom name and optional sketch overlay to
    /// their assembled Outfit 2.
    ///
    /// Transition ownership:
    /// - Timer expiry → <see cref="VotingRoundSetupState"/>
    /// - All players submit customization early → <see cref="VotingRoundSetupState"/>
    /// - <see cref="SubmitCustomizationCommand"/> → recorded in Outfit 2; may trigger early advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class Outfit2CustomizationState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitCustomizationTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.Outfit2Customization);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → Outfit2CustomizationState. Deadline: {deadline}.", _deadline);
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

            context.Logger.LogInformation("Outfit 2 customization timer expired.");
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                new VotingRoundSetupState());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitCustomization(
            DrawnToDressGameContext context, SubmitCustomizationCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "Outfit2 SubmitCustomization: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (player.SubmittedOutfit2 is null)
            {
                context.Logger.LogWarning(
                    "Outfit2 SubmitCustomization: player [{id}] has no submitted Outfit 2.", cmd.PlayerId);
                return null;
            }

            if (string.IsNullOrWhiteSpace(cmd.OutfitName))
            {
                context.Logger.LogWarning(
                    "Outfit2 SubmitCustomization: player [{id}] submitted with no outfit name.", cmd.PlayerId);
                return null;
            }

            if (context.Config.SketchingRequired && string.IsNullOrWhiteSpace(cmd.SketchSvgContent))
            {
                context.Logger.LogWarning(
                    "Outfit2 SubmitCustomization: player [{id}] submitted without a required sketch.", cmd.PlayerId);
                return null;
            }

            player.SubmittedOutfit2.Customization.OutfitName = cmd.OutfitName.Trim();
            player.SubmittedOutfit2.Customization.SketchSvgContent = string.IsNullOrWhiteSpace(cmd.SketchSvgContent)
                ? null
                : cmd.SketchSvgContent;
            player.IsReady = true;

            context.Logger.LogInformation(
                "Player [{id}] submitted Outfit 2 customization. Outfit name: \"{name}\". Has sketch: {hasSketch}.",
                cmd.PlayerId, cmd.OutfitName, cmd.SketchSvgContent is not null);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players submitted Outfit 2 customization. Moving to voting.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                    new VotingRoundSetupState());
            }

            return null;
        }
    }
}
