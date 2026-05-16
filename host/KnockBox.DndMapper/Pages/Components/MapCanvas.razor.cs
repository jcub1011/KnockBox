using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services;
using KnockBox.DndMapper.Services.Logic.Games;
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

        protected override void OnInitialized()
        {
            TokenFocus.Focused += OnTokenFocusRequested;
            base.OnInitialized();
        }

        private async void OnTokenFocusRequested(Guid tokenId)
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
            if (_metricsModule is not null)
            {
                try { await _metricsModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                _metricsModule = null;
            }
            Dispose();
        }

        private static string F(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private void OnToggleGrid(ChangeEventArgs e) =>
            LocalShowGridLines = e.Value is bool b && b;

        private void ZoomIn() => SetZoom(_zoom * 1.25);
        private void ZoomOut() => SetZoom(_zoom / 1.25);

        private void ResetView()
        {
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
            BeginPan(e);
            await CaptureScaleAsync();
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
