using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
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
        [Inject] private ILogger<SvgDrawingCanvas> Logger { get; set; } = default!;

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
                try
                {
                    _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "/_content/KnockBox.Core/js/svgDrawingCanvas.js");
                    await _jsModule.InvokeVoidAsync(
                        "initialize", _svgId, _dotNetRef, _currentColor, _currentStrokeWidth, BackgroundColor);
                    Logger.LogInformation("[SVGCanvas] Initialized — svgId={SvgId}", _svgId);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] Failed to initialize JS module — svgId={SvgId}", _svgId);
                }
            }
        }

        /// <summary>Called from JavaScript whenever a stroke is completed or undone.</summary>
        [JSInvokable]
        public void OnStrokeCompleted(int strokeCount)
        {
            _strokeCount = strokeCount;
        }

        /// <summary>Called from JavaScript when the stroke color changes.</summary>
        [JSInvokable]
        public void OnColorChanged(string color)
        {
            _currentColor = color;
        }

        /// <summary>Called from JavaScript when the stroke width changes.</summary>
        [JSInvokable]
        public void OnStrokeWidthChanged(double width)
        {
            _currentStrokeWidth = width;
        }

        /// <summary>Removes the most recently drawn stroke.</summary>
        public async Task UndoAsync()
        {
            if (_jsModule is null)
            {
                Logger.LogWarning("[SVGCanvas] UndoAsync: JS module not initialized — svgId={SvgId}", _svgId);
                return;
            }
            try
            {
                _strokeCount = await _jsModule.InvokeAsync<int>("undo", _svgId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGCanvas] UndoAsync failed — svgId={SvgId}", _svgId);
            }
        }

        /// <summary>Removes all strokes from the canvas.</summary>
        public async Task ClearAsync()
        {
            if (_jsModule is null)
            {
                Logger.LogWarning("[SVGCanvas] ClearAsync: JS module not initialized — svgId={SvgId}", _svgId);
                return;
            }
            try
            {
                await _jsModule.InvokeVoidAsync("clear", _svgId);
                _strokeCount = 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGCanvas] ClearAsync failed — svgId={SvgId}", _svgId);
            }
        }

        /// <summary>Downloads the current drawing as an SVG file.</summary>
        public async Task ExportSvgAsync()
        {
            if (_jsModule is null)
            {
                Logger.LogWarning("[SVGCanvas] ExportSvgAsync: JS module not initialized — svgId={SvgId}", _svgId);
                return;
            }
            try
            {
                var fileName = $"drawing-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.svg";
                await _jsModule.InvokeVoidAsync("downloadSvg", _svgId, fileName, BackgroundColor);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGCanvas] ExportSvgAsync failed — svgId={SvgId}", _svgId);
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
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[SVGCanvas] DisposeAsync: error during JS dispose — svgId={SvgId}", _svgId);
                }
            }
            _dotNetRef?.Dispose();
        }
    }
}
