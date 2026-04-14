using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class RevealPhase : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;

        private RoundResult? CurrentRoundResult => GameState.RoundResults.LastOrDefault(r => r.RoundNumber == GameState.CurrentRound);

        private int CalculateCorrectGuesses(HiddenAgendaPlayerState player)
        {
            if (player.GuessSubmission is null) return 0;

            int correct = 0;
            foreach (var (targetId, taskIds) in player.GuessSubmission)
            {
                if (!GameState.GamePlayers.TryGetValue(targetId, out var target)) continue;

                foreach (var taskId in taskIds)
                {
                    if (target.SecretTasks.Any(t => t.Id == taskId))
                    {
                        correct++;
                    }
                }
            }
            return correct;
        }
    }
}
