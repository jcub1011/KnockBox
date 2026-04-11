using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.ConsultTheCard;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States
{
    /// <summary>
    /// Reveal phase. Shows the result of the vote. Applies per-cycle scoring.
    /// If an elimination occurred and the eliminated player is the Informant,
    /// waits for the Informant guess. Otherwise, checks win conditions or
    /// resets for the next cycle. Always auto-advances via Tick (reveal is always timed).
    /// </summary>
    public sealed class RevealPhaseState : ITimedConsultTheCardGameState
    {
        private DateTimeOffset _expiresAt;
        private DateTimeOffset _informantGuessExpiresAt;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> OnEnter(ConsultTheCardGameContext context)
        {
            var elimination = context.State.LastElimination;

            context.State.SetPhase(ConsultTheCardGamePhase.Reveal);

            // Apply per-cycle scoring while vote data is still intact.
            string? eliminatedId = (elimination is not null && !elimination.WasTie) ? elimination.PlayerId : null;
            context.ApplyCycleScoring(eliminatedId);

            if (elimination is not null && !elimination.WasTie)
            {
                // Elimination occurred.
                var eliminated = context.GetPlayer(elimination.PlayerId);
                if (eliminated is not null && eliminated.Role == Role.Informant)
                {
                    // Informant eliminated — wait for guess.
                    context.State.AwaitingInformantGuess = true;
                    _informantGuessExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
                        context.State.Config.InformantGuessTimeoutMs);
                    _expiresAt = _informantGuessExpiresAt;

                    context.Logger.LogInformation(
                        "RevealPhase: Informant [{pid}] eliminated; awaiting guess.", elimination.PlayerId);
                    return null;
                }

                // Non-Informant eliminated — check win conditions.
                var winResult = context.CheckWinConditions();
                if (winResult.GameOver)
                {
                    context.State.WinResult = winResult;
                    return new GameOverState();
                }

                // Game continues — reset for next cycle.

            }
            else
            {
                // Tie — no elimination. Reset for next cycle.

            }

            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.RevealPhaseTimeoutMs);

            context.Logger.LogInformation("FSM → RevealPhaseState");
            return null;
        }

        public Result OnExit(ConsultTheCardGameContext context) => Result.Success;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> HandleCommand(
            ConsultTheCardGameContext context, ConsultTheCardCommand command)
        {
            if (command is not InformantGuessCommand cmd)
                return null;

            if (!context.State.AwaitingInformantGuess)
                return new ResultError("No Informant guess is expected at this time.");

            // Validate sender is the eliminated Informant.
            var elimination = context.State.LastElimination;
            if (elimination is null || cmd.PlayerId != elimination.PlayerId)
                return new ResultError("Only the eliminated Informant may guess.");

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null || player.Role != Role.Informant)
                return new ResultError("Only the Informant may guess.");

            // Check the guess against the Agent word (first word in CurrentWordPair).
            string agentWord = context.State.CurrentWordPair![0];
            bool isCorrect = string.Equals(cmd.GuessedWord, agentWord, StringComparison.OrdinalIgnoreCase);

            context.State.AwaitingInformantGuess = false;
            context.State.LastInformantGuess = new InformantGuessResult(
                player.PlayerId, player.DisplayName, cmd.GuessedWord, isCorrect);

            context.Logger.LogInformation(
                "RevealPhase: Informant [{pid}] guessed [{guess}]; correct={correct}.",
                cmd.PlayerId, cmd.GuessedWord, isCorrect);

            if (isCorrect)
            {
                // Informant wins.
                context.State.WinResult = new WinConditionResult(true, Role.Informant, "Informant correctly guessed the Agent word.");
                return new GameOverState();
            }

            // Wrong guess — check win conditions.
            var winResult = context.CheckWinConditions();
            if (winResult.GameOver)
            {
                context.State.WinResult = winResult;
                return new GameOverState();
            }

            // Game continues — set reveal timeout.
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.RevealPhaseTimeoutMs);

            return null;
        }

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> Tick(
            ConsultTheCardGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (context.State.AwaitingInformantGuess)
            {
                // Informant timed out — treat as forfeited (same flow as wrong guess).
                context.State.AwaitingInformantGuess = false;

                var elimination = context.State.LastElimination;
                if (elimination is not null)
                {
                    context.State.LastInformantGuess = new InformantGuessResult(
                        elimination.PlayerId, elimination.PlayerName, string.Empty, WasCorrect: false);
                }

                context.Logger.LogInformation("RevealPhase: Informant guess timed out; forfeited.");

                var winResult = context.CheckWinConditions();
                if (winResult.GameOver)
                {
                    context.State.WinResult = winResult;
                    return new GameOverState();
                }

                _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.RevealPhaseTimeoutMs);
                return null;
            }

            // Auto-advance to next clue phase.
            return new CluePhaseState();
        }

        public ValueResult<TimeSpan> GetRemainingTime(ConsultTheCardGameContext context, DateTimeOffset now)
            => _expiresAt - now;
    }
}
