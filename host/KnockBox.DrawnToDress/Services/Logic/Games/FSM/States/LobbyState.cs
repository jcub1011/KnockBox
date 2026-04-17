using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.DrawnToDress.Services.State.Games;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Initial FSM state. Players join and configure the session while the host
    /// waits in the lobby.
    ///
    /// Transition ownership:
    /// - <see cref="StartGameCommand"/> (host only) → <see cref="ThemeSelectionState"/>
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// </summary>
    public sealed class LobbyState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Lobby);

            // Reset game-specific state when re-entering the lobby (Play Again).
            // Config and registered Players are preserved; all per-game data is cleared.
            if (context.State.GamePlayers.Count > 0 || context.State.VotingRounds.Count > 0)
            {
                context.Logger.LogDebug("FSM → LobbyState (Play Again). Resetting game state.");
                ResetGameState(context);
            }
            else
            {
                context.Logger.LogDebug("FSM → LobbyState");
            }

            return null;
        }

        private static void ResetGameState(DrawnToDressGameContext context)
        {
            var state = context.State;

            // Allow new players to join.
            state.UpdateJoinableStatus(true);

            // Clear per-game player data (players stay registered via state.Players).
            state.GamePlayers.Clear();

            // Clear timer.
            state.PhaseDeadlineUtc = null;

            // Clear theme state.
            state.CurrentTheme = null;
            state.ThemeRevealedToPlayers = false;
            state.PlayerThemeSubmissions.Clear();
            state.ThemeCandidates.Clear();
            state.ThemeVotes.Clear();

            // Clear drawing state.
            state.CurrentDrawingClothingTypeIndex = 0;
            state.ClothingPool.Clear();

            // Clear voting state.
            state.VotingRounds.Clear();
            state.CurrentVotingRoundIndex = 0;
            state.Votes.Clear();
            state.PendingCoinFlipMatchupId = null;
            state.CriterionCoinFlipResults.Clear();
            state.PendingCoinFlips.Clear();
            state.PendingCoinFlipQueue.Clear();
            state.CurrentCoinFlipIndex = 0;
            state.Leaderboard.Clear();

            // Reset ready flags.
            context.ResetReadyFlags();
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case StartGameCommand cmd:
                    // Only the host can start the game.
                    if (cmd.PlayerId != context.State.Host.Id)
                    {
                        context.Logger.LogWarning(
                            "StartGame rejected: player [{id}] is not the host.", cmd.PlayerId);
                        return null;
                    }
                    context.Logger.LogDebug("Host [{id}] started the game.", cmd.PlayerId);
                    return new ThemeSelectionState();

                case UpdateConfigCommand cmd:
                    // Only the host can change configuration.
                    if (cmd.PlayerId != context.State.Host.Id)
                    {
                        context.Logger.LogWarning(
                            "UpdateConfig rejected: player [{id}] is not the host.", cmd.PlayerId);
                        return null;
                    }
                    cmd.Config.Normalize();
                    context.State.Config = cmd.Config;
                    context.State.StateChangedEventManager.Notify();
                    context.Logger.LogDebug("Host [{id}] updated config.", cmd.PlayerId);
                    return null;

                case PauseGameCommand:
                    return new PausedState(this);

                default:
                    context.Logger.LogWarning(
                        "LobbyState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }
    }
}
