using System;
using System.Linq;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class MatchOverState : IHiddenAgendaGameState
    {
        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            // Determine winner with tiebreakers:
            // 1. Highest cumulative score
            // 2. Most total correct guesses across all rounds
            // 3. Most total tasks completed across all rounds
            var ranked = context.GamePlayers.Values
                .OrderByDescending(p => p.CumulativeScore)
                .ThenByDescending(p => context.State.RoundResults
                    .Sum(r => r.PlayerResults.GetValueOrDefault(p.PlayerId)?.GuessPoints ?? 0))
                .ThenByDescending(p => context.State.RoundResults
                    .Sum(r => r.PlayerResults.GetValueOrDefault(p.PlayerId)?.TaskResults
                        .Count(t => t.Completed) ?? 0))
                .ToList();

            context.State.MatchWinner = ranked.First().PlayerId;
            context.State.SetPhase(GamePhase.MatchOver);
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(
            HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            switch (command)
            {
                case ReturnToLobbyCommand cmd:
                {
                    if (cmd.PlayerId != context.State.Host.Id)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Only the host can return to lobby.");
                    
                    // Signal to the engine that the match is done by setting phase to Lobby.
                    context.State.SetPhase(GamePhase.Lobby);
                    context.State.UpdateJoinableStatus(false);
                    return null;
                }

                case PlayAgainCommand cmd:
                {
                    if (cmd.PlayerId != context.State.Host.Id)
                        return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Only the host can start a new match.");

                    // Full reset: clear all scores, round results, round counter
                    context.State.CurrentRound = 0;
                    context.State.RoundResults.Clear();
                    context.State.MatchWinner = null;
                    foreach (var player in context.GamePlayers.Values)
                    {
                        player.CumulativeScore = 0;
                    }
                    context.ResetForNewRound();
                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new RoundSetupState());
                }

                default:
                    return null;
            }
        }
    }
}
