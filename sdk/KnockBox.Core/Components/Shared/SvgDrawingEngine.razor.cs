using KnockBox.Core.Services.Drawing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace KnockBox.Core.Components.Shared
{
    /// <summary>
    /// Headless drawing engine that binds to a consumer-supplied <c>&lt;svg id="..."&gt;</c>
    /// element and exposes the full drawing API (stroke, undo/redo, clear, tool selection,
    /// copy/paste, export). Pair with <see cref="SvgDrawingToolbar"/> for the default UI,
    /// or drive directly from a custom toolbar.
    /// </summary>
    public partial class SvgDrawingEngine : ComponentBase, IAsyncDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private ILogger<SvgDrawingEngine> Logger { get; set; } = default!;
        [Inject] private ISvgClipboardService ClipboardService { get; set; } = default!;

        /// <summary>
        /// The id of the consumer-supplied <c>&lt;svg&gt;</c> element to attach to.
        /// Must be in the DOM by the engine's first render — render the engine AFTER the SVG
        /// in markup, not before.
        /// </summary>
        [Parameter, EditorRequired] public string TargetSvgId { get; set; } = default!;

        /// <summary>
        /// Effective background color of the consumer's SVG. Used as the flood-fill seed
        /// color and the export background. Must match the actual background of the
        /// supplied <c>&lt;svg&gt;</c> element — the engine has no way to inspect it.
        /// </summary>
        [Parameter] public string BackgroundColor { get; set; } = "#ffffff";

        /// <summary>Initial stroke color.</summary>
        [Parameter] public string InitialColor { get; set; } = "#000000";

        /// <summary>Initial stroke width in pixels.</summary>
        [Parameter] public double InitialStrokeWidth { get; set; } = 3;

        /// <summary>
        /// Fires whenever the JS side reports a completed stroke (or undo / redo / fill /
        /// clear / paste). The argument is the current stroke count after the operation.
        /// </summary>
        [Parameter] public EventCallback<int> StrokeCompleted { get; set; }

        /// <summary>
        /// Optional content rendered inside a CascadingValue of this engine, so any
        /// <see cref="SvgDrawingToolbar"/> or custom toolbar nested here can pick up the
        /// engine via [CascadingParameter] without the first-render @ref binding race.
        /// </summary>
        [Parameter] public RenderFragment? ChildContent { get; set; }

        // ── Read-only state surface for toolbars to render against ─────────────

        public string CurrentColor => _currentColor;
        public double CurrentStrokeWidth => _currentStrokeWidth;
        public string CurrentTool => _currentTool;
        public bool CanUndo => _canUndo;
        public bool CanRedo => _canRedo;
        public int StrokeCount => _strokeCount;

        /// <summary>
        /// Fires after any of the read-only state properties change. Subscribers should
        /// call <c>InvokeAsync(StateHasChanged)</c> to refresh their UI.
        /// </summary>
        public event Func<ValueTask>? StateChanged;

        // Stable self-reference for CascadingValue's Value="@Self". Bare `this` in a
        // Razor attribute can be ambiguous to the Razor compiler; routing through a
        // typed property guarantees we cascade the engine instance, not a string.
        private SvgDrawingEngine Self => this;

        private string _currentColor = "#000000";
        private double _currentStrokeWidth = 3;
        private string _currentTool = "brush";
        private bool _canUndo;
        private bool _canRedo;
        private int _strokeCount;

        private IJSObjectReference? _jsModule;
        private DotNetObjectReference<SvgDrawingEngine>? _dotNetRef;
        private string? _initializedSvgId;

        // Maximum characters per JS interop chunk. Each char is a UTF-16 code unit
        // (2 bytes); 12 000 chars ≈ 24 KB raw — comfortably under the default 32 KB
        // SignalR receive limit after JSON framing.
        private const int SvgChunkSize = 12_000;

        protected override void OnInitialized()
        {
            _currentColor = InitialColor;
            _currentStrokeWidth = InitialStrokeWidth;
        }

        protected override async Task OnParametersSetAsync()
        {
            // Re-initialize against a new target if the parameter changes mid-life.
            if (_initializedSvgId is not null
                && !string.Equals(_initializedSvgId, TargetSvgId, StringComparison.Ordinal))
            {
                Logger.LogInformation(
                    "[SVGEngine] TargetSvgId changed from {Old} to {New} — re-initializing.",
                    _initializedSvgId, TargetSvgId);
                await DisposeJsAsync(_initializedSvgId);
                _initializedSvgId = null;
                if (_jsModule is not null)
                {
                    await InitializeJsAsync();
                }
            }
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
                    await InitializeJsAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "[SVGEngine] Failed to load JS module — svgId={SvgId}", TargetSvgId);
                }
            }
        }

        private async Task InitializeJsAsync()
        {
            if (_jsModule is null || _dotNetRef is null) return;
            try
            {
                await _jsModule.InvokeVoidAsync(
                    "initialize", TargetSvgId, _dotNetRef,
                    _currentColor, _currentStrokeWidth, BackgroundColor);
                _initializedSvgId = TargetSvgId;
                Logger.LogDebug("[SVGEngine] Initialized — svgId={SvgId}", TargetSvgId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "[SVGEngine] Failed to initialize JS — svgId={SvgId}", TargetSvgId);
            }
        }

        // ── JSInvokable callbacks ──────────────────────────────────────────────

        /// <summary>
        /// Called from JS whenever the stroke set changes (stroke / undo / redo / clear /
        /// erase / fill / paste). JS supplies the post-operation stroke count and the
        /// undo/redo availability flags.
        /// </summary>
        [JSInvokable]
        public async Task OnStrokeCompleted(int strokeCount, bool canUndo, bool canRedo)
        {
            _strokeCount = strokeCount;
            _canUndo = canUndo;
            _canRedo = canRedo;
            await RaiseStateChangedAsync();
            if (StrokeCompleted.HasDelegate)
                await StrokeCompleted.InvokeAsync(strokeCount);
        }

        // ── Public drawing API ─────────────────────────────────────────────────

        public async Task SetColorAsync(string color)
        {
            if (string.IsNullOrEmpty(color) || string.Equals(color, _currentColor, StringComparison.OrdinalIgnoreCase))
                return;
            _currentColor = color;
            // Switching color from a non-brush tool implicitly returns to brush, matching
            // the prior UX where picking a swatch deselected eraser/fill.
            if (_currentTool != "brush")
            {
                _currentTool = "brush";
                if (_jsModule is not null)
                    await SafeInvokeAsync(_ => _jsModule.InvokeVoidAsync("setTool", TargetSvgId, "brush").AsTask());
            }
            if (_jsModule is not null)
                await SafeInvokeAsync(_ => _jsModule.InvokeVoidAsync("setColor", TargetSvgId, color).AsTask());
            await RaiseStateChangedAsync();
        }

        public async Task SetStrokeWidthAsync(double width)
        {
            if (width < 1) width = 1;
            if (width > 30) width = 30;
            if (width == _currentStrokeWidth) return;
            _currentStrokeWidth = width;
            if (_jsModule is not null)
                await SafeInvokeAsync(_ => _jsModule.InvokeVoidAsync("setStrokeWidth", TargetSvgId, width).AsTask());
            await RaiseStateChangedAsync();
        }

        /// <summary>
        /// Sets the active tool. Recognised values: <c>"brush"</c>, <c>"eraser"</c>,
        /// <c>"fill"</c>. Toggling an already-active non-brush tool reverts to brush.
        /// </summary>
        public async Task SetToolAsync(string tool)
        {
            if (tool != "brush" && tool != "eraser" && tool != "fill")
                tool = "brush";
            // Re-clicking an active non-brush tool returns to brush.
            if (tool != "brush" && _currentTool == tool)
                tool = "brush";
            if (tool == _currentTool) return;
            _currentTool = tool;
            if (_jsModule is not null)
                await SafeInvokeAsync(_ => _jsModule.InvokeVoidAsync("setTool", TargetSvgId, tool).AsTask());
            await RaiseStateChangedAsync();
        }

        public Task UndoAsync() => InvokeJsAsync("undo");
        public Task RedoAsync() => InvokeJsAsync("redo");
        public Task ClearAsync() => InvokeJsAsync("clear");

        private async Task InvokeJsAsync(string fn)
        {
            if (_jsModule is null)
            {
                Logger.LogWarning("[SVGEngine] {Fn}: JS module not initialized — svgId={SvgId}", fn, TargetSvgId);
                return;
            }
            try
            {
                await _jsModule.InvokeVoidAsync(fn, TargetSvgId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGEngine] {Fn} failed — svgId={SvgId}", fn, TargetSvgId);
            }
        }

        /// <summary>
        /// Returns the current SVG drawing content as a serialised string, or
        /// <see langword="null"/> when the canvas is empty.
        /// </summary>
        /// <exception cref="JSException">JS interop failure.</exception>
        /// <exception cref="OperationCanceledException">Blazor circuit temporarily unavailable.</exception>
        public async Task<string?> GetSvgContentAsync()
        {
            if (_jsModule is null) return null;
            try
            {
                if (!await _jsModule.InvokeAsync<bool>("isInitialized", TargetSvgId))
                {
                    Logger.LogWarning(
                        "[SVGEngine] GetSvgContentAsync: JS state lost after circuit reconnect — re-initializing. svgId={SvgId}",
                        TargetSvgId);
                    await InitializeJsAsync();
                    return null;
                }
                return await ReadSvgInChunksAsync("prepareSvgContentForChunkedRead");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGEngine] GetSvgContentAsync failed — svgId={SvgId}", TargetSvgId);
                throw;
            }
        }

        public async Task ExportSvgAsync(string? fileName = null)
        {
            if (_jsModule is null) return;
            try
            {
                fileName ??= $"drawing-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.svg";
                await _jsModule.InvokeVoidAsync("downloadSvg", TargetSvgId, fileName, BackgroundColor);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGEngine] ExportSvgAsync failed — svgId={SvgId}", TargetSvgId);
            }
        }

        /// <summary>
        /// Serialises the drawing (with background) and stores it in the clipboard service.
        /// Returns the share code, or <see langword="null"/> if the canvas is empty.
        /// </summary>
        public async Task<string?> CopyToShareCodeAsync()
        {
            if (_jsModule is null) return null;
            try
            {
                var content = await ReadSvgInChunksAsync("prepareSvgContentWithBgForChunkedRead");
                if (string.IsNullOrEmpty(content)) return null;
                return ClipboardService.Store(content);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGEngine] CopyToShareCodeAsync failed — svgId={SvgId}", TargetSvgId);
                return null;
            }
        }

        /// <summary>
        /// Retrieves stored drawing content for <paramref name="shareCode"/> and loads it
        /// into the canvas. Returns <c>false</c> when the code is unknown or expired.
        /// </summary>
        public async Task<bool> PasteFromShareCodeAsync(string shareCode)
        {
            if (_jsModule is null) return false;
            try
            {
                var content = ClipboardService.Retrieve(shareCode);
                if (content is null) return false;
                await _jsModule.InvokeAsync<int>("loadSvgContent", TargetSvgId, content);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SVGEngine] PasteFromShareCodeAsync failed — svgId={SvgId}", TargetSvgId);
                return false;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private async Task<string?> ReadSvgInChunksAsync(string prepareFunction)
        {
            if (_jsModule is null) return null;
            var totalLength = await _jsModule.InvokeAsync<int>(prepareFunction, TargetSvgId);
            if (totalLength == 0) return null;
            var sb = new System.Text.StringBuilder(totalLength);
            for (int offset = 0; offset < totalLength; offset += SvgChunkSize)
            {
                var chunkLength = Math.Min(SvgChunkSize, totalLength - offset);
                sb.Append(await _jsModule.InvokeAsync<string>(
                    "getSvgContentChunk", TargetSvgId, offset, chunkLength));
            }
            var result = sb.ToString();
            return result.Length > 0 ? result : null;
        }

        private async ValueTask RaiseStateChangedAsync()
        {
            var handler = StateChanged;
            if (handler is null) return;
            // Iterate the invocation-list array directly — casting each element
            // inline avoids the LINQ Cast<> iterator allocation on every fire,
            // and this runs on every stroke/undo/redo/clear/fill/tool change.
            var subs = handler.GetInvocationList();
            for (int i = 0; i < subs.Length; i++)
            {
                try { await ((Func<ValueTask>)subs[i])(); }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[SVGEngine] StateChanged subscriber threw — svgId={SvgId}", TargetSvgId);
                }
            }
        }

        private async Task SafeInvokeAsync(Func<object?, Task> body)
        {
            try { await body(null); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[SVGEngine] JS invocation failed — svgId={SvgId}", TargetSvgId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeJsAsync(_initializedSvgId ?? TargetSvgId);
            if (_jsModule is not null)
            {
                try { await _jsModule.DisposeAsync(); }
                catch (JSDisconnectedException) { }
                catch (ObjectDisposedException) { }
            }
            _dotNetRef?.Dispose();
        }

        private async Task DisposeJsAsync(string svgId)
        {
            if (_jsModule is null || string.IsNullOrEmpty(svgId)) return;
            try { await _jsModule.InvokeVoidAsync("dispose", svgId); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[SVGEngine] DisposeJsAsync failed — svgId={SvgId}", svgId);
            }
        }
    }
}
