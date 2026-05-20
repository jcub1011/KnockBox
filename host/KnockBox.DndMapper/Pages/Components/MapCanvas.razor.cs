using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services;
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

        [CascadingParameter] public DndMapperViewport? Viewport { get; set; }

        private const double MinZoom = 0.10;
        private const double MaxZoom = 10.0;
        private const int LeftMouseButton = 0;
        private const int MiddleMouseButton = 1;
        // Pixel distance below which a left-button "pan" is treated as a click (= deselect).
        private const double ClickDeadZonePixels = 3.0;

        private readonly string _svgId = $"dndm-svg-{Guid.NewGuid():N}";
        private ElementReference _frameRef;

        private double _panX;
        private double _panY;
        private double _zoom = 1.0;
        private bool _panning;
        private bool _panMoved;
        private long _panButton;
        private double _panLastClientX;
        private double _panLastClientY;
        private double _panStartClientX;
        private double _panStartClientY;

        private bool _spaceHeld;
        private bool _shiftHeld;
        private bool _ctrlHeld;

        private bool LocalShowGridLines { get; set; } = true;
        private bool _gridInitialized;

        // Real on-screen scale (CSS pixels per cell). Captured from the SVG's
        // getScreenCTM at the start of a drag/pan; 0 = not yet measured (fallback to
        // CellPixels*zoom). Cached for the duration of the gesture and cleared on mouse-up.
        private double _pxPerCell;
        private IJSObjectReference? _metricsModule;
        private IJSObjectReference? _fogPaintModule;
        private DotNetObjectReference<MapCanvas>? _fogPaintRef;

        // ── Image transform drag state ───────────────────────────────────
        internal enum HandleKind { Body, NW, NE, SW, SE, Rotate }

        internal sealed class DragState
        {
            public Guid ImageId;
            public HandleKind Kind;
            public double StartClientX;
            public double StartClientY;
            public double OrigX, OrigY, OrigW, OrigH, OrigRot;
            public double X, Y, W, H, Rot;
        }

        private DragState? _drag;

        private MapImage? SelectedImage =>
            SelectedImageId is Guid id ? Map.Images.FirstOrDefault(i => i.Id == id) : null;

        private string ViewBoxString
        {
            get
            {
                double w = Map.Grid.WidthCells / _zoom;
                double h = Map.Grid.HeightCells / _zoom;
                return string.Create(CultureInfo.InvariantCulture,
                    $"{_panX} {_panY} {w} {h}");
            }
        }

        protected override void OnParametersSet()
        {
            if (!_gridInitialized && Map is not null)
            {
                LocalShowGridLines = Map.Grid.ShowGridLines;
                _gridInitialized = true;
            }
            PublishViewport();
            base.OnParametersSet();
        }

        // ── Markup overlay (v1.x — §5.6) ─────────────────────────────────
        // Host-only toggle: when true, an interactive SvgDrawingCanvas covers the
        // map. The read-only saved markup is rendered inline on the SVG so pan
        // and zoom apply uniformly for everyone.
        private bool _markupActive;
        internal bool MarkupActive => _markupActive;

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
            TokenFocus.Focused += OnTokenFocusRequested;
            _stateSub = State.StateChangedEventManager.Subscribe(OnStateChanged);
            FogContext.Changed += OnFogContextChanged;
            // Seed the "last seen" nonce so an existing request from before the
            // page loaded doesn't snap the viewport on first render.
            _lastSeenCenterNonce = State.PendingCenterRequest?.Nonce;
            base.OnInitialized();
        }

        private void OnFogContextChanged() => InvokeAsync(StateHasChanged);

        private bool IsFogPaintActive => IsHost && FogContext.Mode != FogPaintMode.Off;

        private async ValueTask OnStateChanged()
        {
            // Reactor for the host's "centre everyone here" broadcast. Compare
            // against the last-seen nonce so identical successive requests still
            // re-centre (the engine issues a fresh Guid on every verb call).
            var req = State.PendingCenterRequest;
            if (req is not null && req.Nonce != _lastSeenCenterNonce && req.MapId == Map.Id)
            {
                _lastSeenCenterNonce = req.Nonce;
                double viewW = Map.Grid.WidthCells / _zoom;
                double viewH = Map.Grid.HeightCells / _zoom;
                _panX = req.X - viewW / 2.0;
                _panY = req.Y - viewH / 2.0;
                PublishViewport();
            }
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
            }
        }

        private async void OnTokenFocusRequested(Guid tokenId)
        {
            // `async void` because the underlying event delegate is
            // `Action<Guid>` (synchronous); any exception thrown after the
            // first await would otherwise escape into the sync context and
            // crash the Blazor circuit. Catch everything here and log.
            try
            {
                if (Map is null) return;
                var token = Map.Tokens.FirstOrDefault(t => t.Id == tokenId);
                if (token is null) return;
                double viewW = Map.Grid.WidthCells / _zoom;
                double viewH = Map.Grid.HeightCells / _zoom;
                _panX = token.X - viewW / 2.0;
                _panY = token.Y - viewH / 2.0;
                PublishViewport();
                await InvokeAsync(StateHasChanged);
            }
            catch (ObjectDisposedException) { /* component disposed */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to focus token {TokenId}.", tokenId);
            }
        }

        private void PublishViewport()
        {
            if (Viewport is null || Map is null) return;
            double viewW = Map.Grid.WidthCells / _zoom;
            double viewH = Map.Grid.HeightCells / _zoom;
            Viewport.Set(Map.Id, _panX + viewW / 2.0, _panY + viewH / 2.0);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // Focus the frame on first render so Space/Shift work without the user clicking first.
            if (firstRender)
            {
                try { await _frameRef.FocusAsync(preventScroll: true); }
                catch { /* element not focusable yet — ignore */ }

                try
                {
                    _metricsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./_content/KnockBox.DndMapper/js/dndMapperSvgMetrics.js");
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to load SVG metrics JS module.");
                }

                if (IsHost)
                {
                    try
                    {
                        _fogPaintModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                            "import", "./_content/KnockBox.DndMapper/js/dndMapperFogPaint.js");
                        _fogPaintRef = DotNetObjectReference.Create(this);
                    }
                    catch (JSDisconnectedException) { /* circuit teardown */ }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to load fog-paint JS module.");
                    }
                }
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        private async ValueTask CaptureScaleAsync()
        {
            if (_metricsModule is null) return;
            try
            {
                double scale = await _metricsModule.InvokeAsync<double>("getPixelsPerCell", _svgId);
                if (scale > 0) _pxPerCell = scale;
            }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to read SVG screen scale; falling back to CellPixels*zoom.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            TokenFocus.Focused -= OnTokenFocusRequested;
            FogContext.Changed -= OnFogContextChanged;
            _stateSub?.Dispose();
            if (_fogPaintModule is not null)
            {
                try { await _fogPaintModule.InvokeVoidAsync("cancelStroke"); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
                try { await _fogPaintModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                _fogPaintModule = null;
            }
            _fogPaintRef?.Dispose();
            _fogPaintRef = null;
            if (_metricsModule is not null)
            {
                try { await _metricsModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                _metricsModule = null;
            }
            Dispose();
        }

        // ── Right-click context menu handlers ─────────────────────────────
        private async Task OnContextMenu(MouseEventArgs e)
        {
            if (!IsHost) return;

            // Resolve the click into map-space coordinates so menu actions
            // (e.g. "centre everyone here") can use the targeted cell.
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
            // The SVG viewBox already maps screen pixels into grid-cell units;
            // recover the cell coords from the cached pixel scale + current pan.
            await CaptureScaleAsync();
            if (_pxPerCell <= 0) return (0, 0);
            try
            {
                if (_metricsModule is null) return (_panX, _panY);
                var rect = await _metricsModule.InvokeAsync<ViewportMetrics?>("getViewportMetrics", _svgId);
                if (rect is null || rect.SvgWidth == 0) return (_panX, _panY);
                // getViewportMetrics returns the SVG box dimensions in client px;
                // the SVG itself is offset by getBoundingClientRect.left/top, which
                // we don't have here. Approximate by using the difference between
                // client position and the metrics' "left" offset (rails width). For
                // M01 ship this is good enough for the centre-viewport affordance.
                double localX = clientX - rect.LeftPx;
                double localY = clientY;
                double mapX = _panX + localX / _pxPerCell;
                double mapY = _panY + localY / _pxPerCell;
                return (mapX, mapY);
            }
            catch
            {
                return (_panX, _panY);
            }
        }

        private static string F(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private void OnToggleGrid(ChangeEventArgs e) =>
            LocalShowGridLines = e.Value is bool b && b;

        private void ZoomIn() => SetZoom(_zoom * 1.25);
        private void ZoomOut() => SetZoom(_zoom / 1.25);

        private sealed class ViewportMetrics
        {
            public double SvgWidth { get; set; }
            public double SvgHeight { get; set; }
            public double LeftPx { get; set; }
            public double RightPx { get; set; }
        }

        private async Task ResetView()
        {
            // Fit the map into the visible region (full SVG minus the left/right
            // rail widths). Rails overlay the canvas, so without this offset the
            // map would center under the rails. JS round-trip is one frame; if
            // metrics aren't available, fall back to plain pan=0, zoom=1.
            if (_metricsModule is not null)
            {
                try
                {
                    var m = await _metricsModule.InvokeAsync<ViewportMetrics?>("getViewportMetrics", _svgId);
                    if (m is not null && m.SvgWidth > 0 && m.SvgHeight > 0
                        && Map.Grid.WidthCells > 0 && Map.Grid.HeightCells > 0)
                    {
                        double pxW = m.SvgWidth, pxH = m.SvgHeight;
                        double L = m.LeftPx, R = m.RightPx;
                        double vw = Math.Max(1.0, pxW - L - R);
                        double vh = Math.Max(1.0, pxH);
                        double W = Map.Grid.WidthCells, H = Map.Grid.HeightCells;
                        // Pixels-per-cell at zoom=1 under xMidYMid meet.
                        double basePxPerCell = Math.Min(pxW / W, pxH / H);
                        double mapPxW = basePxPerCell * W;
                        double mapPxH = basePxPerCell * H;
                        double fitZoom = Math.Min(vw / mapPxW, vh / mapPxH);
                        fitZoom = Math.Clamp(fitZoom, MinZoom, MaxZoom);
                        // viewBox spans (W/fitZoom, H/fitZoom) world units. Under
                        // xMidYMid meet, the viewBox CENTER (not the map center)
                        // is what lands at the SVG pixel center. So to put the
                        // map center at SVG center we need panX = (W - VW)/2;
                        // then shift further by (R - L)/(2·s) to recenter into
                        // the visible-area midpoint between the two rails.
                        double VW = W / fitZoom;
                        double VH = H / fitZoom;
                        double scale = fitZoom * basePxPerCell;
                        double dxPx = (L - R) / 2.0;
                        _zoom = fitZoom;
                        _panX = (W - VW) / 2.0 - dxPx / scale;
                        _panY = (H - VH) / 2.0;
                        PublishViewport();
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
            PublishViewport();
        }

        private void SetZoom(double next)
        {
            next = Math.Clamp(next, MinZoom, MaxZoom);
            double oldW = Map.Grid.WidthCells / _zoom;
            double oldH = Map.Grid.HeightCells / _zoom;
            double newW = Map.Grid.WidthCells / next;
            double newH = Map.Grid.HeightCells / next;
            _panX += (oldW - newW) / 2.0;
            _panY += (oldH - newH) / 2.0;
            _zoom = next;
            ClampPan();
            PublishViewport();
        }

        private void ClampPan()
        {
            // Pan is unbounded by design — the host frequently needs to scroll past
            // grid edges (off-map sticky notes, secondary scenes). ResetView still
            // re-centers on (0, 0).
        }

        private void OnWheel(WheelEventArgs e)
        {
            double factor = e.DeltaY < 0 ? 1.1 : 1.0 / 1.1;
            SetZoom(_zoom * factor);
        }

        private void OnKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == " " || e.Code == "Space") _spaceHeld = true;
            if (e.Key == "Shift") _shiftHeld = true;
            if (e.Key == "Control") _ctrlHeld = true;
        }

        private void OnKeyUp(KeyboardEventArgs e)
        {
            if (e.Key == " " || e.Code == "Space") _spaceHeld = false;
            if (e.Key == "Shift") _shiftHeld = false;
            if (e.Key == "Control") _ctrlHeld = false;
        }

        private async Task OnSvgMouseDown(MouseEventArgs e)
        {
            // Middle-button always pans. Any left-button event reaching the SVG (background
            // rect, or a locked-image picker whose pointer-events are off) starts a "pan"
            // that turns into a deselect on mouse-up if the cursor never moved beyond
            // ClickDeadZonePixels (see OnSvgMouseUp). Picker/handle rects stopPropagation,
            // so legitimate image interactions don't reach here.
            if (e.Button != MiddleMouseButton && e.Button != LeftMouseButton) return;

            // Fog paint/erase mode intercepts left-clicks (middle still pans).
            // The JS module takes over pointermove/pointerup for the duration of
            // the stroke and flushes cells back via FlushFogStroke.
            if (e.Button == LeftMouseButton && IsFogPaintActive && _fogPaintModule is not null && _fogPaintRef is not null)
            {
                try
                {
                    await _fogPaintModule.InvokeVoidAsync(
                        "beginStroke", _svgId, _fogPaintRef, FogContext.BrushRadius, e.ClientX, e.ClientY);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to start fog paint stroke.");
                }
                return;
            }

            BeginPan(e);
            await CaptureScaleAsync();
        }

        [JSInvokable]
        public Task FlushFogStroke(int[] xs, int[] ys)
        {
            if (!IsHost || UserService.CurrentUser is null) return Task.CompletedTask;
            if (xs is null || ys is null || xs.Length == 0 || xs.Length != ys.Length) return Task.CompletedTask;
            if (FogContext.Mode == FogPaintMode.Off) return Task.CompletedTask;

            var cells = new (int cx, int cy)[xs.Length];
            for (var i = 0; i < xs.Length; i++) cells[i] = (xs[i], ys[i]);

            var result = Engine.PaintFogAsync(
                State, UserService.CurrentUser, Map.Id, cells,
                fogged: FogContext.Mode == FogPaintMode.Paint);

            if (result.TryGetFailure(out var err))
                Logger.LogWarning("PaintFogAsync flush failed: {Error}", err.PublicMessage);

            return Task.CompletedTask;
        }

        private void BeginPan(MouseEventArgs e)
        {
            _panning = true;
            _panButton = e.Button;
            _panMoved = false;
            _panLastClientX = e.ClientX;
            _panLastClientY = e.ClientY;
            _panStartClientX = e.ClientX;
            _panStartClientY = e.ClientY;
        }

        private async Task OnSvgMouseMove(MouseEventArgs e)
        {
            if (_panning)
            {
                double dx = e.ClientX - _panLastClientX;
                double dy = e.ClientY - _panLastClientY;
                _panLastClientX = e.ClientX;
                _panLastClientY = e.ClientY;

                double totalDx = e.ClientX - _panStartClientX;
                double totalDy = e.ClientY - _panStartClientY;
                if (Math.Abs(totalDx) > ClickDeadZonePixels || Math.Abs(totalDy) > ClickDeadZonePixels)
                    _panMoved = true;

                double pxPerCell = ActualPxPerCell();
                _panX -= dx / pxPerCell;
                _panY -= dy / pxPerCell;
                ClampPan();
                PublishViewport();
                return;
            }

            if (_drag is { } d)
            {
                var (cdx, cdy) = ClientDeltaToCells(e.ClientX - d.StartClientX, e.ClientY - d.StartClientY);
                ApplyDragDelta(d, cdx, cdy, _shiftHeld);
                StateHasChanged();
            }

            await Task.CompletedTask;
        }

        private async Task OnSvgMouseUp(MouseEventArgs e)
        {
            // Pan finalize: if the left-button "pan" never moved, treat it as a background
            // click → deselect any selected image.
            if (_panning)
            {
                bool wasLeftClickWithoutDrag =
                    _panButton == LeftMouseButton && !_panMoved && !_spaceHeld;
                _panning = false;
                _panMoved = false;
                if (wasLeftClickWithoutDrag && IsHost && SelectedImageId is not null)
                {
                    await SelectedImageIdChanged.InvokeAsync(null);
                }
            }

            if (_drag is { } d)
            {
                if (UserService.CurrentUser is not null)
                {
                    SnapDragToGrid(d);
                    var w = Math.Max(0.1, d.W);
                    var h = Math.Max(0.1, d.H);
                    var op = SelectedImage?.Opacity ?? 1.0;
                    Engine.UpdateImageTransformAsync(
                        State, UserService.CurrentUser, Map.Id, d.ImageId,
                        d.X, d.Y, w, h, d.Rot, op);
                }
                _drag = null;
                StateHasChanged();
            }

            // Invalidate the cached scale so the next gesture re-measures (handles
            // window resize / layout shifts between gestures).
            _pxPerCell = 0;
            await Task.CompletedTask;
        }

        private (double dx, double dy) ClientDeltaToCells(double pxDx, double pxDy)
        {
            double pxPerCell = ActualPxPerCell();
            return (pxDx / pxPerCell, pxDy / pxPerCell);
        }

        // Real on-screen scale captured from getScreenCTM at gesture start; if that
        // hasn't run yet (or JS interop failed) fall back to the configured CellPixels
        // adjusted for the current zoom — which matches the old behavior.
        private double ActualPxPerCell() =>
            _pxPerCell > 0 ? _pxPerCell : Math.Max(1, Map.Grid.CellPixels * _zoom);

        private async Task OnImageMouseDown(MouseEventArgs e, MapImage img)
        {
            // Picker rect stopPropagation swallows mousedown for any button, so the SVG
            // pan handler never sees a middle-click landing on an image. Start the pan
            // directly here instead.
            if (e.Button == MiddleMouseButton) { BeginPan(e); await CaptureScaleAsync(); return; }
            if (!IsHost || e.Button != LeftMouseButton) return;
            // Space-held pan takes precedence — let the SVG handler pan instead.
            if (_spaceHeld) return;
            // Locked images do not select or drag; the event bubbles to the SVG which
            // will start a pan in OnSvgMouseDown.
            if (img.Locked) return;
            if (SelectedImageId != img.Id)
            {
                await SelectedImageIdChanged.InvokeAsync(img.Id);
            }
            _drag = NewDrag(e, img, HandleKind.Body);
            await CaptureScaleAsync();
        }

        private async Task OnHandleMouseDown(MouseEventArgs e, MapImage img, HandleKind kind)
        {
            if (e.Button == MiddleMouseButton) { BeginPan(e); await CaptureScaleAsync(); return; }
            if (!IsHost || e.Button != LeftMouseButton) return;
            if (img.Locked) return;
            _drag = NewDrag(e, img, kind);
            await CaptureScaleAsync();
        }

        private static DragState NewDrag(MouseEventArgs e, MapImage img, HandleKind kind) => new()
        {
            ImageId = img.Id,
            Kind = kind,
            StartClientX = e.ClientX,
            StartClientY = e.ClientY,
            OrigX = img.X,
            OrigY = img.Y,
            OrigW = img.Width,
            OrigH = img.Height,
            OrigRot = img.Rotation,
            X = img.X,
            Y = img.Y,
            W = img.Width,
            H = img.Height,
            Rot = img.Rotation,
        };

        private static void ApplyDragDelta(DragState d, double dx, double dy, bool freeAspect)
        {
            switch (d.Kind)
            {
                case HandleKind.Body:
                    d.X = d.OrigX + dx;
                    d.Y = d.OrigY + dy;
                    break;
                case HandleKind.NW:
                case HandleKind.NE:
                case HandleKind.SW:
                case HandleKind.SE:
                    ApplyResize(d, dx, dy, freeAspect);
                    break;
                case HandleKind.Rotate:
                    // Rotate handle starts above the image center; treat the start
                    // direction as -Y and derive a relative angle from the cursor delta.
                    double startAngle = Math.Atan2(-1, 0);
                    double currentAngle = Math.Atan2(-1 + dy, dx);
                    double deg = (currentAngle - startAngle) * 180.0 / Math.PI;
                    d.Rot = (d.OrigRot + deg) % 360.0;
                    break;
            }
        }

        internal static void ApplyResize(DragState d, double dx, double dy, bool freeAspect)
        {
            // Direction signs: which corner is being dragged relative to the image origin (NW).
            // east = true for NE/SE (drag column = right edge); south = true for SW/SE.
            bool east = d.Kind is HandleKind.NE or HandleKind.SE;
            bool south = d.Kind is HandleKind.SW or HandleKind.SE;

            // Raw new dimensions, free-form.
            double rawW = east ? d.OrigW + dx : d.OrigW - dx;
            double rawH = south ? d.OrigH + dy : d.OrigH - dy;

            double minDim = 0.1;
            rawW = Math.Max(minDim, rawW);
            rawH = Math.Max(minDim, rawH);

            double newW = rawW;
            double newH = rawH;

            if (!freeAspect && d.OrigW > 0 && d.OrigH > 0)
            {
                // Aspect-locked: drive both axes from whichever scale moved further from 1.0.
                // Using Math.Max would bias toward growth — if the user shrinks one axis while
                // the other is near-unchanged, the unchanged axis (scale ≈ 1) would win and
                // the image would grow instead of shrink.
                double scaleW = rawW / d.OrigW;
                double scaleH = rawH / d.OrigH;
                double scale = Math.Abs(scaleW - 1.0) >= Math.Abs(scaleH - 1.0) ? scaleW : scaleH;
                newW = Math.Max(minDim, d.OrigW * scale);
                newH = Math.Max(minDim, d.OrigH * scale);
            }

            d.W = newW;
            d.H = newH;
            // Reposition anchored corner so the opposite corner (the fixed one) stays put.
            d.X = east ? d.OrigX : d.OrigX + (d.OrigW - newW);
            d.Y = south ? d.OrigY : d.OrigY + (d.OrigH - newH);
        }

        private void SnapDragToGrid(DragState d)
        {
            if (!Map.Grid.SnapToGrid) return;
            if (d.Kind == HandleKind.Rotate) return;
            // Hold Ctrl to bypass grid snap for this single move/resize (acts like
            // Shift for free aspect ratio — a temporary modifier, not persistent state).
            if (_ctrlHeld) return;

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
    }
}
