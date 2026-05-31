using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList.Pages
{
    public partial class RoundOverPhase : ComponentBase
    {
        [Inject] protected LinkedListGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<RoundOverPhase> Logger { get; set; } = default!;

        [Parameter] public LinkedListGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private bool _starting;

        /// <summary>Formats banked thinking time as <c>m:ss</c> (or <c>h:mm:ss</c>).</summary>
        protected static string FormatElapsed(TimeSpan elapsed)
            => elapsed >= TimeSpan.FromHours(1)
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

        /// <summary>True when the round was played as competing groups (§8.2).</summary>
        protected bool IsGroups => GameState.Settings.PlayerStructure == PlayerStructure.Groups;

        /// <summary>Per-group standings for the round, ranked with cross-metric tie-break.</summary>
        protected IReadOnlyList<GroupStanding> Standings => GameState.LastStandings;

        /// <summary>True when the primary scoring metric is time (so guesses break ties).</summary>
        protected bool PrimaryIsTime => GameState.Settings.ScoringMode == ScoringMode.FastestTime;

        protected static string RankMedal(int rank) => rank switch
        {
            1 => "🥇",
            2 => "🥈",
            3 => "🥉",
            _ => $"#{rank}",
        };

        /// <summary>The chain for a given standing's group (for the per-group chain list).</summary>
        protected ChainState? GroupOf(string groupId) => GameState.GroupById(groupId);

        /// <summary>True once the match has played its full complement of rounds — the
        /// host can only proceed to the Results screen (§10 auto-end).</summary>
        protected bool IsLastRound => GameState.RoundNumber >= GameState.Settings.RoundsPerMatch;

        /// <summary>Display name of the Auditor who'll run the next round (§6).</summary>
        protected string NextAuditorName
        {
            get
            {
                var id = LinkedListGameEngine.NextAuditorId(GameState);
                return GameState.GamePlayers.TryGetValue(id, out var ps) ? ps.DisplayName : "—";
            }
        }

        /// <summary>Host-only: rotates the Auditor and starts the next round (§6/§10).</summary>
        protected void NextRound()
        {
            if (_starting || UserService.CurrentUser is null) return;
            if (GameState.Host.Id != UserService.CurrentUser.Id) return;

            _starting = true;
            try
            {
                var result = GameEngine.RotateAuditorAndStartRound(GameState);
                if (result.TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to start the next Linked List round: {Error}", error.InternalMessage);
                    _ = OnError.InvokeAsync(error.PublicMessage);
                }
            }
            finally
            {
                _starting = false;
            }
        }

        /// <summary>Host-only: ends the match and shows the Results screen (§10).</summary>
        protected void EndMatch()
        {
            if (_starting || UserService.CurrentUser is null) return;
            if (GameState.Host.Id != UserService.CurrentUser.Id) return;

            _starting = true;
            try
            {
                var result = GameEngine.EndMatch(GameState);
                if (result.TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to end the Linked List match: {Error}", error.InternalMessage);
                    _ = OnError.InvokeAsync(error.PublicMessage);
                }
            }
            finally
            {
                _starting = false;
            }
        }
    }
}
