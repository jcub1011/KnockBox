using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class MapCanvas : DisposableComponent, IAsyncDisposable
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter, EditorRequired] public Map Map { get; set; } = default!;
        [Parameter] public string RoomCode { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;

        [Parameter] public Guid? SelectedImageId { get; set; }
        [Parameter] public EventCallback<Guid?> SelectedImageIdChanged { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] protected ILogger<MapCanvas> Logger { get; set; } = default!;
        [Inject] protected TokenFocusService TokenFocus { get; set; } = default!;
        [Inject] protected IFogPaintContext FogContext { get; set; } = default!;
        // Host's circuit has a populated blob cache; players' circuits resolve
        // the service but report a cache miss for every image and we render
        // the /blob-share/{token} fallback. Always injected — registration is
        // Scoped, so DI always supplies an instance.
        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;

        [CascadingParameter] public DndMapperViewport? Viewport { get; set; }
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private const double MinZoom = 0.01;
        private const double MaxZoom = 10.0;
        private const int LeftMouseButton = 0;
        private const int MiddleMouseButton = 1;

        private readonly string _svgId = $"dndm-svg-{Guid.NewGuid():N}";
        private readonly string _markupSvgId = $"svg-canvas-{Guid.NewGuid():N}";
        // Id for the viewport-sized <canvas> that dndMapperBitmapCanvas.js
        // initialises against. One per MapCanvas instance, mirrors the _svgId
        // pattern; the JS module keys its instance map by this id.
        private readonly string _canvasId = $"dndm-canvas-{Guid.NewGuid():N}";
        private SvgDrawingEngine? _markupEngine;
        // Tracks whether we've already triggered the extra render that follows the
        // engine's @ref binding. Toggled off when _markupActive flips false so the
        // re-render fires again on the next activation.
        private bool _markupToolbarRenderTriggered;
        private ElementReference _frameRef;

        private double _panX;
        private double _panY;
        private double _zoom = 1.0;

        private bool _spaceHeld;

        private bool LocalShowGridLines { get; set; } = true;
        private bool _gridInitialized;

        // Assigned in OnInitialized; the individual module slots inside stay
        // null until OnAfterRenderAsync imports each one. Marked null! so
        // callers don't trip a CS8602 chain through `_jsModules?.X`.
        private MapCanvasJsModules _jsModules = null!;
        // Tracks which (map, ImagesMembershipVersion) was last pushed to the JS
        // drag module so we don't re-marshal on every render. The membership
        // counter on Map bumps only when an image is added, removed, or its
        // Locked flag toggles — which is the exact set the JS module cares
        // about — so pure transform edits never trigger a marshal.
        private (Guid MapId, int Version)? _imageDragSnapshot;
        // Tracks the (map id, ImagesVersion) last marshaled into the bitmap
        // canvas module. ImagesVersion bumps on any image change (transform
        // or membership), which is exactly what the canvas redraw cares
        // about. The membership-only counter on the image-drag module is
        // intentionally separate — that module only re-marshals on
        // add/remove/lock.
        private (Guid MapId, int Version, int LocalUrlsVersion)? _bitmapCanvasSnapshot;
        // Bumped whenever _localImageUrls changes (new host-blob URLs land)
        // so the canvas re-marshal picks up freshly resolved sources.
        private int _localImageUrlsVersion;
        private string _currentJsMode = "none";
        // Tracks (map id, grid dims) so we re-push when the map is swapped
        // or its grid is resized.
        private (Guid MapId, int Width, int Height)? _lastBoundsSent;

        // Version-keyed memoization (paired with Map.FogVersion / Map.ImagesVersion).
        // Each Map carries its own version counters starting at 0, so the Map.Id
        // must be part of the cache key — otherwise switching to a different map
        // whose counters happen to match keeps serving the previous map's data
        // (visible as stale layer bitmaps + fog until a per-map edit bumps the
        // counter past the cached value).
        private Guid _cachedFogMapId;
        private int _cachedFogVersion = -1;
        private string _cachedFogPath = string.Empty;

        // Host-only direct-blob URLs keyed by image id. Populated lazily by
        // RefreshLocalImageUrlsAsync from the library's blob cache; players'
        // circuits stay empty here and the renderer falls back to the
        // /blob-share/{token} URL. Lets the host's own browser render its
        // own bitmaps without the wasteful SignalR roundtrip that the
        // share endpoint does to fetch bytes from this same browser's IDB.
        private readonly Dictionary<Guid, string> _localImageUrls = new();

        // ── Image transform drag (commit-side) ──────────────────────────
        // Per-frame visual updates live in dndMapperImageDrag.js. C# only
        // sees the start + end of the drag via OnImageDragEnd, builds a
        // DragState, runs SnapDragToGrid, and calls UpdateImageTransformAsync.
        internal enum HandleKind { Body, NW, NE, SW, SE, Rotate }

        internal sealed class DragState
        {
            public Guid ImageId;
            public HandleKind Kind;
            public double OrigX, OrigY, OrigW, OrigH, OrigRot;
            public double X, Y, W, H, Rot;
        }

        private MapImage? SelectedImage =>
            SelectedImageId is Guid id ? Map.Images.FirstOrDefault(i => i.Id == id) : null;

        protected override void OnParametersSet()
        {
            if (!_gridInitialized && Map is not null)
            {
                LocalShowGridLines = Map.Grid.ShowGridLines;
                _gridInitialized = true;
            }
            base.OnParametersSet();
        }

        protected override async Task OnParametersSetAsync()
        {
            await RefreshLocalImageUrlsAsync().ConfigureAwait(false);
            await base.OnParametersSetAsync().ConfigureAwait(false);
        }

        // Single entry-point the razor template uses to resolve an image's
        // src. Returns null only if neither path is available (host
        // disconnected mid-render), in which case the template falls
        // through to a placeholder div.
        //
        // Host: _localImageUrls has the blob: URL — straight-to-IDB,
        // no SignalR round-trip back to this same browser.
        // Players or first render on the host (before
        // RefreshLocalImageUrlsAsync runs): falls through to the
        // /blob-share/{token} capability URL.
        //
        // Kept as a code-behind method so the Razor template stays in
        // plain markup mode — embedding the lookup + ternary inline
        // caused Razor to silently emit attribute-less <img> tags.
        internal string? ResolveImageSrc(MapImage img)
        {
            if (_localImageUrls.TryGetValue(img.Id, out var localUrl) && localUrl is not null)
                return localUrl;
            // Host-but-URL-pending guard: we own the blob locally, but the
            // async CreateObjectUrlAsync round-trip hasn't finished yet.
            // Returning null lets the bitmap canvas paint its loading
            // placeholder for one frame instead of pushing a /blob-share/
            // URL that would trigger a SignalR-backed fetch from the host's
            // browser to itself — that contention is what timed out and
            // killed the circuit on session attach.
            if (Library.HasLocalBlob(img.Id)) return null;
            if (img.ShareToken is Guid shareToken)
                return $"/blob-share/{shareToken:D}";
            return null;
        }

        /// <summary>
        /// Reconciles <see cref="_localImageUrls"/> with the current image
        /// set: drops URLs for removed images and asks the library for a
        /// <c>blob:</c> URL for any image we don't already know about. The
        /// library returns <see langword="null"/> for images this circuit
        /// doesn't own (the common player case) so the share-URL fallback
        /// stays intact. Triggers a re-render only when the set actually
        /// changes to avoid render storms during the host's first attach
        /// (which republishes every image's share token in parallel).
        /// </summary>
        private async ValueTask RefreshLocalImageUrlsAsync()
        {
            if (Map is null) return;
            var changed = false;

            if (_localImageUrls.Count > 0)
            {
                var currentIds = new HashSet<Guid>(Map.Images.Select(i => i.Id));
                foreach (var key in _localImageUrls.Keys.ToList())
                {
                    if (!currentIds.Contains(key))
                    {
                        _localImageUrls.Remove(key);
                        changed = true;
                    }
                }
            }

            foreach (var img in Map.Images)
            {
                if (_localImageUrls.ContainsKey(img.Id)) continue;
                var url = await Library.TryGetLocalObjectUrlAsync(img.Id).ConfigureAwait(false);
                if (url is null) continue;
                _localImageUrls[img.Id] = url;
                changed = true;
            }

            if (changed)
            {
                _localImageUrlsVersion++;
                await InvokeAsync(StateHasChanged).ConfigureAwait(false);
            }
        }

        // ── Markup overlay (v1.x — §5.6) ─────────────────────────────────
        // Host-only toggle: when true, an interactive SvgDrawingEngine + Toolbar covers the
        // map. The read-only saved markup is rendered inline on the SVG so pan
        // and zoom apply uniformly for everyone.
        private bool _markupActive;
        internal bool MarkupActive => _markupActive;

        // ── Focus box (host-only, drives display zoom) ───────────────────
        // When _focusActive is true, a left-button drag on the SVG enters
        // dndMapperFocusDrag.js which renders the in-flight preview entirely
        // client-side and only calls CommitFocusRect at pointer-up. Mutually
        // exclusive with markup + fog modes (each toggle clears the others).
        private bool _focusActive;

        // ── Ruler tool (host-only, fully client-side) ───────────────────
        // Left-click picks the two endpoints; third+ clicks replace point B.
        // Right-click clears both. Deactivating the tool also clears both.
        // Pure C#/SVG — no JS module needed; clicks are infrequent enough
        // that ClientToMapAsync per mousedown is acceptable.
        private bool _rulerActive;
        private (double X, double Y)? _rulerA;
        private (double X, double Y)? _rulerB;
        private const double FeetPerSquare = 5.0;

        // ── Centre-viewport broadcast (v1.x — §6.4) ──────────────────────
        // Last-seen request Nonce. When the server pushes a new request with a
        // fresh nonce, every client (including the host who sent it) recentres.
        private Guid? _lastSeenCenterNonce;

        // ── Right-click context menu ─────────────────────────────────────
        private bool _contextMenuOpen;
        private double _contextMenuClientX;
        private double _contextMenuClientY;
        // Map-space coordinates of the right-clicked cell (already snapped to grid
        // when grid snap is enabled). Captured at open-time so subsequent menu
        // actions act on the cell the user actually right-clicked.
        private double _contextMenuMapX;
        private double _contextMenuMapY;

        private IDisposable? _stateSub;

        protected override void OnInitialized()
        {
            // Initialize the JS-module container eagerly so callers don't need
            // to deal with two layers of null (container + individual module).
            // The individual module refs stay null until OnAfterRenderAsync
            // imports each one.
            _jsModules = new MapCanvasJsModules(JSRuntime, Logger, _svgId, _canvasId);
            TokenFocus.Focused += OnTokenFocusRequested;
            _stateSub = State.StateChangedEventManager.Subscribe(OnStateChanged);
            FogContext.Changed += OnFogContextChanged;
            // Seed the "last seen" nonce so an existing request from before the
            // page loaded doesn't snap the viewport on first render.
            _lastSeenCenterNonce = State.PendingCenterRequest?.Nonce;
            base.OnInitialized();
        }

        private void OnFogContextChanged()
        {
            _ = PushJsMode();
            InvokeAsync(StateHasChanged);
        }

        private bool IsFogPaintActive => IsHost && FogContext.Mode != FogPaintMode.Off;

        // True while a host-only canvas tool (fog paint/erase or focus-box or
        // ruler) is active. Images/tokens become non-interactive in this mode
        // so clicks fall through to the SVG's OnSvgMouseDown and reach the
        // tool's entry path. (Markup mode covers the canvas with its own
        // overlay, so it doesn't need to flip image/token pointer-events.)
        private bool IsCanvasToolActive => IsFogPaintActive || _focusActive || _rulerActive;

        // ── Fog toolbar handlers ─────────────────────────────────────────
        private bool _confirmFillFog;
        private bool _confirmClearFog;

        private void OnTogglePaintMode()
        {
            var next = FogContext.Mode == FogPaintMode.Paint ? FogPaintMode.Off : FogPaintMode.Paint;
            if (next != FogPaintMode.Off) ExitOtherCanvasTools();
            FogContext.Set(next, FogContext.BrushRadius);
        }

        private void OnToggleEraseMode()
        {
            var next = FogContext.Mode == FogPaintMode.Erase ? FogPaintMode.Off : FogPaintMode.Erase;
            if (next != FogPaintMode.Off) ExitOtherCanvasTools();
            FogContext.Set(next, FogContext.BrushRadius);
        }

        // Focus-box, markup, and ruler are mutually exclusive with fog
        // paint/erase. The focus/markup/ruler toggles already disable fog
        // when entering their mode; this is the mirror path for when fog
        // is being entered.
        private void ExitOtherCanvasTools()
        {
            if (_focusActive)
            {
                _focusActive = false;
                _ = CancelFocusDragJs();
            }
            _markupActive = false;
            if (_rulerActive)
            {
                _rulerActive = false;
                _rulerA = null;
                _rulerB = null;
            }
        }

        private void OnCycleBrush()
        {
            // 1 → 2 → 3 → 1. Cycling is more compact than three separate buttons
            // and matches how the toolbar's zoom controls already work.
            var next = FogContext.BrushRadius >= FogPaintContext.MaxBrush
                ? FogPaintContext.MinBrush
                : FogContext.BrushRadius + 1;
            FogContext.Set(FogContext.Mode, next);
        }

        private void OnFillFogClicked()
        {
            if (Map is null) return;
            _confirmFillFog = true;
        }

        private void OnClearFogClicked()
        {
            if (Map is null) return;
            _confirmClearFog = true;
        }

        private void OnConfirmFillFog()
        {
            _confirmFillFog = false;
            if (UserService.CurrentUser is null || Map is null) return;
            var result = Engine.FillMapWithFogAsync(State, UserService.CurrentUser, Map.Id);
            if (result.TryGetFailure(out var err))
                Logger.LogWarning("FillMapWithFogAsync failed: {Error}", err.PublicMessage);
        }

        private void OnCancelFillFog() => _confirmFillFog = false;

        private void OnConfirmClearFog()
        {
            _confirmClearFog = false;
            if (UserService.CurrentUser is null || Map is null) return;
            var result = Engine.ClearAllFogAsync(State, UserService.CurrentUser, Map.Id);
            if (result.TryGetFailure(out var err))
                Logger.LogWarning("ClearAllFogAsync failed: {Error}", err.PublicMessage);
        }

        private void OnCancelClearFog() => _confirmClearFog = false;

        private async ValueTask OnStateChanged()
        {
            // Reactor for the host's "centre everyone here" broadcast. Compare
            // against the last-seen nonce so identical successive requests still
            // re-centre (the engine issues a fresh Guid on every verb call).
            var req = State.PendingCenterRequest;
            if (req is not null && req.Nonce != _lastSeenCenterNonce && req.MapId == Map.Id)
            {
                _lastSeenCenterNonce = req.Nonce;
                if (_jsModules.Viewport is not null)
                {
                    // JS handles the rail-aware centering and commits back via
                    // OnViewportChanged. Fire-and-forget — the broadcast handler
                    // is not awaited by the state-change pipeline.
                    _ = _jsModules.Viewport.InvokeVoidAsync("centerOnWorld", _svgId, req.X, req.Y);
                }
            }
            // OnParametersSetAsync only fires on parent-pushed parameter
            // changes; a self-driven StateHasChanged from a state-change
            // notification won't re-run it. Refresh blob: URLs here too so
            // reconnect republish (which mutates state but not parameters)
            // still backfills the host's local URLs.
            await RefreshLocalImageUrlsAsync().ConfigureAwait(false);
            await InvokeAsync(StateHasChanged);
        }

        private async Task ToggleMarkup()
        {
            _markupActive = !_markupActive;
            if (_markupActive)
            {
                // The interactive drawing surface uses a fixed 0..W × 0..H viewBox
                // (in grid cell units), so the host must be at 1.0 zoom / 0,0 pan
                // for the stroke to land on the intended cell. ResetView snaps both.
                await ResetView();
                // Exclusive with focus + fog tools.
                if (_focusActive)
                {
                    _focusActive = false;
                    _ = CancelFocusDragJs();
                }
                if (FogContext.Mode != FogPaintMode.Off)
                    FogContext.Set(FogPaintMode.Off, FogContext.BrushRadius);
            }
            await PushJsMode();
        }

        /// <summary>
        /// Fires after every stroke / undo / redo / fill / clear from the markup
        /// drawing engine. Reads the serialized SVG (in pixel space because the
        /// surface's viewBox is in pixels) and persists it as cell-space markup by
        /// wrapping in <c>scale(1 / CellPixels)</c>.
        /// </summary>
        private async Task OnMarkupStrokeCompleted(int strokeCount)
        {
            if (_markupEngine is null) return;
            var user = UserService.CurrentUser;
            if (user is null) return;

            var svg = await _markupEngine.GetSvgContentAsync();
            if (string.IsNullOrWhiteSpace(svg))
            {
                var clearResult = Engine.UpdateMapMarkupAsync(State, user, Map.Id, null);
                if (clearResult.TryGetFailure(out var clearErr) && Toasts is not null)
                {
                    await Toasts.Push(clearErr.PublicMessage, DndMapperToastTone.Danger);
                }
                return;
            }

            var inv = 1.0 / Map.Grid.CellPixels;
            // Invariant culture so e.g. "0.02" doesn't become "0,02" under locales
            // where comma is the decimal separator — SVG requires `.`.
            var scaleStr = inv.ToString("0.######", CultureInfo.InvariantCulture);
            var wrapped = $"<g transform=\"scale({scaleStr})\">{svg}</g>";
            var saveResult = Engine.UpdateMapMarkupAsync(State, user, Map.Id, wrapped);
            if (saveResult.TryGetFailure(out var saveErr) && Toasts is not null)
            {
                await Toasts.Push(saveErr.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void ToggleFocusMode()
        {
            _focusActive = !_focusActive;
            if (_focusActive)
            {
                _markupActive = false;
                if (FogContext.Mode != FogPaintMode.Off)
                    FogContext.Set(FogPaintMode.Off, FogContext.BrushRadius);
                if (_rulerActive)
                {
                    _rulerActive = false;
                    _rulerA = null;
                    _rulerB = null;
                }
            }
            else
            {
                // Cancel any in-flight drag preview in the JS module.
                _ = CancelFocusDragJs();
            }
            _ = PushJsMode();
        }

        // Ruler is the simplest tool of the family: pure client-side, two
        // map-coord points plus a derived label. Activating it follows the
        // same mutual-exclusion handshake as ToggleFocusMode and the fog
        // toggles, and deactivating clears the measurement so the next
        // activation starts fresh.
        private void ToggleRulerMode()
        {
            _rulerActive = !_rulerActive;
            if (_rulerActive)
            {
                _markupActive = false;
                if (_focusActive)
                {
                    _focusActive = false;
                    _ = CancelFocusDragJs();
                }
                if (FogContext.Mode != FogPaintMode.Off)
                    FogContext.Set(FogPaintMode.Off, FogContext.BrushRadius);
            }
            else
            {
                _rulerA = null;
                _rulerB = null;
            }
            _ = PushJsMode();
        }

        private async ValueTask CancelFocusDragJs()
        {
            if (_jsModules.FocusDrag is null) return;
            try { await _jsModules.FocusDrag.InvokeVoidAsync("cancelDrag"); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception) { /* ignore */ }
        }

        private void OnClearFocusClicked()
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.ClearFocusRect(State, UserService.CurrentUser);
            if (result.TryGetFailure(out var err))
                Logger.LogWarning("ClearFocusRect failed: {Error}", err.PublicMessage);
            if (_focusActive)
            {
                _focusActive = false;
                _ = CancelFocusDragJs();
                _ = PushJsMode();
            }
        }

        private async ValueTask OnTokenFocusRequested(Guid tokenId)
        {
            try
            {
                if (Map is null) return;
                var token = Map.Tokens.FirstOrDefault(t => t.Id == tokenId);
                if (token is null) return;
                if (_jsModules.Viewport is null) return;
                // JS owns the centering math — it has the live stage rect and
                // rail widths and will commit back via OnViewportChanged so
                // _panX/_panY/_zoom and the published center stay in sync.
                await _jsModules.Viewport.InvokeVoidAsync("centerOnWorld", _svgId, token.X, token.Y);
            }
            catch (ObjectDisposedException) { /* component disposed */ }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to focus token {TokenId}.", tokenId);
            }
        }

        private string CurrentJsMode()
        {
            // Space-hold defeats the tool gate so the host can left-click pan
            // while a tool is armed; dndMapperViewport.js's mousedown handler
            // only blocks left-button pan when mode != 'none'.
            if (_spaceHeld) return "none";
            if (_markupActive) return "markup";
            if (_focusActive) return "focus";
            if (IsFogPaintActive) return "fog";
            if (_rulerActive) return "ruler";
            return "none";
        }

        private async ValueTask PushJsMode()
        {
            if (_jsModules.Viewport is null) return;
            var next = CurrentJsMode();
            if (next == _currentJsMode) return;
            _currentJsMode = next;
            try { await _jsModules.Viewport.InvokeVoidAsync("setMode", _svgId, next); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "viewport.setMode failed."); }
        }

        private async ValueTask PushJsViewBox()
        {
            if (_jsModules.Viewport is null) return;
            try { await _jsModules.Viewport.InvokeVoidAsync("setViewBox", _svgId, _panX, _panY, _zoom); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "viewport.setViewBox failed."); }
        }

        private async ValueTask PushJsBounds()
        {
            if (_jsModules.Viewport is null || Map is null) return;
            try
            {
                await _jsModules.Viewport.InvokeVoidAsync(
                    "setBounds", _svgId,
                    Map.Grid.WidthCells, Map.Grid.HeightCells, Map.Grid.CellPixels);
            }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "viewport.setBounds failed."); }

            if (_jsModules.BitmapCanvas is not null)
            {
                try
                {
                    await _jsModules.BitmapCanvas.InvokeVoidAsync(
                        "setBounds", _canvasId,
                        Map.Grid.WidthCells, Map.Grid.HeightCells, Map.Grid.CellPixels);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex) { Logger.LogWarning(ex, "bitmapCanvas.setBounds failed."); }
            }
        }

        [JSInvokable]
        public async Task OnViewportChanged(double panX, double panY, double zoom, double centerX, double centerY, bool wasClickWithoutDrag)
        {
            _panX = panX;
            _panY = panY;
            _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            // JS owns the rail-aware center computation (it has direct access
            // to the stage rect + rail widths). Trust the value it sent and
            // publish it straight into the per-circuit viewport cache so
            // HostTokenPanel.SpawnAnchor reads the same world point the user
            // is actually looking at.
            if (Viewport is not null && Map is not null)
            {
                Viewport.Set(Map.Id, centerX, centerY);
            }
            if (wasClickWithoutDrag && IsHost && SelectedImageId is not null)
            {
                await SelectedImageIdChanged.InvokeAsync(null);
            }
            StateHasChanged();
        }

        internal string GetFogPathCached()
        {
            if (Map.Id != _cachedFogMapId || Map.FogVersion != _cachedFogVersion)
            {
                _cachedFogPath = FogPolygonBuilder.BuildSvgPathData(Map);
                _cachedFogVersion = Map.FogVersion;
                _cachedFogMapId = Map.Id;
            }
            return _cachedFogPath;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // The floating markup toolbar is rendered as a sibling of the engine and
            // takes the engine via @ref. @ref bindings happen during render commit, so
            // on the first render after _markupActive flips on, _markupEngine is null
            // and the toolbar block is skipped. Force a re-render once the ref binds
            // so the toolbar appears.
            if (_markupActive && _markupEngine is not null && !_markupToolbarRenderTriggered)
            {
                _markupToolbarRenderTriggered = true;
                StateHasChanged();
            }
            if (!_markupActive) _markupToolbarRenderTriggered = false;

            // Focus the frame on first render so Space/Shift work without the user clicking first.
            if (firstRender)
            {
                try { await _frameRef.FocusAsync(preventScroll: true); }
                catch { /* element not focusable yet — ignore */ }

                await _jsModules.LoadMetricsAsync();

                if (IsHost)
                {
                    await _jsModules.LoadFogPaintAsync(this);
                    await _jsModules.LoadFocusDragAsync(this);
                }

                // Load + initialise the bitmap canvas BEFORE the viewport so
                // viewport.initialize's first applyTransform can fan out a
                // setViewport call to a canvas that's already alive. (The
                // wiring between viewport and canvas is set up in task #8 —
                // viewport reads _jsModules.BitmapCanvas and calls into it.)
                await _jsModules.LoadBitmapCanvasAsync(this);
                if (_jsModules.BitmapCanvas is not null)
                {
                    try
                    {
                        await _jsModules.BitmapCanvas.InvokeVoidAsync(
                            "initialize", _canvasId, _jsModules.BitmapCanvasRef,
                            Map.Grid.WidthCells, Map.Grid.HeightCells, Map.Grid.CellPixels);
                        // Seed initial viewport so the canvas redraws at the
                        // correct pan/zoom before the viewport JS module
                        // takes over per-frame updates.
                        await _jsModules.BitmapCanvas.InvokeVoidAsync(
                            "setViewport", _canvasId, _panX, _panY, _zoom, Map.Grid.CellPixels);
                        await PushImagesToBitmapCanvas();
                    }
                    catch (JSDisconnectedException) { /* circuit teardown */ }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "BitmapCanvas initialize failed.");
                    }
                }

                await _jsModules.LoadViewportAsync(this);
                if (_jsModules.Viewport is not null && _jsModules.ViewportRef is not null)
                {
                    try
                    {
                        _currentJsMode = CurrentJsMode();
                        await _jsModules.Viewport.InvokeVoidAsync(
                            "initialize", _svgId, _jsModules.ViewportRef, _panX, _panY, _zoom,
                            Map.Grid.WidthCells, Map.Grid.HeightCells, Map.Grid.CellPixels, _currentJsMode,
                            // Bridge to the bitmap canvas — viewport calls
                            // setViewport on this module after every applyTransform
                            // so the canvas redraws synchronously without a .NET hop.
                            _jsModules.BitmapCanvas, _canvasId);
                        _lastBoundsSent = (Map.Id, Map.Grid.WidthCells, Map.Grid.HeightCells);
                    }
                    catch (JSDisconnectedException) { /* circuit teardown */ }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Viewport initialize failed.");
                    }
                }

                if (IsHost)
                {
                    await _jsModules.LoadImageDragAsync(this);
                    if (_jsModules.ImageDrag is not null && _jsModules.ImageDragRef is not null)
                    {
                        try
                        {
                            var payload = Map.Images
                                .Select(i => new { imageId = i.Id.ToString(), locked = i.Locked })
                                .ToArray();
                            await _jsModules.ImageDrag.InvokeVoidAsync(
                                "initialize", _svgId, _jsModules.ImageDragRef, payload, Map.Grid.CellPixels,
                                // Bridge to the bitmap canvas so the drag module
                                // calls setInFlightTransform per frame and the
                                // rendered image follows the gesture without a
                                // Blazor round-trip. Null-tolerant on the JS side.
                                _jsModules.BitmapCanvas, _canvasId);
                            // Seed snapshot so PushImagesToJs skips the next render's
                            // redundant marshal — we just pushed exactly this version.
                            _imageDragSnapshot = (Map.Id, Map.ImagesMembershipVersion);
                        }
                        catch (JSDisconnectedException) { /* circuit teardown */ }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Image-drag initialize failed.");
                        }
                    }
                }

                // Centre the wrapper inside the stage on first mount so the
                // map doesn't open clinging to the left edge. With the new
                // cursor-anchored wheel zoom and centre-anchored button zoom,
                // an off-centre initial pan no longer causes drift on zoom,
                // but starting centred is still the right first impression.
                try { await ResetView(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex) { Logger.LogWarning(ex, "Initial ResetView failed."); }
            }
            else if (_jsModules.Viewport is not null && Map is not null)
            {
                // Push new bounds on map swap or grid resize. Off-map content
                // is handled by the SVG's overflow="visible" attribute, not
                // by extending the viewBox, so ImagesVersion is not part of
                // this tuple.
                var current = (Map.Id, Map.Grid.WidthCells, Map.Grid.HeightCells);
                if (_lastBoundsSent != current)
                {
                    _lastBoundsSent = current;
                    await PushJsBounds();
                }
                if (IsHost) await PushImagesToJs();
                await PushImagesToBitmapCanvas();
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        public async ValueTask DisposeAsync()
        {
            TokenFocus.Focused -= OnTokenFocusRequested;
            FogContext.Changed -= OnFogContextChanged;
            _stateSub?.Dispose();
            await _jsModules.DisposeAsync();
            Dispose();
        }

        // ── Right-click context menu handlers ─────────────────────────────
        private async Task OnContextMenu(MouseEventArgs e)
        {
            if (!IsHost) return;

            // Ruler-tool override: right-click clears both points instead of
            // opening the context menu. The frame's
            // @oncontextmenu:preventDefault attribute already suppresses the
            // browser menu; returning here skips opening ours too.
            if (_rulerActive)
            {
                if (_rulerA is not null || _rulerB is not null)
                {
                    _rulerA = null;
                    _rulerB = null;
                    StateHasChanged();
                }
                return;
            }

            // Resolve the click into map-space coordinates so menu actions
            // (e.g. "centre everyone here") can use the targeted cell. Guard
            // Map because the active map can be cleared while ClientToMapAsync
            // awaits a JS round-trip.
            if (Map is null) return;
            var (mx, my) = await ClientToMapAsync(e.ClientX, e.ClientY);
            if (Map.Grid.SnapToGrid)
            {
                mx = Math.Floor(mx) + 0.5;
                my = Math.Floor(my) + 0.5;
            }

            _contextMenuMapX = mx;
            _contextMenuMapY = my;
            _contextMenuClientX = e.ClientX;
            _contextMenuClientY = e.ClientY;
            _contextMenuOpen = true;
        }

        private void CloseContextMenu() => _contextMenuOpen = false;

        private void RequestCenterEveryone()
        {
            _contextMenuOpen = false;
            var user = UserService.CurrentUser;
            if (user is null) return;
            Engine.RequestCenterViewportAsync(State, user, Map.Id, _contextMenuMapX, _contextMenuMapY);
        }

        private async Task<(double X, double Y)> ClientToMapAsync(double clientX, double clientY)
        {
            // Delegate to clientToSvgPoint (createSVGPoint + getScreenCTM().inverse()),
            // the same exact conversion path used by token-drag, image-drag, fog-paint,
            // and focus-drag. getScreenCTM walks every ancestor transform — including
            // the wrapper's pan/zoom and the rails' layout offset — so the result is
            // in SVG user-space (= grid cells, since the viewBox is denominated in
            // cells) with no further pan/rail adjustment needed.
            if (_jsModules.Metrics is null) return (_panX, _panY);
            try
            {
                var pt = await _jsModules.Metrics.InvokeAsync<SvgPoint?>(
                    "clientToSvgPoint", _svgId, clientX, clientY);
                if (pt is null) return (_panX, _panY);
                return (pt.X, pt.Y);
            }
            catch (JSDisconnectedException)
            {
                return (_panX, _panY);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "clientToSvgPoint failed; falling back to pan origin.");
                return (_panX, _panY);
            }
        }

        private sealed class SvgPoint
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        private static string F(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        // Three-figure ruler label: Chebyshev cells (D&D 5e default rule),
        // true Euclidean cells (helps players reason about diagonals), and
        // feet (Chebyshev × 5, the rulebook unit). Format keeps each piece
        // aligned with the existing toolbar/typography conventions.
        internal static string RulerLabel((double X, double Y) a, (double X, double Y) b)
        {
            var dx = Math.Abs(b.X - a.X);
            var dy = Math.Abs(b.Y - a.Y);
            var cheb = (int)Math.Round(Math.Max(dx, dy), MidpointRounding.AwayFromZero);
            var euc = Math.Sqrt(dx * dx + dy * dy);
            var ft = (int)(cheb * FeetPerSquare);
            return string.Create(CultureInfo.InvariantCulture,
                $"{cheb} sq · {euc:0.0} actual · {ft} ft");
        }

        // Cheap monospace-ish width estimate at the pill's 12px font size.
        // Returns half the pill width in user-space units (pixels because
        // the parent <g> inverse-scales by --dndm-inv-zoom). Doesn't need
        // to be exact — the rounded pill absorbs a few pixels of slop.
        internal static double EstimatePillHalfWidth(string label)
        {
            // ~6.5px per character at 12px Inter-ish + 12px side padding.
            return (label.Length * 6.5) / 2.0 + 12.0;
        }

        private void OnToggleGrid(ChangeEventArgs e) =>
            LocalShowGridLines = e.Value is bool b && b;

        // The +/- buttons route through the JS viewport so they use the same
        // anchor-at-non-rail-centre math as the cursor-anchored wheel zoom.
        private void ZoomIn() => _ = ZoomByFactorJs(1.25);
        private void ZoomOut() => _ = ZoomByFactorJs(1.0 / 1.25);

        private async ValueTask ZoomByFactorJs(double factor)
        {
            if (_jsModules.Viewport is null) return;
            try { await _jsModules.Viewport.InvokeVoidAsync("zoomByFactorAtCenter", _svgId, factor); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "zoomByFactorAtCenter failed."); }
        }

        private sealed class ViewportMetrics
        {
            public double SvgWidth { get; set; }
            public double SvgHeight { get; set; }
            public double LeftPx { get; set; }
            public double RightPx { get; set; }
        }

        private async Task ResetView()
        {
            // Fit the map into the visible region (stage minus left/right rail
            // widths). Rails overlay the canvas; without compensating, the map
            // would center behind them. JS round-trip is one frame; if metrics
            // aren't available, fall back to pan=0, zoom=1.
            //
            // New-architecture math: the wrapper's natural pixel size is
            // W*cellPx × H*cellPx (not stage-sized). fitZoom is the scale that
            // makes that natural box fit the visible stage area. panX/panY
            // (world cells at stage top-left) are then chosen to center the
            // map within the visible area.
            if (_jsModules.Metrics is not null)
            {
                try
                {
                    var m = await _jsModules.Metrics.InvokeAsync<ViewportMetrics?>("getViewportMetrics", _svgId);
                    if (m is not null && m.SvgWidth > 0 && m.SvgHeight > 0
                        && Map.Grid.WidthCells > 0 && Map.Grid.HeightCells > 0)
                    {
                        double stageW = m.SvgWidth, stageH = m.SvgHeight;
                        double L = m.LeftPx, R = m.RightPx;
                        double visibleW = Math.Max(1.0, stageW - L - R);
                        double visibleH = Math.Max(1.0, stageH);
                        double W = Map.Grid.WidthCells, H = Map.Grid.HeightCells;
                        double cellPx = Map.Grid.CellPixels;
                        double mapPxW = W * cellPx;
                        double mapPxH = H * cellPx;
                        double fitZoom = Math.Min(visibleW / mapPxW, visibleH / mapPxH);
                        fitZoom = Math.Clamp(fitZoom, MinZoom, MaxZoom);

                        // Visible window in world cells at fitZoom (uses full stage,
                        // including behind-rail area). Centering shifts the map so
                        // its visual center lands at the midpoint of the visible
                        // (non-rail) area: (L - R) / 2 stage-px away from stage
                        // center, converted to cells.
                        double visibleCellsW = stageW / (cellPx * fitZoom);
                        double visibleCellsH = stageH / (cellPx * fitZoom);
                        _zoom = fitZoom;
                        _panX = (W - visibleCellsW) / 2.0 - (L - R) / (2.0 * cellPx * fitZoom);
                        _panY = (H - visibleCellsH) / 2.0;
                        // setViewBox commits via JS, which fires OnViewportChanged
                        // back to .NET with the new rail-aware center.
                        await PushJsViewBox();
                        return;
                    }
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to measure viewport for ResetView; falling back.");
                }
            }
            _panX = 0;
            _panY = 0;
            _zoom = 1.0;
            await PushJsViewBox();
        }

        private void OnKeyDown(KeyboardEventArgs e)
        {
            if ((e.Key == " " || e.Code == "Space") && !_spaceHeld)
            {
                _spaceHeld = true;
                _ = PushJsMode();
            }
        }

        private void OnKeyUp(KeyboardEventArgs e)
        {
            if ((e.Key == " " || e.Code == "Space") && _spaceHeld)
            {
                _spaceHeld = false;
                _ = PushJsMode();
            }
        }

        private async Task OnSvgMouseDown(MouseEventArgs e)
        {
            // Middle-button always pans (owned by dndMapperViewport.js on the canvas-stage).
            // Any left-button event reaching the SVG (background rect, or a locked-image
            // picker whose pointer-events are off) is dispatched to focus-box or fog-paint
            // mode if active; otherwise it's a background click that the viewport module
            // turns into a deselect if no drag occurred. Picker/handle rects stopPropagation,
            // so legitimate image interactions don't reach here.
            if (e.Button != MiddleMouseButton && e.Button != LeftMouseButton) return;

            // Focus-box mode intercepts left-clicks before fog/pan (middle still pans).
            // The JS module owns the gesture entirely — preview is appended to the
            // SVG outside the Razor render tree and .NET is only invoked at
            // pointer-up via CommitFocusRect.
            if (e.Button == LeftMouseButton && IsHost && _focusActive && _jsModules.FocusDrag is not null && _jsModules.FocusDragRef is not null)
            {
                try
                {
                    await _jsModules.FocusDrag.InvokeVoidAsync(
                        "beginDrag", _svgId, _jsModules.FocusDragRef, LocalShowGridLines, e.ClientX, e.ClientY);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to start focus-box drag.");
                }
                return;
            }

            // Ruler mode intercepts left-clicks (middle still pans). Two
            // map-coord points fully describe the measurement; subsequent
            // clicks only move B (until right-click clears both).
            if (e.Button == LeftMouseButton && IsHost && _rulerActive)
            {
                var (mx, my) = await ClientToMapAsync(e.ClientX, e.ClientY);
                if (Map.Grid.SnapToGrid)
                {
                    mx = Math.Floor(mx) + 0.5;
                    my = Math.Floor(my) + 0.5;
                }
                if (_rulerA is null)
                {
                    _rulerA = (mx, my);
                }
                else
                {
                    // Once A is set, every further click replaces B until a
                    // right-click clears both.
                    _rulerB = (mx, my);
                }
                StateHasChanged();
                return;
            }

            // Fog paint/erase mode intercepts left-clicks (middle still pans).
            // The JS module handles the entire stroke client-side: it appends
            // its own preview <g> to the SVG and only calls back into .NET
            // once the host releases the pointer, with the full cell list.
            // That keeps the stroke snappy even when the SignalR round-trip
            // is slow.
            if (e.Button == LeftMouseButton && IsFogPaintActive && _jsModules.FogPaint is not null && _jsModules.FogPaintRef is not null)
            {
                var mode = FogContext.Mode == FogPaintMode.Paint ? "paint" : "erase";
                try
                {
                    await _jsModules.FogPaint.InvokeVoidAsync(
                        "beginStroke", _svgId, _jsModules.FogPaintRef, FogContext.BrushRadius, mode, e.ClientX, e.ClientY);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to start fog paint stroke.");
                }
                return;
            }

            // Background left/middle click: the JS viewport module owns pan now.
            // Just make sure subsequent move/up handlers don't try to act.
            await Task.CompletedTask;
        }

        [JSInvokable]
        public Task ApplyFogStroke(int[] xs, int[] ys, bool fogged)
        {
            if (!IsHost || UserService.CurrentUser is null) return Task.CompletedTask;
            if (xs is null || ys is null || xs.Length == 0 || xs.Length != ys.Length) return Task.CompletedTask;

            var cells = new (int cx, int cy)[xs.Length];
            for (var i = 0; i < xs.Length; i++) cells[i] = (xs[i], ys[i]);

            var result = Engine.PaintFogAsync(State, UserService.CurrentUser, Map.Id, cells, fogged);
            if (result.TryGetFailure(out var err))
                Logger.LogWarning("PaintFogAsync stroke failed: {Error}", err.PublicMessage);

            return Task.CompletedTask;
        }

        [JSInvokable]
        public Task CommitFocusRect(double x, double y, double w, double h)
        {
            if (!IsHost || UserService.CurrentUser is null) return Task.CompletedTask;
            if (w <= 0 || h <= 0) return Task.CompletedTask;
            var result = Engine.SetFocusRect(State, UserService.CurrentUser, Map.Id, x, y, w, h);
            if (result.TryGetFailure(out var err))
                Logger.LogDebug("SetFocusRect rejected: {Error}", err.PublicMessage);
            return Task.CompletedTask;
        }

        private void SnapDragToGrid(DragState d, bool snapBypass)
        {
            if (!Map.Grid.SnapToGrid) return;
            if (d.Kind == HandleKind.Rotate) return;
            // Hold Ctrl to bypass grid snap for this single move/resize (acts like
            // Shift for free aspect ratio — a temporary modifier, not persistent state).
            if (snapBypass) return;

            if (d.Kind == HandleKind.Body)
            {
                var (sx, sy) = SnapToGridHelper.SnapCorner(d.X, d.Y, Map.Grid);
                d.X = sx;
                d.Y = sy;
                return;
            }

            // Resize handles: snap both corners (anchor + drag corner), then recompute W/H
            // from the difference. Width/height stay positive because of the min-clamp in
            // ApplyResize.
            double anchorX = d.Kind is HandleKind.NE or HandleKind.SE ? d.OrigX : d.OrigX + d.OrigW;
            double anchorY = d.Kind is HandleKind.SW or HandleKind.SE ? d.OrigY : d.OrigY + d.OrigH;
            double dragCornerX = d.Kind is HandleKind.NE or HandleKind.SE ? d.X + d.W : d.X;
            double dragCornerY = d.Kind is HandleKind.SW or HandleKind.SE ? d.Y + d.H : d.Y;

            var (sAnchorX, sAnchorY) = SnapToGridHelper.SnapCorner(anchorX, anchorY, Map.Grid);
            var (sDragX, sDragY) = SnapToGridHelper.SnapCorner(dragCornerX, dragCornerY, Map.Grid);

            double newX = Math.Min(sAnchorX, sDragX);
            double newY = Math.Min(sAnchorY, sDragY);
            double newW = Math.Max(0.1, Math.Abs(sDragX - sAnchorX));
            double newH = Math.Max(0.1, Math.Abs(sDragY - sAnchorY));

            d.X = newX;
            d.Y = newY;
            d.W = newW;
            d.H = newH;
        }

        // ── JS-owned image drag (dndMapperImageDrag.js) ─────────────────────
        //
        // The JS module owns per-frame visual updates during body/resize/rotate
        // drag. It calls back into .NET only at drag-end so the engine can run
        // snap + UpdateImageTransformAsync inside the state Execute lock.
        //
        // The previous Blazor-server pattern (@onmousemove on the SVG with a
        // _drag draft + StateHasChanged() per move) round-tripped every pointer
        // event over SignalR and re-rendered the entire SVG, which caused
        // visible blocky artifacting and lag with large bitmap images.

        // Picker mousedown handles selection (left) and middle-click pan
        // forwarding. The stopPropagation modifier on the rect keeps the
        // SVG's @onmousedown (focus-box / fog-paint entry) from firing under
        // an image click, but it also stops the viewport JS module's pan
        // listener from seeing middle-click — so we forward to it manually.
        private async Task OnPickerMouseDown(MouseEventArgs e, MapImage img)
        {
            if (e.Button == MiddleMouseButton)
            {
                await ForceBeginPanAsync(e);
                return;
            }
            if (!IsHost || e.Button != LeftMouseButton || img.Locked) return;
            if (SelectedImageId != img.Id)
                await SelectedImageIdChanged.InvokeAsync(img.Id);
        }

        // Handles only need to swallow middle-click → pan forwarding and
        // block the SVG's @onmousedown via the stopPropagation modifier; the
        // JS module owns the actual resize/rotate drag.
        private async Task OnHandleMouseDown(MouseEventArgs e)
        {
            if (e.Button == MiddleMouseButton) await ForceBeginPanAsync(e);
        }

        private async Task ForceBeginPanAsync(MouseEventArgs e)
        {
            if (_jsModules.Viewport is null) return;
            try { await _jsModules.Viewport.InvokeVoidAsync("forceBeginPan", _svgId, e.ClientX, e.ClientY, e.Button); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "viewport.forceBeginPan failed."); }
        }

        [JSInvokable]
        public Task OnImageDragEnd(
            Guid imageId, string kind,
            double origX, double origY, double origW, double origH, double origRot,
            double newX, double newY, double newW, double newH, double newRot,
            bool snapBypass, bool freeAspect)
        {
            if (!IsHost || UserService.CurrentUser is null) return Task.CompletedTask;
            var image = Map.Images.FirstOrDefault(i => i.Id == imageId);
            if (image is null || image.Locked) return Task.CompletedTask;

            var handleKind = ParseHandleKind(kind);
            var drag = new DragState
            {
                ImageId = imageId,
                Kind = handleKind,
                OrigX = origX, OrigY = origY, OrigW = origW, OrigH = origH, OrigRot = origRot,
                X = newX, Y = newY, W = newW, H = newH, Rot = newRot,
            };

            SnapDragToGrid(drag, snapBypass);

            var w = Math.Max(0.1, drag.W);
            var h = Math.Max(0.1, drag.H);
            var op = image.Opacity;

            var result = Engine.UpdateImageTransformAsync(
                State, UserService.CurrentUser, Map.Id, imageId,
                drag.X, drag.Y, w, h, drag.Rot, op);

            if (result.TryGetFailure(out var err))
            {
                Logger.LogWarning("UpdateImageTransformAsync rejected: {Error}", err.PublicMessage);
                // Revert the JS preview to canonical so the user sees the rejected
                // state instead of a stale drag-end position.
                _ = ReconcileImageJs(image);
            }
            else
            {
                // Snap may have landed on canonical-equal values (Blazor diff skips
                // the DOM write); push the authoritative transform back to JS so
                // the preview snaps to the snapped position rather than the
                // unsnapped drag-end position.
                _ = ReconcileImageJs(imageId, drag.X, drag.Y, w, h, drag.Rot);
            }

            return Task.CompletedTask;
        }

        private static HandleKind ParseHandleKind(string kind) => kind switch
        {
            "nw" => HandleKind.NW,
            "ne" => HandleKind.NE,
            "sw" => HandleKind.SW,
            "se" => HandleKind.SE,
            "rot" => HandleKind.Rotate,
            _ => HandleKind.Body,
        };

        private async ValueTask PushImagesToJs()
        {
            if (_jsModules.ImageDrag is null) return;
            // Engine bumps Map.ImagesMembershipVersion on add/remove/lock only,
            // so pure transform edits never marshal a payload. Map-swap is
            // detected by the MapId in the snapshot tuple.
            var current = (Map.Id, Map.ImagesMembershipVersion);
            if (_imageDragSnapshot == current) return;
            _imageDragSnapshot = current;

            var payload = Map.Images
                .Select(i => new { imageId = i.Id.ToString(), locked = i.Locked })
                .ToArray();
            try { await _jsModules.ImageDrag.InvokeVoidAsync("setImages", _svgId, payload, Map.Grid.CellPixels); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "imageDrag.setImages failed."); }
        }

        // Marshals the full image set (transforms included) into the bitmap
        // canvas JS module. Triggered on first render and whenever Map.ImagesVersion
        // bumps OR when _localImageUrlsVersion changes (a freshly resolved blob
        // URL needs to land in the canvas as a new src). Membership-only
        // changes still need this — the canvas redraws lower-z-order images
        // under newly added ones.
        private async ValueTask PushImagesToBitmapCanvas()
        {
            if (_jsModules.BitmapCanvas is null) return;
            var current = (Map.Id, Map.ImagesVersion, _localImageUrlsVersion);
            if (_bitmapCanvasSnapshot == current) return;
            _bitmapCanvasSnapshot = current;

            var payload = Map.Images
                .Where(i => !i.Hidden)
                .Select(i => new
                {
                    id = i.Id.ToString(),
                    src = ResolveImageSrc(i),
                    x = i.X,
                    y = i.Y,
                    width = i.Width,
                    height = i.Height,
                    rotation = i.Rotation,
                    opacity = i.Opacity,
                    layerOrder = i.LayerOrder,
                    hidden = i.Hidden,
                })
                .ToArray();
            try { await _jsModules.BitmapCanvas.InvokeVoidAsync("setImages", _canvasId, payload); }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "bitmapCanvas.setImages failed."); }
        }

        private async ValueTask ReconcileImageJs(MapImage image)
        {
            await ReconcileImageJs(image.Id, image.X, image.Y, image.Width, image.Height, image.Rotation);
        }

        private async ValueTask ReconcileImageJs(Guid imageId, double x, double y, double w, double h, double rot)
        {
            if (_jsModules.ImageDrag is null) return;
            try
            {
                await _jsModules.ImageDrag.InvokeVoidAsync(
                    "reconcileImage", _svgId, imageId.ToString(), x, y, w, h, rot);
            }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex) { Logger.LogWarning(ex, "imageDrag.reconcileImage failed."); }
        }
    }
}
