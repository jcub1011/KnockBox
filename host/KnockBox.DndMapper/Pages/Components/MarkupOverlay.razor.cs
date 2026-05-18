using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class MarkupOverlay : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter, EditorRequired] public Map Map { get; set; } = default!;
        [Parameter] public bool IsActive { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;

        private SvgDrawingCanvas? _canvas;

        private async Task OnStrokeCompleted(int strokeCount)
        {
            if (_canvas is null) return;
            var user = UserService.CurrentUser;
            if (user is null) return;

            // GetSvgContentAsync streams the serialized inner SVG (chunked to stay
            // under SignalR's 32KB receive limit). Strokes come back in pixel-space
            // coordinates because the canvas's viewBox is sized in pixels
            // (cells × CellPixels) — see the .razor for why.
            //
            // Wrap in `scale(1 / CellPixels)` so the persisted markup is in the
            // map's cell coordinate system; this lets MapCanvas drop the saved
            // markup directly into its cell-unit viewBox without further
            // transformation, and decouples the on-wire format from any later
            // CellPixels changes. The g + transform pair are both in the
            // SvgContentSanitizer allowlist.
            var svg = await _canvas.GetSvgContentAsync();
            if (string.IsNullOrWhiteSpace(svg))
            {
                Engine.UpdateMapMarkupAsync(State, user, Map.Id, null);
                return;
            }

            var inv = 1.0 / Map.Grid.CellPixels;
            // Invariant culture so e.g. "0.02" doesn't become "0,02" under locales
            // where comma is the decimal separator — SVG requires `.`.
            var scaleStr = inv.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            var wrapped = $"<g transform=\"scale({scaleStr})\">{svg}</g>";
            Engine.UpdateMapMarkupAsync(State, user, Map.Id, wrapped);
        }
    }
}
