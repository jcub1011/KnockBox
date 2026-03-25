using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Initial FSM state. Players join and configure the session while the host
    /// waits in the lobby.
    ///
    /// Transition ownership:
    /// - <see cref="StartGameCommand"/> (host only) → <see cref="ThemeSelectionState"/>
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class LobbyState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Lobby);
            context.Logger.LogInformation("FSM → LobbyState");
            return null;
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
                    context.Logger.LogInformation("Host [{id}] started the game.", cmd.PlayerId);
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
                    context.Logger.LogInformation("Host [{id}] updated config.", cmd.PlayerId);
                    return null;

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }
    }
}
