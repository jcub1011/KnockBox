using KnockBox.Core.Components.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public enum InteractionMode
    {
        Draw,
        MoveItems
    }

    public partial class OutfitCustomizationPhase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<OutfitCustomizationPhase> Logger { get; set; } = default!;

        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        [Parameter] public int OutfitRound { get; set; } = 1;

        private SvgDrawingCanvas? _sketchCanvas;
        private string _outfitName = string.Empty;
        private bool _submitting;
        private string? _errorMessage;

        private InteractionMode _mode = InteractionMode.Draw;
        private bool _showMannequin;
        private Dictionary<string, ItemPositionOverride> _itemPositions = new();
        private IJSObjectReference? _dragModule;
        private DotNetObjectReference<OutfitCustomizationPhase>? _dotNetRef;
        private readonly string _dragSvgId = $"drag-layer-{Guid.NewGuid():N}";
        private bool _dragInitialized;
        private CancellationTokenSource? _nameSyncCts;

        private string CurrentPlayerId => UserService.CurrentUser?.Id ?? string.Empty;

        protected override void OnInitialized()
        {
            _showMannequin = GameState.Config.ShowMannequin;
            var myPlayer = GameState.GamePlayers.GetValueOrDefault(CurrentPlayerId);
            _outfitName = myPlayer?.DraftOutfitName ?? string.Empty;

            // Initialize default scaled positions to natively match the mannequin
            foreach (var ct in GameState.Config.ClothingTypes)
            {
                int viewCenterY = ct.CanvasHeight / 2;
                int partCenterY = 440;
                switch (ct.Id)
                {
                    case "hat": partCenterY = 160; break;
                    case "top": partCenterY = 440; break;
                    case "bottom": partCenterY = 820; break;
                    case "shoes": partCenterY = 1070; break;
                }
                
                // Since the item was drawn centered in its respective canvas height,
                // translating it natively here maps it perfectly.
                _itemPositions[ct.Id] = new ItemPositionOverride { X = 50, Y = (partCenterY - viewCenterY) + 200 };
            }
        }

        private async Task OnOutfitNameChangedAsync(string newName)
        {
            _outfitName = newName;

            // Debounce the sync to the server.
            _nameSyncCts?.Cancel();
            _nameSyncCts?.Dispose();
            _nameSyncCts = new CancellationTokenSource();
            try
            {
                await Task.Delay(500, _nameSyncCts.Token);
                UpdateDraftNameOnServer(newName);
            }
            catch (TaskCanceledException) { }
        }

        private void UpdateDraftNameOnServer(string name)
        {
            if (GameState.Context is null) return;
            var cmd = new UpdateDraftOutfitNameCommand(CurrentPlayerId, name);
            GameEngine.ProcessCommand(GameState.Context, cmd);
        }

        /// <summary>
        /// Switches between Draw and MoveItems modes.
        /// </summary>
        protected async Task SwitchModeAsync(InteractionMode newMode)
        {
            if (_mode == newMode) return;
            _mode = newMode;
            _dragInitialized = false;
            StateHasChanged();

            if (newMode == InteractionMode.MoveItems)
            {
                // Allow the drag SVG to render, then initialize the JS module
                await Task.Yield();
                await InitializeDragLayerAsync();
            }
        }

        private async Task InitializeDragLayerAsync()
        {
            if (_dragInitialized) return;

            var myId = UserService.CurrentUser?.Id;
            var myPlayer = myId is not null && GameState.GamePlayers.TryGetValue(myId, out var p) ? p : null;
            var myOutfit = myPlayer?.GetOutfit(OutfitRound);
            if (myOutfit is null) return;

            _dragModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./js/outfitItemDrag.js");
            _dotNetRef ??= DotNetObjectReference.Create(this);

            var items = new List<object>();
            foreach (var ct in GameState.Config.ClothingTypes)
            {
                if (myOutfit.SelectedItemsByType.TryGetValue(ct.Id, out var itemId)
                    && GameState.ClothingPool.TryGetValue(itemId, out var poolItem)
                    && poolItem.SvgContent is not null)
                {
                    _itemPositions.TryGetValue(ct.Id, out var pos);
                    items.Add(new
                    {
                        typeId = ct.Id,
                        svgContent = poolItem.SvgContent,
                        x = pos?.X ?? 0,
                        y = pos?.Y ?? 0,
                        width = ct.CanvasWidth,
                        height = ct.CanvasHeight,
                    });
                }
            }

            int canvasWidth = (GameState.Config.ClothingTypes.FirstOrDefault()?.CanvasWidth ?? 600) + 100;
            int totalHeight = GameState.Config.ClothingTypes.Sum(ct => (int)(ct.CanvasHeight * 0.8));
            await _dragModule.InvokeVoidAsync("initialize", _dragSvgId, _dotNetRef, items, canvasWidth, totalHeight);
            _dragInitialized = true;
        }

        /// <summary>
        /// Called from JS when a drag operation completes.
        /// </summary>
        [JSInvokable]
        public void OnItemMoved(string typeId, double x, double y)
        {
            _itemPositions[typeId] = new ItemPositionOverride { X = x, Y = y };
            InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// Called when a manual X or Y input changes.
        /// </summary>
        protected async Task OnManualPositionChanged(string typeId, bool isX, ChangeEventArgs e)
        {
            if (!double.TryParse(e.Value?.ToString(), out var val)) return;

            if (!_itemPositions.TryGetValue(typeId, out var pos))
            {
                pos = new ItemPositionOverride();
                _itemPositions[typeId] = pos;
            }

            if (isX) pos.X = val;
            else pos.Y = val;

            // Sync to drag layer if it's active
            if (_dragModule is not null && _dragInitialized)
            {
                await _dragModule.InvokeVoidAsync("updateItemPosition", _dragSvgId, typeId, pos.X, pos.Y);
            }

            StateHasChanged();
        }

        /// <summary>
        /// Retrieves the current sketch SVG (if any) and sends a
        /// <see cref="SubmitCustomizationCommand"/> to the engine.
        /// </summary>
        protected async Task SubmitCustomizationAsync()
        {
            if (UserService.CurrentUser is null || GameState.Context is null) return;

            _errorMessage = null;

            if (string.IsNullOrWhiteSpace(_outfitName))
            {
                _errorMessage = "Please enter a name for your outfit.";
                StateHasChanged();
                return;
            }

            _submitting = true;
            StateHasChanged();

            try
            {
                string? sketchSvg = null;
                if (_sketchCanvas is not null)
                {
                    var svg = await _sketchCanvas.GetSvgContentAsync();
                    sketchSvg = string.IsNullOrWhiteSpace(svg) ? null : svg;
                }

                if (GameState.Config.SketchingRequired && sketchSvg is null)
                {
                    _errorMessage = "A sketch overlay is required for this game. Please draw something before finalizing.";
                    return;
                }

                // Always submit position overrides so VotingPhase properly reconstructs the mapped outfit.
                Dictionary<string, ItemPositionOverride>? positionOverrides = new();
                foreach (var ct in GameState.Config.ClothingTypes)
                {
                    if (_itemPositions.TryGetValue(ct.Id, out var pos))
                    {
                        positionOverrides[ct.Id] = new ItemPositionOverride { X = pos.X, Y = pos.Y };
                    }
                }

                var cmd = new SubmitCustomizationCommand(
                    UserService.CurrentUser.Id,
                    _outfitName.Trim(),
                    sketchSvg,
                    positionOverrides);

                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("SubmitCustomization failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error finalizing outfit.");
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Flush any pending debounced outfit name to the server before tearing down.
            _nameSyncCts?.Cancel();
            _nameSyncCts?.Dispose();
            _nameSyncCts = null;
            if (!string.IsNullOrWhiteSpace(_outfitName))
            {
                UpdateDraftNameOnServer(_outfitName);
            }

            if (_dragModule is not null)
            {
                try
                {
                    await _dragModule.InvokeVoidAsync("dispose", _dragSvgId);
                }
                catch (JSDisconnectedException) { }

                try
                {
                    await _dragModule.DisposeAsync();
                }
                catch (JSDisconnectedException) { }
            }
            _dotNetRef?.Dispose();
        }
    }
}
