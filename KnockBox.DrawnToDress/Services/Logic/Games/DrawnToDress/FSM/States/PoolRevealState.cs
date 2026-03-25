using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Brief display state that reveals the communal clothing pool to all players before
    /// outfit building begins. The pool is already populated by the end of the drawing round;
    /// this state exists so the UI can animate / present the items before advancing.
    ///
    /// When <see cref="ThemeAnnouncement.AfterDrawing"/> is configured the theme is also
    /// revealed here (i.e. <see cref="DrawnToDressGameState.ThemeRevealedToPlayers"/> is set
    /// to <see langword="true"/>).
    ///
    /// Transition ownership:
    /// - <see cref="OnEnter"/> immediately chains to <see cref="OutfitBuildingState"/>.
    ///   (Later issues may introduce a configurable reveal timer here.)
    /// </summary>
    public sealed class PoolRevealState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.PoolReveal);
            context.Logger.LogInformation(
                "FSM → PoolRevealState. Pool contains {count} item(s).",
                context.ClothingPool.Count);

            // In AfterDrawing mode the theme is revealed now that drawing is complete.
            if (context.Config.ThemeAnnouncement == ThemeAnnouncement.AfterDrawing &&
                !context.State.ThemeRevealedToPlayers)
            {
                context.State.ThemeRevealedToPlayers = true;
                context.Logger.LogInformation(
                    "AfterDrawing: theme revealed: [{id}] \"{name}\".",
                    context.State.CurrentTheme?.Id, context.State.CurrentTheme?.DisplayName);
            }

            // Chain immediately to outfit building.
            // TODO: Add a configurable reveal-pause timer in a later issue.
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(new OutfitBuildingState());
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command) => null;
    }
}
