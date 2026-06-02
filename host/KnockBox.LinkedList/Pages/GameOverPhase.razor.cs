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

        /// <summary>Host-only: returns the match to the lobby so players can join/leave
        /// and settings can change before the next game. The engine clears all per-match
        /// state and flips back to joinable, which re-renders every player at the lobby.</summary>
        protected async Task ReturnToLobby()
        {
            if (_starting || UserService.CurrentUser is null) return;
            if (GameState.Host.Id != UserService.CurrentUser.Id) return;

            _starting = true;
            try
            {
                var result = GameEngine.ReturnToLobby(UserService.CurrentUser, GameState);
                if (result.TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to return Linked List to the lobby: {Error}", error.InternalMessage);
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
