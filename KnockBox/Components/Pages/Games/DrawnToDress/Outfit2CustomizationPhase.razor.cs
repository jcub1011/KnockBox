using KnockBox.Core.Components.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class Outfit2CustomizationPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<Outfit2CustomizationPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private SvgDrawingCanvas? _sketchCanvas;
        private string? _outfitName;
        private bool _submitting;
        private string? _errorMessage;

        /// <summary>
        /// Retrieves the current sketch SVG (if any) and sends a
        /// <see cref="SubmitCustomizationCommand"/> to the engine, which routes it to
        /// Outfit 2 customization storage in <see cref="Outfit2CustomizationState"/>.
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

                var cmd = new SubmitCustomizationCommand(
                    UserService.CurrentUser.Id,
                    _outfitName.Trim(),
                    sketchSvg);

                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("Outfit2 SubmitCustomization failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error finalizing Outfit 2.");
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
