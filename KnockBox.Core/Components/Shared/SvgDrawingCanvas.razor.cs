using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.Core.Components.Shared
{
    /// <summary>
    /// A resizable, interactive SVG drawing canvas supporting mouse and touch input.
    /// Provides configurable stroke color and width, undo support, and SVG export.
    /// </summary>
    public partial class SvgDrawingCanvas : ComponentBase, IAsyncDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        /// <summary>CSS width of the canvas container (e.g. "100%", "800px").</summary>
        [Parameter] public string Width { get; set; } = "100%";

        /// <summary>CSS height of the canvas container (e.g. "400px", "60vh").</summary>
        [Parameter] public string Height { get; set; } = "400px";

        /// <summary>CSS background color of the drawing surface.</summary>
        [Parameter] public string BackgroundColor { get; set; } = "#ffffff";

        /// <summary>Initial stroke color (hex or CSS color string).</summary>
        [Parameter] public string StrokeColor { get; set; } = "#000000";

        /// <summary>Initial stroke width in pixels.</summary>
        [Parameter] public double StrokeWidth { get; set; } = 3;

        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<SvgDrawingCanvas>? _dotNetRef;

        private readonly string _svgId = $"svg-canvas-{Guid.NewGuid():N}";
        private string _colorInputId => $"color-{_svgId}";
        private string _sizeInputId => $"size-{_svgId}";

        private string _currentColor = "#000000";
        private double _currentStrokeWidth = 3;
        private int _strokeCount;

        // Preset color palette shown as swatches in the toolbar.
        private static readonly string[] _colorSwatches =
        [
            "#000000", "#ffffff", "#ef4444", "#f97316",
            "#eab308", "#22c55e", "#3b82f6", "#8b5cf6",
            "#ec4899", "#6b7280", "#92400e", "#164e63",
        ];

        protected override void OnInitialized()
        {
            _currentColor = StrokeColor;
            _currentStrokeWidth = StrokeWidth;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                _dotNetRef = DotNetObjectReference.Create(this);
                _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "/_content/KnockBox.Core/js/svgDrawingCanvas.js");
                await _jsModule.InvokeVoidAsync(
                    "initialize", _svgId, _dotNetRef, _currentColor, _currentStrokeWidth);
            }
        }

        /// <summary>
        /// Called from JavaScript whenever a stroke is completed.
        /// Updates the undo button enabled state.
        /// </summary>
        [JSInvokable]
        public void OnStrokeCompleted(int strokeCount)
        {
            _strokeCount = strokeCount;
            InvokeAsync(StateHasChanged);
        }

        private async Task OnColorChangedAsync(ChangeEventArgs e)
        {
            _currentColor = e.Value?.ToString() ?? _currentColor;
            if (_jsModule is not null)
                await _jsModule.InvokeVoidAsync("setColor", _svgId, _currentColor);
        }

        private async Task OnSwatchClickedAsync(string color)
        {
            _currentColor = color;
            if (_jsModule is not null)
                await _jsModule.InvokeVoidAsync("setColor", _svgId, _currentColor);
        }

        private async Task OnStrokeWidthChangedAsync(ChangeEventArgs e)
        {
            if (double.TryParse(e.Value?.ToString(), out var width))
            {
                _currentStrokeWidth = width;
                if (_jsModule is not null)
                    await _jsModule.InvokeVoidAsync("setStrokeWidth", _svgId, _currentStrokeWidth);
                StateHasChanged();
            }
        }

        /// <summary>Removes the most recently drawn stroke.</summary>
        public async Task UndoAsync()
        {
            if (_jsModule is not null)
            {
                _strokeCount = await _jsModule.InvokeAsync<int>("undo", _svgId);
                StateHasChanged();
            }
        }

        /// <summary>Removes all strokes from the canvas.</summary>
        public async Task ClearAsync()
        {
            if (_jsModule is not null)
            {
                await _jsModule.InvokeVoidAsync("clear", _svgId);
                _strokeCount = 0;
                StateHasChanged();
            }
        }

        /// <summary>Downloads the current drawing as an SVG file.</summary>
        public async Task ExportSvgAsync()
        {
            if (_jsModule is not null)
            {
                var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                await _jsModule.InvokeVoidAsync(
                    "downloadSvg", _svgId, $"drawing-{timestamp}.svg", BackgroundColor);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("dispose", _svgId);
                    await _jsModule.DisposeAsync();
                }
                catch
                {
                    // Suppress disposal errors (e.g. when the JS runtime is already gone)
                }
            }
            _dotNetRef?.Dispose();
        }
    }
}
