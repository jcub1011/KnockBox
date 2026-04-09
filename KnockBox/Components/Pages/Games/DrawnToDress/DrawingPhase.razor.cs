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
    public partial class DrawingPhase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<DrawingPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private SvgDrawingCanvas? _canvas;
        private bool _submitting;
        private string? _errorMessage;
        private bool _showMannequin;

        protected string? MannequinSvg => _showMannequin ? GetMannequinSvg(CurrentTypeId) : null;

        protected void ToggleMannequin()
        {
            _showMannequin = !_showMannequin;
            StateHasChanged();
        }

        protected override void OnInitialized()
        {
            _showMannequin = GameState.Config.ShowMannequin;
        }

        private string GetMannequinSvg(string currentTypeId)
        {
            var ct = GameState.Config.ClothingTypes.FirstOrDefault(c => c.Id == currentTypeId);
            int canvasWidth = ct?.CanvasWidth ?? 400;
            int canvasHeight = ct?.CanvasHeight ?? 400;
            int partCenterY = ct?.MannequinAnchorY ?? 440;
            int yOffset = (canvasHeight / 2) - partCenterY;

            return MannequinSvgHelper.Build(canvasWidth, yOffset, currentTypeId);
        }

        /// <summary>Display name for the clothing type of the current round.</summary>
        protected string CurrentTypeName
        {
            get
            {
                var types = GameState.Config.ClothingTypes;
                int idx = GameState.CurrentDrawingClothingTypeIndex;
                return idx >= 0 && idx < types.Count
                    ? types[idx].DisplayName
                    : "Drawing";
            }
        }

        /// <summary>
        /// The ID of the clothing type for the current round.
        /// </summary>
        private string CurrentTypeId
        {
            get
            {
                var types = GameState.Config.ClothingTypes;
                int idx = GameState.CurrentDrawingClothingTypeIndex;
                return idx >= 0 && idx < types.Count ? types[idx].Id : string.Empty;
            }
        }

        /// <summary>Canvas pixel width for the current clothing type.</summary>
        protected int CurrentTypeCanvasWidth
        {
            get
            {
                var types = GameState.Config.ClothingTypes;
                int idx = GameState.CurrentDrawingClothingTypeIndex;
                return idx >= 0 && idx < types.Count ? types[idx].CanvasWidth : 300;
            }
        }

        /// <summary>Canvas pixel height for the current clothing type.</summary>
        protected int CurrentTypeCanvasHeight
        {
            get
            {
                var types = GameState.Config.ClothingTypes;
                int idx = GameState.CurrentDrawingClothingTypeIndex;
                return idx >= 0 && idx < types.Count ? types[idx].CanvasHeight : 300;
            }
        }

        /// <summary>
        /// Max items per round for the current clothing type (0 = unlimited).
        /// </summary>
        protected int CurrentTypeMaxItems
        {
            get
            {
                var types = GameState.Config.ClothingTypes;
                int idx = GameState.CurrentDrawingClothingTypeIndex;
                return idx >= 0 && idx < types.Count ? types[idx].MaxItemsPerRound : 0;
            }
        }

        /// <summary>
        /// Counts how many items the given player has submitted for the CURRENT clothing type.
        /// </summary>
        protected int CountSubmittedForCurrentType(DrawnToDressPlayerState player)
        {
            var typeId = CurrentTypeId;
            return GameState.ClothingPool.Values
                .Count(item => item.CreatorPlayerId == player.PlayerId
                               && item.ClothingTypeId == typeId);
        }

        /// <summary>
        /// Grabs the current SVG from the canvas, sends it to the engine, and clears the canvas
        /// so the player can optionally draw another item.
        /// </summary>
        protected async Task SubmitDrawingAsync()
        {
            if (_canvas is null || UserService.CurrentUser is null || GameState.Context is null)
                return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                string? svgContent;
                try
                {
                    svgContent = await _canvas.GetSvgContentAsync();
                }
                // TaskCanceledException (the specific exception from the bug report) inherits
                // from OperationCanceledException, so this catch covers both.
                catch (OperationCanceledException)
                {
                    _errorMessage = "Canvas connection timed out — please try again in a moment.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(svgContent))
                {
                    _errorMessage = "Nothing drawn yet — pick up the pen first!";
                    return;
                }

                var cmd = new SubmitDrawingCommand(
                    UserService.CurrentUser.Id,
                    CurrentTypeId,
                    svgContent);

                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("SubmitDrawing failed: {msg}", err.PublicMessage);
                    return;
                }

                // Clear the canvas so the player can draw a new item.
                await _canvas.ClearAsync();

                // Auto-ready when the player has hit the drawing limit.
                int myMax = CurrentTypeMaxItems;
                if (myMax > 0)
                {
                    string? myId = UserService.CurrentUser.Id;
                    var myPlayer = GameState.GamePlayers.TryGetValue(myId, out var p) ? p : null;
                    int newCount = myPlayer is not null ? CountSubmittedForCurrentType(myPlayer) : 0;
                    if (newCount >= myMax)
                    {
                        var readyCmd = new MarkReadyCommand(UserService.CurrentUser.Id);
                        GameEngine.ProcessCommand(GameState.Context, readyCmd);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error submitting drawing.");
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Marks the player as done with this round so the game can advance early when all
        /// players are ready.
        /// </summary>
        protected async Task MarkDoneAsync()
        {
            if (UserService.CurrentUser is null || GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                // Auto-submit any in-progress drawing before marking done.
                // Isolated in its own try/catch so that a canvas retrieval failure
                // (e.g. a transient circuit interruption) does not block the ready signal.
                if (_canvas is not null)
                {
                    try
                    {
                        var svgContent = await _canvas.GetSvgContentAsync();
                        if (!string.IsNullOrWhiteSpace(svgContent))
                        {
                            int myMax = CurrentTypeMaxItems;
                            string? myId = UserService.CurrentUser.Id;
                            var myPlayer = GameState.GamePlayers.TryGetValue(myId, out var p) ? p : null;
                            int mySubmitted = myPlayer is not null ? CountSubmittedForCurrentType(myPlayer) : 0;

                            // Only auto-submit if still under the limit.
                            if (myMax == 0 || mySubmitted < myMax)
                            {
                                var submitCmd = new SubmitDrawingCommand(
                                    myId, CurrentTypeId, svgContent);
                                var submitResult = GameEngine.ProcessCommand(GameState.Context, submitCmd);
                                if (submitResult.TryGetFailure(out var submitErr))
                                    Logger.LogWarning("Auto-submit on done failed: {msg}", submitErr.PublicMessage);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Auto-submit before marking done failed — skipping.");
                    }
                }

                var cmd = new MarkReadyCommand(UserService.CurrentUser.Id);
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("MarkReady failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error marking done.");
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
            if (_canvas is not null)
            {
                try
                {
                    await _canvas.DisposeAsync();
                }
                catch (JSDisconnectedException) { }
            }
        }
    }
}
