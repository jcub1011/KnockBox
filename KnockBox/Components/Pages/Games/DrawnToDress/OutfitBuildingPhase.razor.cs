using KnockBox.Core.Services.Drawing;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class OutfitBuildingPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<OutfitBuildingPhase> Logger { get; set; } = default!;

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

        /// <summary>Returns a tooltip title for a pool item.</summary>
        protected string GetItemTitle(DrawnClothingItem item)
            => $"{item.ClothingTypeId} by {GetCreatorName(item.CreatorPlayerId)}";

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

        /// <summary>Sends a <see cref="ClaimPoolItemCommand"/> for the given item.</summary>
        protected void ClaimItem(Guid itemId)
            => SendCommand(new ClaimPoolItemCommand(CurrentPlayerId, itemId), "claim item");

        /// <summary>Sends an <see cref="UnclaimPoolItemCommand"/> for the given item.</summary>
        protected void UnclaimItem(Guid itemId)
            => SendCommand(new UnclaimPoolItemCommand(CurrentPlayerId, itemId), "unclaim item");

        /// <summary>
        /// Builds the selected-items dictionary from the player's owned items and sends
        /// a <see cref="SubmitOutfitCommand"/> to lock in the outfit.
        /// </summary>
        protected async Task SubmitOutfitAsync()
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
                    Logger.LogWarning("SubmitOutfit failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error submitting outfit.");
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
        /// Sanitizes raw SVG inner markup for safe rendering via
        /// <see cref="Microsoft.AspNetCore.Components.MarkupString"/>. Returns
        /// <see langword="null"/> when there is no content or the content is unparseable.
        /// </summary>
        protected static string? SafeSvgContent(string? raw)
            => SvgContentSanitizer.Sanitize(raw);

        /// <summary>
        /// Builds a dictionary mapping each clothing type to the best available item
        /// the player owns for that type.  For each type the selection prefers claimed
        /// items over self-drawn fallbacks.
        /// </summary>
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
                    Logger.LogWarning("Outfit building — {action} failed: {msg}", action, err.PublicMessage);
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
