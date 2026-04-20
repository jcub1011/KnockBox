using System;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class GuessPhaseState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (currentPlayerId == null || !context.GamePlayers.TryGetValue(currentPlayerId, out var player))
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(AdvanceToNextPlayer(context));

            // If player already guessed, skip
            if (player.HasSubmittedGuess)
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(AdvanceToNextPlayer(context));

            context.State.SetPhase(GamePhase.GuessPhase);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
                context.State.Config.GuessPhaseTimeoutMs);
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(
            HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            var currentPlayerId = context.State.TurnManager.CurrentPlayer;

            switch (command)
            {
                case SubmitGuessCommand cmd:
                {
                    if (cmd.PlayerId != currentPlayerId)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("It is not your turn.");

                    var player = context.GamePlayers[cmd.PlayerId];
                    if (player.HasSubmittedGuess)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("You have already submitted guesses this round.");

                    // Validate guess format
                    var validationError = context.ValidateGuessSubmission(cmd.PlayerId, cmd.Guesses);
                    if (validationError != null)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError(validationError);

                    // Store guesses
                    player.HasSubmittedGuess = true;
                    player.GuessSubmission = cmd.Guesses;

                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(AdvanceToNextPlayer(context));
                }

                case SkipGuessCommand cmd:
                {
                    if (cmd.PlayerId != currentPlayerId)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("It is not your turn.");
                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(AdvanceToNextPlayer(context));
                }

                default:
                    return null;
            }
        }

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            // Auto-skip on timeout
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(AdvanceToNextPlayer(context));
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;

        private static IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>? AdvanceToNextPlayer(HiddenAgendaGameContext context)
        {
            return context.AdvanceToNextPlayerOrEndRound();
        }
    }
}
