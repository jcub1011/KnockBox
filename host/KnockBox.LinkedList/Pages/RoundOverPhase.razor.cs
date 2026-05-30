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

        /// <summary>
        /// Host-only: begins a fresh round. Clears the start/destination words so a
        /// new curated pair is drawn, then re-runs the engine's start logic (which
        /// resets the chain, scoring, and turn order). Full match flow — scoreboards,
        /// auditor rotation, match end — arrives in Milestone 4.
        /// </summary>
        protected async Task StartNewRound()
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
                    Logger.LogError("Failed to start a new Linked List round: {Error}", error.InternalMessage);
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
