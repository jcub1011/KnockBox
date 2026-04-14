using System;
using System.Linq;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class FinalGuessState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            // Check if anyone still needs to guess
            bool anyoneNeedsToGuess = context.GamePlayers.Values
                .Any(p => !p.HasSubmittedGuess);

            if (!anyoneNeedsToGuess)
            {
                // Everyone already guessed, skip straight to Reveal
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new RevealState());
            }

            context.State.SetPhase(GamePhase.FinalGuess);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
                context.State.Config.FinalGuessTimeoutMs);
            context.State.PhaseEndTime = _expiresAt;
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(
            HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            switch (command)
            {
                case SubmitFinalGuessCommand cmd:
                {
                    if (!context.GamePlayers.TryGetValue(cmd.PlayerId, out var player))
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Player not found.");
                    
                    if (player.HasSubmittedGuess)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("You have already submitted guesses.");

                    // Validate guess format
                    var error = context.ValidateGuessSubmission(cmd.PlayerId, cmd.Guesses);
                    if (error != null)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError(error);

                    player.HasSubmittedGuess = true;
                    player.GuessSubmission = cmd.Guesses;

                    // Check if all players have now submitted
                    if (context.GamePlayers.Values.All(p => p.HasSubmittedGuess))
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new RevealState());

                    return null; // Stay in FinalGuess, waiting for others
                }

                case SkipFinalGuessCommand cmd:
                {
                    if (!context.GamePlayers.TryGetValue(cmd.PlayerId, out var player))
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Player not found.");

                    // Mark as "skipped" so we don't wait for them
                    player.HasSubmittedGuess = true;
                    // GuessSubmission stays null -> 0 guess points

                    if (context.GamePlayers.Values.All(p => p.HasSubmittedGuess))
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new RevealState());

                    return null;
                }

                default:
                    return null;
            }
        }

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            // Timeout: mark all non-guessing players as skipped
            foreach (var player in context.GamePlayers.Values)
            {
                if (!player.HasSubmittedGuess)
                    player.HasSubmittedGuess = true; // No guess submission = 0 points
            }

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new RevealState());
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;
    }
}
