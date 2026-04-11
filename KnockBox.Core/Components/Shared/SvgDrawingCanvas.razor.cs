using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using KnockBox.Core.Services.Drawing;

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
        [Inject] private ISvgClipboardService ClipboardService { get; set; } = default!;

        /// <summary>CSS width of the canvas container (e.g. "100%", "800px").</summary>
        [Parameter] public string Width { get; set; } = "100%";

        /// <summary>CSS height of the canvas container (e.g. "400px", "60vh").</summary>
        [Parameter] public string Height { get; set; } = "400px";

        /// <summary>
        /// Optional overlay content to render perfectly inside the drawing area bounds.
        /// </summary>
        [Parameter] public RenderFragment? OverlayContent { get; set; }

        /// <summary>
        /// Whether to show the drawing toolbar.
        /// </summary>
        [Parameter] public bool ShowToolbar { get; set; } = true;

        /// <summary>CSS background color of the drawing surface.</summary>
        [Parameter] public string BackgroundColor { get; set; } = "#ffffff";

        /// <summary>Initial stroke color (hex or CSS color string).</summary>
        [Parameter] public string StrokeColor { get; set; } = "#000000";

        /// <summary>Initial stroke width in pixels.</summary>
        [Parameter] public double StrokeWidth { get; set; } = 3;

        /// <summary>
        /// When <c>true</c>, displays "📋 Copy" and "📥 Paste" buttons in the toolbar.
        /// Copy serializes the drawing to a server-side share code; Paste loads a drawing
        /// from a code produced by another user on any device.
        /// </summary>
        [Parameter] public bool EnableSharing { get; set; } = false;

        /// <summary>
        /// Optional SVG inner markup to display as a read-only background layer behind the
        /// drawing surface. Rendered as a non-interactive underlay so the player can draw
        /// on top of an existing image (e.g. a composite of their selected outfit items).
        /// When <see langword="null"/> (the default) no background is shown.
        /// <para>Content is sanitized — only safe SVG elements are retained.</para>
        /// </summary>
        [Parameter] public string? BackgroundSvgContent { get; set; }

        /// <summary>
        /// Optional SVG inner markup rendered as a trusted (unsanitized) background layer.
        /// Use only for server-generated content (e.g. mannequin image references) that
        /// requires tags not in the sanitizer allowlist such as <c>&lt;image&gt;</c>.
        /// Renders behind <see cref="BackgroundSvgContent"/> in DOM order.
        /// </summary>
        [Parameter] public string? TrustedBackgroundSvgContent { get; set; }

        /// <summary>Width of the SVG viewBox coordinate space (device-independent units).</summary>
        [Parameter] public int? ViewBoxWidth { get; set; }

        /// <summary>Height of the SVG viewBox coordinate space (device-independent units).</summary>
        [Parameter] public int? ViewBoxHeight { get; set; }

        private string? ViewBoxAttribute => ViewBoxWidth.HasValue && ViewBoxHeight.HasValue
            ? $"0 0 {ViewBoxWidth} {ViewBoxHeight}"
            : null;

        // Sanitized version of BackgroundSvgContent, computed in OnParametersSet.
        private string? _sanitizedBackground;

        // Unsanitized trusted background, assigned directly in OnParametersSet.
        private string? _trustedBackground;

        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<SvgDrawingCanvas>? _dotNetRef;

        private readonly string _svgId = $"svg-canvas-{Guid.NewGuid():N}";
        private string _colorInputId => $"color-{_svgId}";
        private string _sizeInputId => $"size-{_svgId}";

        private string _currentColor = "#000000";
        private string _customSwatchColor = "#808080";
        private bool _isCustomColorActive;
        private bool _isEraserActive;
        private bool _isFillActive;
        private double _currentStrokeWidth = 3;
        private int _strokeCount;

        // Curated color palette — bold, complementary colors.
        private static readonly string[] _colorSwatches =
        [
            "#000000", "#ffffff", "#ef4444",
            "#3b82f6", "#22c55e", "#f59e0b",
        ];

        // Preset brush sizes: small, medium, large.
        private static readonly (string Label, int Size)[] _sizePresets =
        [
            ("S", 3), ("M", 8), ("L", 16),
        ];

        protected override void OnInitialized()
        {
            _currentColor = StrokeColor;
            _currentStrokeWidth = StrokeWidth;
        }

        protected override void OnParametersSet()
        {
            _sanitizedBackground = SvgContentSanitizer.Sanitize(BackgroundSvgContent);
            _trustedBackground = TrustedBackgroundSvgContent;
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

        /// <summary>Called from JavaScript when the active tool changes.</summary>
        [JSInvokable]
        public void OnToolChanged(string toolName)
        {
            _isEraserActive = toolName == "eraser";
            _isFillActive = toolName == "fill";
            StateHasChanged();
        }

        /// <summary>Called from JavaScript when the stroke color changes.</summary>
        [JSInvokable]
        public void OnColorChanged(string color)
        {
            _currentColor = color;
            _isCustomColorActive = !_colorSwatches.Any(s =>
                string.Equals(s, color, StringComparison.OrdinalIgnoreCase));
            if (_isCustomColorActive)
            {
                _customSwatchColor = color;
            }
            StateHasChanged();
        }

        /// <summary>Called from JavaScript when the stroke width changes.</summary>
        [JSInvokable]
        public void OnStrokeWidthChanged(double width)
        {
            _currentStrokeWidth = width;
            StateHasChanged();
        }

        private string CurrentSizeLabel =>
            _sizePresets.FirstOrDefault(p => p.Size == (int)_currentStrokeWidth).Label
            ?? ((int)_currentStrokeWidth).ToString();

        /// <summary>
        /// Called from JavaScript when the Copy toolbar button is clicked.
        /// Serializes the current drawing and stores it server-side, returning a share code
        /// that another user can enter on a different device to paste the drawing.
        /// Returns <c>null</c> when the canvas is empty or serialization fails.
        /// </summary>
        [JSInvokable]
        public async Task<string?> OnCopyRequestedAsync()
        {
            if (_jsModule is null) return null;
            try
            {
                var content = await ReadSvgInChunksWithBgAsync();
                if (string.IsNullOrEmpty(content)) return null;
                return ClipboardService.Store(content);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGCanvas] OnCopyRequestedAsync failed — svgId={SvgId}", _svgId);
                return null;
            }
        }

        /// <summary>
        /// Called from JavaScript when the user submits a share code via the Paste toolbar input.
        /// Retrieves the stored drawing content for <paramref name="shareCode"/> and loads it
        /// into this canvas. Returns <c>false</c> if the code is unknown or expired.
        /// </summary>
        [JSInvokable]
        public async Task<bool> OnPasteRequestedAsync(string shareCode)
        {
            if (_jsModule is null) return false;
            try
            {
                var content = ClipboardService.Retrieve(shareCode);
                if (content is null) return false;
                _strokeCount = await _jsModule.InvokeAsync<int>("loadSvgContent", _svgId, content);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGCanvas] OnPasteRequestedAsync failed — svgId={SvgId}", _svgId);
                return false;
            }
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

        /// <summary>Restores the most recently undone action.</summary>
        public async Task RedoAsync()
        {
            if (_jsModule is null)
            {
                Logger.LogWarning("[SVGCanvas] RedoAsync: JS module not initialized — svgId={SvgId}", _svgId);
                return;
            }
            try
            {
                _strokeCount = await _jsModule.InvokeAsync<int>("redo", _svgId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGCanvas] RedoAsync failed — svgId={SvgId}", _svgId);
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

        /// <summary>
        /// Returns the current SVG drawing content as a serialised string, or
        /// <see langword="null"/> when the canvas is empty or has not yet been initialised.
        /// </summary>
        /// <exception cref="JSException">Thrown when the JS interop call fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the Blazor circuit is
        /// temporarily unavailable. The caller should surface a retry prompt rather than
        /// treating the canvas as empty.</exception>
        public async Task<string?> GetSvgContentAsync()
        {
            if (_jsModule is null)
            {
                Logger.LogWarning("[SVGCanvas] GetSvgContentAsync: JS module not initialized — svgId={SvgId}", _svgId);
                return null;
            }
            try
            {
                // If the JS-side canvas state was lost (e.g. after a Blazor circuit
                // reconnect that reset the JavaScript runtime), re-initialize the canvas
                // so it is usable again. Any drawing the user made is no longer
                // recoverable from JS, so return null to indicate an empty canvas.
                if (!await _jsModule.InvokeAsync<bool>("isInitialized", _svgId))
                {
                    Logger.LogWarning(
                        "[SVGCanvas] GetSvgContentAsync: JS state lost after circuit reconnect, canvas content unrecoverable — re-initializing — svgId={SvgId}", _svgId);
                    await _jsModule.InvokeVoidAsync(
                        "initialize", _svgId, _dotNetRef, _currentColor, _currentStrokeWidth, BackgroundColor);
                    _strokeCount = 0;
                    return null;
                }

                return await ReadSvgInChunksAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGCanvas] GetSvgContentAsync failed — svgId={SvgId}", _svgId);
                throw;
            }
        }

        /// <summary>
        /// Retrieves the current SVG content from JS by calling
        /// <c>prepareSvgContentForChunkedRead</c> once to get the total length, then
        /// fetching the markup in <see cref="SvgChunkSize"/>-character segments via
        /// <c>getSvgContentChunk</c>.
        /// <para>
        /// This avoids sending the entire SVG as a single SignalR message, which would
        /// fail for complex drawings that exceed the default 32 KB receive limit.
        /// </para>
        /// </summary>
        private Task<string?> ReadSvgInChunksAsync()
            => ReadSvgInChunksAsync("prepareSvgContentForChunkedRead");

        private Task<string?> ReadSvgInChunksWithBgAsync()
            => ReadSvgInChunksAsync("prepareSvgContentWithBgForChunkedRead");

        private async Task<string?> ReadSvgInChunksAsync(string prepareFunction)
        {
            if (_jsModule is null)
            {
                Logger.LogWarning("ReadSvgInChunksAsync: JS module not initialized.");
                return null;
            }

            // First call: JS serializes the SVG into a per-instance cache and returns
            // the total character count. This response is always tiny (just an int).
            var totalLength = await _jsModule.InvokeAsync<int>(prepareFunction, _svgId);
            if (totalLength == 0) return null;

            // Fetch the cached SVG string in bounded chunks so that no single SignalR
            // message approaches the server's MaximumReceiveMessageSize limit.
            var sb = new System.Text.StringBuilder(totalLength);
            for (int offset = 0; offset < totalLength; offset += SvgChunkSize)
            {
                var chunkLength = Math.Min(SvgChunkSize, totalLength - offset);
                sb.Append(await _jsModule.InvokeAsync<string>("getSvgContentChunk", _svgId, offset, chunkLength));
            }

            var result = sb.ToString();
            return result.Length > 0 ? result : null;
        }

        // Maximum characters transferred per JS interop chunk. Each character in a
        // JavaScript string is a UTF-16 code unit (2 bytes), so 12 000 chars ≈ 24 KB
        // of raw data — comfortably below the default 32 KB SignalR message limit even
        // after JSON framing overhead is added.
        private const int SvgChunkSize = 12_000;

        /// <summary>Downloads the current drawing as an SVG file.</summary>
        public async Task ExportSvgAsync()        {
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
                }
                catch (JSDisconnectedException) { }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[SVGCanvas] DisposeAsync: error during JS dispose — svgId={SvgId}", _svgId);
                }

                try
                {
                    await _jsModule.DisposeAsync();
                }
                catch (JSDisconnectedException) { }
            }
            _dotNetRef?.Dispose();
        }
    }
}
