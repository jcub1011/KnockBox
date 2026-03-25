using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class PoolRevealPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<PoolRevealPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private bool _submitting;
        private string? _errorMessage;

        /// <summary>
        /// Returns the display name for an item's creator, falling back to a shortened ID.
        /// </summary>
        protected string GetCreatorName(string creatorPlayerId)
        {
            if (GameState.GamePlayers.TryGetValue(creatorPlayerId, out var ps))
                return ps.DisplayName;
            return creatorPlayerId.Length > 8 ? creatorPlayerId[..8] : creatorPlayerId;
        }

        /// <summary>Returns a tooltip title for a pool item.</summary>
        protected string GetItemTitle(DrawnClothingItem item)
            => $"{item.ClothingTypeId} by {GetCreatorName(item.CreatorPlayerId)}";

        /// <summary>
        /// Sends a <see cref="MarkReadyCommand"/> so the phase can advance early once all
        /// players are ready.
        /// </summary>
        protected void MarkReady()
        {
            if (UserService.CurrentUser is null || GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                var cmd = new MarkReadyCommand(UserService.CurrentUser.Id);
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("MarkReady (pool reveal) failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error marking ready during pool reveal.");
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }
    }
}
