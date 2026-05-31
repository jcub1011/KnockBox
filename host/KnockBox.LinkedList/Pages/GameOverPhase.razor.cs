using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList.Pages
{
    public partial class GameOverPhase : ComponentBase
    {
        [Inject] protected LinkedListGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<GameOverPhase> Logger { get; set; } = default!;

        [Parameter] public LinkedListGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private bool _starting;

        protected bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        /// <summary>Players ranked for the scoreboard. Collective is a co-op result, so
        /// the ranking is by accepted pairs (most helpful contributor first), with
        /// fewest rejections breaking ties.</summary>
        protected IReadOnlyList<LinkedListPlayerState> RankedPlayers =>
            [.. GameState.GamePlayers.Values
                .OrderByDescending(p => p.AcceptedPairs)
                .ThenBy(p => p.RejectionsReceived)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)];

        protected static string FormatElapsed(TimeSpan elapsed)
            => elapsed >= TimeSpan.FromHours(1)
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

        protected static string RankMedal(int rank) => rank switch
        {
            1 => "🥇",
            2 => "🥈",
            3 => "🥉",
            _ => $"#{rank}",
        };

        /// <summary>True when the match was played as competing groups (§8.2).</summary>
        protected bool IsGroups => GameState.Settings.PlayerStructure == PlayerStructure.Groups;

        /// <summary>Final per-group standings (the last round's result).</summary>
        protected IReadOnlyList<GroupStanding> Standings => GameState.LastStandings;

        /// <summary>True when the primary scoring metric is time (so guesses break ties).</summary>
        protected bool PrimaryIsTime => GameState.Settings.ScoringMode == ScoringMode.FastestTime;

        /// <summary>Host-only: starts a brand-new match with the same players. Clears the
        /// round words so a fresh curated pair is drawn, then re-runs the engine start
        /// logic (which resets accumulators, the chain, scoring, and round counter).</summary>
        protected async Task PlayAgain()
        {
            if (_starting || UserService.CurrentUser is null) return;
            if (GameState.Host.Id != UserService.CurrentUser.Id) return;

            _starting = true;
            try
            {
                GameState.Execute(() =>
                {
                    GameState.StartWord = "";
                    GameState.DestinationWord = "";
                });

                var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
                if (result.TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to start a new Linked List match: {Error}", error.InternalMessage);
                    await OnError.InvokeAsync(error.PublicMessage);
                }
            }
            finally
            {
                _starting = false;
            }
        }
    }
}
