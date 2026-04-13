using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class OutfitDistinctnessResolutionPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<OutfitDistinctnessResolutionPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private bool _submitting;
        private string? _errorMessage;

        /// <summary>
        /// Finds clothing types where the current player shares an item with another player.
        /// </summary>
        protected List<(string TypeId, Guid ConflictingItemId)> GetConflictsForCurrentPlayer()
        {
            var myId = UserService.CurrentUser?.Id;
            if (myId is null) return [];

            var myOutfit = GameState.GamePlayers.GetValueOrDefault(myId)?.SubmittedOutfit;
            if (myOutfit is null) return [];

            var conflicts = new List<(string, Guid)>();

            foreach (var (typeId, itemId) in myOutfit.SelectedItemsByType)
            {
                // Check if any OTHER player uses the same item in the same slot.
                foreach (var (otherId, otherState) in GameState.GamePlayers)
                {
                    if (otherId == myId) continue;
                    if (otherState.SubmittedOutfit is null) continue;
                    if (otherState.SubmittedOutfit.SelectedItemsByType.TryGetValue(typeId, out var otherItemId)
                        && otherItemId == itemId)
                    {
                        conflicts.Add((typeId, itemId));
                        break;
                    }
                }
            }

            return conflicts;
        }

        /// <summary>
        /// Gets available replacement items for a given clothing type that the player owns.
        /// </summary>
        protected List<DrawnClothingItem> GetReplacementCandidates(string typeId, Guid conflictingItemId)
        {
            var myId = UserService.CurrentUser?.Id;
            if (myId is null) return [];

            var player = GameState.GamePlayers.GetValueOrDefault(myId);
            if (player is null) return [];

            return player.OwnedClothingItemIds
                .Where(id => id != conflictingItemId
                             && GameState.ClothingPool.TryGetValue(id, out var item)
                             && item.ClothingTypeId == typeId)
                .Select(id => GameState.ClothingPool[id])
                .ToList();
        }

        protected async Task SubmitReplacementAsync(Guid replacementItemId)
        {
            if (UserService.CurrentUser is null || GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                var cmd = new ResolveDistinctnessCommand(
                    UserService.CurrentUser.Id,
                    replacementItemId);

                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("ResolveDistinctness failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error resolving distinctness.");
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }

        protected bool IsPlayerResolved()
        {
            var myId = UserService.CurrentUser?.Id;
            if (myId is null) return false;
            var player = GameState.GamePlayers.GetValueOrDefault(myId);
            return player?.IsReady ?? false;
        }
    }
}

