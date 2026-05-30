using KnockBox.Core.Components.Shared;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList.Pages
{
    public partial class LinkedListLobby : LobbyPageBase<LinkedListGameState>
    {
        [Inject] protected LinkedListGameEngine GameEngine { get; set; } = default!;

        private string? _errorMessage;
        private int _errorKey;

        // Mirror the CSS .ll-error-toast animation duration in LinkedListLobby.razor.css.
        private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(3);

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            await GameEngine.StartAsync(UserService.CurrentUser, GameState);
        }

        // The per-turn timeout itself fires server-side via ScheduleCallback, so it
        // runs regardless of any connected client. This host tick only nudges a
        // re-render each second so the host's view of banked elapsed / paused state
        // stays fresh; the per-turn countdown bar animates client-side via CountdownClock.
        protected override bool TryGetHostTick(out Action action, out int tickInterval)
        {
            action = () => _ = InvokeAsync(StateHasChanged);
            tickInterval = TickService.TicksPerSecond;
            return true;
        }

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            int capturedKey = _errorKey;
            StateHasChanged();

            // Server-side dismissal because WebKit (iPhone Safari) tears the
            // circuit on `@onanimationend`. Guard against rapid successive
            // errors via the captured key.
            ScheduleClear(ErrorToastDuration, () =>
            {
                if (_errorKey == capturedKey) _errorMessage = null;
            });
        }
    }
}
