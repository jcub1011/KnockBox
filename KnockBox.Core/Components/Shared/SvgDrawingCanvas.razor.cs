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
            Logger.LogInformation("[SVGCanvas] OnInitialized — svgId={SvgId}, StrokeColor={StrokeColor}, StrokeWidth={StrokeWidth}",
                _svgId, StrokeColor, StrokeWidth);
            _currentColor = StrokeColor;
            _currentStrokeWidth = StrokeWidth;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                Logger.LogInformation("[SVGCanvas] OnAfterRenderAsync (firstRender) — importing JS module for svgId={SvgId}", _svgId);
                _dotNetRef = DotNetObjectReference.Create(this);
                try
                {
                    _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "/_content/KnockBox.Core/js/svgDrawingCanvas.js");
                    Logger.LogInformation("[SVGCanvas] JS module imported successfully. Calling initialize for svgId={SvgId}, color={Color}, strokeWidth={Width}",
                        _svgId, _currentColor, _currentStrokeWidth);
                    await _jsModule.InvokeVoidAsync(
                        "initialize", _svgId, _dotNetRef, _currentColor, _currentStrokeWidth, BackgroundColor);
                    Logger.LogInformation("[SVGCanvas] initialize JS call completed for svgId={SvgId}", _svgId);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] Failed to import or initialize JS module for svgId={SvgId}. JS-dependent features (drawing, undo, export) will not work.", _svgId);
                    try
                    {
                        await JSRuntime.InvokeVoidAsync("console.error",
                            $"[SVGCanvas] Failed to load JS module for svgId={_svgId}. Drawing, undo, and export will not work. Error: {ex.Message}");
                    }
                    catch { /* ignore secondary failure */ }
                }
            }
        }

        /// <summary>
        /// Called from JavaScript whenever a stroke is completed.
        /// Updates internal stroke count (no re-render needed — undo button state is managed in JS).
        /// </summary>
        [JSInvokable]
        public void OnStrokeCompleted(int strokeCount)
        {
            Logger.LogInformation("[SVGCanvas] OnStrokeCompleted received — svgId={SvgId}, strokeCount={StrokeCount}", _svgId, strokeCount);
            _strokeCount = strokeCount;
            // JS manages the undo button disabled state directly via setUndoDisabled();
            // we do NOT call StateHasChanged here to avoid Blazor overwriting JS DOM changes.
        }

        private async Task OnSwatchClickedAsync(string color)
        {
            Logger.LogInformation("[SVGCanvas] OnSwatchClickedAsync — svgId={SvgId}, color={Color}", _svgId, color);
            _currentColor = color;
            if (_jsModule is not null)
            {
                try
                {
                    Logger.LogInformation("[SVGCanvas] Calling JS setColor — svgId={SvgId}, color={Color}", _svgId, color);
                    await _jsModule.InvokeVoidAsync("setColor", _svgId, _currentColor);
                    Logger.LogInformation("[SVGCanvas] JS setColor completed — svgId={SvgId}", _svgId);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] OnSwatchClickedAsync: JS setColor threw an exception — svgId={SvgId}", _svgId);
                }
            }
            else
            {
                Logger.LogWarning("[SVGCanvas] OnSwatchClickedAsync: _jsModule is null, cannot call setColor.");
            }
        }

        private async Task OnColorInputAsync(ChangeEventArgs e)
        {
            var color = e.Value?.ToString();
            if (string.IsNullOrEmpty(color))
            {
                Logger.LogWarning("[SVGCanvas] OnColorInputAsync (Blazor): received empty color value, ignoring.");
                return;
            }
            Logger.LogInformation("[SVGCanvas] OnColorInputAsync (Blazor) — svgId={SvgId}, color={Color}", _svgId, color);
            _currentColor = color;
            if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("setColor", _svgId, _currentColor);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] OnColorInputAsync: JS setColor threw an exception — svgId={SvgId}", _svgId);
                }
            }
            else
            {
                Logger.LogWarning("[SVGCanvas] OnColorInputAsync: _jsModule is null — color updated in C# state only, canvas stroke color unchanged.");
            }
        }

        private async Task OnSizeInputAsync(ChangeEventArgs e)
        {
            if (!double.TryParse(e.Value?.ToString(), out var width))
            {
                Logger.LogWarning("[SVGCanvas] OnSizeInputAsync (Blazor): could not parse stroke width from value '{Value}', ignoring.", e.Value);
                return;
            }
            Logger.LogInformation("[SVGCanvas] OnSizeInputAsync (Blazor) — svgId={SvgId}, width={Width}", _svgId, width);
            _currentStrokeWidth = width;
            if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("setStrokeWidth", _svgId, _currentStrokeWidth);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] OnSizeInputAsync: JS setStrokeWidth threw an exception — svgId={SvgId}", _svgId);
                }
            }
            else
            {
                Logger.LogWarning("[SVGCanvas] OnSizeInputAsync: _jsModule is null — stroke width updated in C# state only, canvas stroke width unchanged.");
            }
        }

        /// <summary>Called from JavaScript when the custom color picker value changes.</summary>
        [JSInvokable]
        public void OnColorChanged(string color)
        {
            Logger.LogInformation("[SVGCanvas] OnColorChanged received from JS — svgId={SvgId}, color={Color}", _svgId, color);
            _currentColor = color;
            // JS updates the active swatch class directly; no re-render needed.
        }

        /// <summary>Called from JavaScript when the stroke-width slider moves.</summary>
        [JSInvokable]
        public void OnStrokeWidthChanged(double width)
        {
            Logger.LogInformation("[SVGCanvas] OnStrokeWidthChanged received from JS — svgId={SvgId}, width={Width}", _svgId, width);
            _currentStrokeWidth = width;
            // JS updates the size label text directly; no re-render needed.
        }

        /// <summary>Removes the most recently drawn stroke.</summary>
        public async Task UndoAsync()
        {
            Logger.LogInformation("[SVGCanvas] UndoAsync — svgId={SvgId}, currentStrokeCount={StrokeCount}", _svgId, _strokeCount);
            if (_jsModule is not null)
            {
                try
                {
                    _strokeCount = await _jsModule.InvokeAsync<int>("undo", _svgId);
                    Logger.LogInformation("[SVGCanvas] UndoAsync: JS undo returned strokeCount={StrokeCount}", _strokeCount);
                    // JS manages the undo button disabled state via setUndoDisabled(); no re-render needed.
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] UndoAsync: JS undo threw an exception — svgId={SvgId}", _svgId);
                }
            }
            else
            {
                Logger.LogWarning("[SVGCanvas] UndoAsync: _jsModule is null, cannot call undo.");
            }
        }

        /// <summary>Removes all strokes from the canvas.</summary>
        public async Task ClearAsync()
        {
            Logger.LogInformation("[SVGCanvas] ClearAsync — svgId={SvgId}", _svgId);
            if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("clear", _svgId);
                    _strokeCount = 0;
                    Logger.LogInformation("[SVGCanvas] ClearAsync: JS clear completed.");
                    // JS manages the undo button disabled state via setUndoDisabled(); no re-render needed.
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] ClearAsync: JS clear threw an exception — svgId={SvgId}", _svgId);
                }
            }
            else
            {
                Logger.LogWarning("[SVGCanvas] ClearAsync: _jsModule is null, cannot call clear.");
            }
        }

        /// <summary>Downloads the current drawing as an SVG file.</summary>
        public async Task ExportSvgAsync()
        {
            Logger.LogInformation("[SVGCanvas] ExportSvgAsync — svgId={SvgId}, backgroundColor={BgColor}", _svgId, BackgroundColor);
            if (_jsModule is not null)
            {
                try
                {
                    var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                    var fileName = $"drawing-{timestamp}.svg";
                    Logger.LogInformation("[SVGCanvas] ExportSvgAsync: calling JS downloadSvg — fileName={FileName}", fileName);
                    await _jsModule.InvokeVoidAsync(
                        "downloadSvg", _svgId, fileName, BackgroundColor);
                    Logger.LogInformation("[SVGCanvas] ExportSvgAsync: JS downloadSvg completed.");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[SVGCanvas] ExportSvgAsync: JS downloadSvg threw an exception — svgId={SvgId}", _svgId);
                }
            }
            else
            {
                Logger.LogWarning("[SVGCanvas] ExportSvgAsync: _jsModule is null, cannot call downloadSvg.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            Logger.LogInformation("[SVGCanvas] DisposeAsync — svgId={SvgId}", _svgId);
            if (_jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("dispose", _svgId);
                    await _jsModule.DisposeAsync();
                    Logger.LogInformation("[SVGCanvas] DisposeAsync: JS module disposed.");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[SVGCanvas] DisposeAsync: error during JS dispose (JS runtime may already be gone).");
                }
            }
            _dotNetRef?.Dispose();
        }
    }
}
