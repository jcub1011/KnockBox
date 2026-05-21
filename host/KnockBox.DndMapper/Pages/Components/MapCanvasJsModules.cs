using Microsoft.JSInterop;

namespace KnockBox.DndMapper.Pages.Components
{
    // Holds the five JS ES-module handles MapCanvas attaches on first render
    // (viewport, image drag, focus drag, fog paint, SVG metrics) together with
    // their DotNetObjectReferences. Exists purely to drain the duplicated
    // try/catch boilerplate out of MapCanvas — every module followed the
    // pattern "import, capture ref, optionally call a JS-side teardown verb,
    // dispose the module, dispose the ref, swallow JSDisconnectedException".
    //
    // Exposes the raw IJSObjectReferences as nullable properties: MapCanvas
    // still owns the per-module InvokeVoidAsync calls (their argument shapes
    // are too varied to bundle here usefully).
    internal sealed class MapCanvasJsModules : IAsyncDisposable
    {
        private readonly IJSRuntime _js;
        private readonly ILogger _logger;
        private readonly string _svgId;

        private Entry? _viewport;
        private Entry? _imageDrag;
        private Entry? _focusDrag;
        private Entry? _fogPaint;
        private Entry? _metrics; // metrics has no DotNet ref and no JS-side teardown

        public IJSObjectReference? Viewport => _viewport?.Module;
        public IJSObjectReference? ImageDrag => _imageDrag?.Module;
        public IJSObjectReference? FocusDrag => _focusDrag?.Module;
        public IJSObjectReference? FogPaint => _fogPaint?.Module;
        public IJSObjectReference? Metrics => _metrics?.Module;

        public DotNetObjectReference<MapCanvas>? ViewportRef => _viewport?.DotNetRef;
        public DotNetObjectReference<MapCanvas>? ImageDragRef => _imageDrag?.DotNetRef;
        public DotNetObjectReference<MapCanvas>? FocusDragRef => _focusDrag?.DotNetRef;
        public DotNetObjectReference<MapCanvas>? FogPaintRef => _fogPaint?.DotNetRef;

        public MapCanvasJsModules(IJSRuntime js, ILogger logger, string svgId)
        {
            _js = js;
            _logger = logger;
            _svgId = svgId;
        }

        public async ValueTask LoadMetricsAsync()
            => _metrics = await TryLoadAsync("dndMapperSvgMetrics.js", owner: null);

        public async ValueTask LoadViewportAsync(MapCanvas owner)
            => _viewport = await TryLoadAsync("dndMapperViewport.js", owner);

        public async ValueTask LoadImageDragAsync(MapCanvas owner)
            => _imageDrag = await TryLoadAsync("dndMapperImageDrag.js", owner);

        public async ValueTask LoadFocusDragAsync(MapCanvas owner)
            => _focusDrag = await TryLoadAsync("dndMapperFocusDrag.js", owner);

        public async ValueTask LoadFogPaintAsync(MapCanvas owner)
            => _fogPaint = await TryLoadAsync("dndMapperFogPaint.js", owner);

        private async ValueTask<Entry?> TryLoadAsync(string fileName, MapCanvas? owner)
        {
            try
            {
                var mod = await _js.InvokeAsync<IJSObjectReference>(
                    "import", $"./_content/KnockBox.DndMapper/js/{fileName}");
                var dotNetRef = owner is null ? null : DotNetObjectReference.Create(owner);
                return new Entry(mod, dotNetRef);
            }
            catch (JSDisconnectedException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load JS module {Module}.", fileName);
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Modules that own per-instance JS state (viewport + image drag are
            // keyed by svgId, fog + focus by gesture state) get a teardown call
            // before module disposal so their windowed listeners detach cleanly.
            await DisposeWithTeardown(_viewport, teardownVerb: "dispose", passSvgId: true);
            _viewport = null;
            await DisposeWithTeardown(_imageDrag, teardownVerb: "dispose", passSvgId: true);
            _imageDrag = null;
            await DisposeWithTeardown(_focusDrag, teardownVerb: "cancelDrag", passSvgId: false);
            _focusDrag = null;
            await DisposeWithTeardown(_fogPaint, teardownVerb: "cancelStroke", passSvgId: false);
            _fogPaint = null;
            await DisposeWithTeardown(_metrics, teardownVerb: null, passSvgId: false);
            _metrics = null;
        }

        private async ValueTask DisposeWithTeardown(Entry? entry, string? teardownVerb, bool passSvgId)
        {
            if (entry is null) return;
            if (teardownVerb is not null)
            {
                try
                {
                    if (passSvgId) await entry.Module.InvokeVoidAsync(teardownVerb, _svgId);
                    else await entry.Module.InvokeVoidAsync(teardownVerb);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore — teardown is best-effort */ }
            }
            try { await entry.Module.DisposeAsync(); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            entry.DotNetRef?.Dispose();
        }

        private sealed record Entry(IJSObjectReference Module, DotNetObjectReference<MapCanvas>? DotNetRef);
    }
}
