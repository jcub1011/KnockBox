using KnockBox.Core.Services.Drawing;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class Outfit2BuildingPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<Outfit2BuildingPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private bool _submitting;
        private string? _errorMessage;

        /// <summary>
        /// Finds which item from the player's owned items is currently "selected" for a
        /// given clothing type.  The selection is the claimed item of that type, or the
        /// player's own drawing of that type when no claimed item exists.
        /// </summary>
        protected Guid? GetSelectedItemForType(DrawnToDressPlayerState player, string clothingTypeId)
        {
            // Prefer a claimed (non-self-drawn) item for this type.
            foreach (var id in player.OwnedClothingItemIds)
            {
                if (!GameState.ClothingPool.TryGetValue(id, out var item)) continue;
                if (item.ClothingTypeId != clothingTypeId) continue;
                if (item.ClaimedByPlayerId == player.PlayerId) return id;
            }

            // Fall back to own drawing for this type.
            foreach (var id in player.OwnedClothingItemIds)
            {
                if (!GameState.ClothingPool.TryGetValue(id, out var item)) continue;
                if (item.ClothingTypeId != clothingTypeId) continue;
                if (item.CreatorPlayerId == player.PlayerId) return id;
            }

            return null;
        }

        /// <summary>Returns the display name for an item's creator, falling back to a shortened ID.</summary>
        protected string GetCreatorName(string creatorPlayerId)
        {
            if (GameState.GamePlayers.TryGetValue(creatorPlayerId, out var ps))
                return ps.DisplayName;
            return creatorPlayerId.Length > 8 ? creatorPlayerId[..8] : creatorPlayerId;
        }

        /// <summary>
        /// Claims an available item or unclaims a currently-claimed item (toggle).
        /// No-ops when the item is another player's drawing or already taken.
        /// </summary>
        protected void ToggleClaim(DrawnClothingItem item, bool claimedByMe, bool isAvailable)
        {
            if (claimedByMe)
                UnclaimItem(item.Id);
            else if (isAvailable)
                ClaimItem(item.Id);
        }

        protected void ClaimItem(Guid itemId)
            => SendCommand(new ClaimPoolItemCommand(CurrentPlayerId, itemId), "claim item");

        protected void UnclaimItem(Guid itemId)
            => SendCommand(new UnclaimPoolItemCommand(CurrentPlayerId, itemId), "unclaim item");

        /// <summary>
        /// Builds the selected-items dictionary from owned items and sends a
        /// <see cref="SubmitOutfitCommand"/> to lock in Outfit 2.
        /// </summary>
        protected async Task SubmitOutfit2Async()
        {
            if (UserService.CurrentUser is null || GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                var myPlayer = GameState.GamePlayers.GetValueOrDefault(CurrentPlayerId);
                if (myPlayer is null)
                {
                    _errorMessage = "Player state not found.";
                    return;
                }

                var selected = BuildSelectedItems(myPlayer);

                var cmd = new SubmitOutfitCommand(CurrentPlayerId, selected);
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("SubmitOutfit2 failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error submitting Outfit 2.");
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string CurrentPlayerId => UserService.CurrentUser?.Id ?? string.Empty;

        /// <summary>
        /// Sanitizes raw SVG inner markup for safe rendering.
        /// Returns <see langword="null"/> when there is no content.
        /// </summary>
        protected static string? SafeSvgContent(string? raw)
            => SvgContentSanitizer.Sanitize(raw);

        private Dictionary<string, Guid> BuildSelectedItems(DrawnToDressPlayerState player)
        {
            var result = new Dictionary<string, Guid>();
            foreach (var clothingType in GameState.Config.ClothingTypes)
            {
                var selected = GetSelectedItemForType(player, clothingType.Id);
                if (selected.HasValue)
                    result[clothingType.Id] = selected.Value;
            }
            return result;
        }

        private void SendCommand(DrawnToDressCommand cmd, string action)
        {
            if (GameState.Context is null) return;

            _errorMessage = null;

            try
            {
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("Outfit 2 building — {action} failed: {msg}", action, err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during {action}.", action);
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                StateHasChanged();
            }
        }
    }
}
