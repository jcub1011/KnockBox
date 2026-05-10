using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class MapCanvas : DisposableComponent
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

        private const double MinZoom = 0.25;
        private const double MaxZoom = 4.0;
        private const int LeftMouseButton = 0;
        private const int MiddleMouseButton = 1;

        private readonly string _svgId = $"dndm-svg-{Guid.NewGuid():N}";

        private double _panX;
        private double _panY;
        private double _zoom = 1.0;
        private bool _panning;
        private double _panLastClientX;
        private double _panLastClientY;

        private bool LocalShowGridLines { get; set; } = true;
        private bool _gridInitialized;

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
            base.OnParametersSet();
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
        }

        private void ClampPan()
        {
            double viewW = Map.Grid.WidthCells / _zoom;
            double viewH = Map.Grid.HeightCells / _zoom;
            double maxPanX = Math.Max(0, Map.Grid.WidthCells - viewW);
            double maxPanY = Math.Max(0, Map.Grid.HeightCells - viewH);
            _panX = Math.Clamp(_panX, -viewW * 0.25, maxPanX + viewW * 0.25);
            _panY = Math.Clamp(_panY, -viewH * 0.25, maxPanY + viewH * 0.25);
        }

        private void OnWheel(WheelEventArgs e)
        {
            double factor = e.DeltaY < 0 ? 1.1 : 1.0 / 1.1;
            SetZoom(_zoom * factor);
        }

        private void OnSvgMouseDown(MouseEventArgs e)
        {
            if (e.Button != MiddleMouseButton) return;
            _panning = true;
            _panLastClientX = e.ClientX;
            _panLastClientY = e.ClientY;
        }

        private async Task OnSvgMouseMove(MouseEventArgs e)
        {
            if (_panning)
            {
                double dx = e.ClientX - _panLastClientX;
                double dy = e.ClientY - _panLastClientY;
                _panLastClientX = e.ClientX;
                _panLastClientY = e.ClientY;
                double pixelsPerCell = Math.Max(1, Map.Grid.CellPixels);
                _panX -= dx / (pixelsPerCell * _zoom);
                _panY -= dy / (pixelsPerCell * _zoom);
                ClampPan();
                return;
            }

            if (_drag is { } d)
            {
                var (cdx, cdy) = ClientDeltaToCells(e.ClientX - d.StartClientX, e.ClientY - d.StartClientY);
                ApplyDragDelta(d, cdx, cdy);
                StateHasChanged();
            }

            await Task.CompletedTask;
        }

        private async Task OnSvgMouseUp(MouseEventArgs e)
        {
            _panning = false;
            if (_drag is { } d)
            {
                if (UserService.CurrentUser is not null)
                {
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
            await Task.CompletedTask;
        }

        private (double dx, double dy) ClientDeltaToCells(double pxDx, double pxDy)
        {
            double pixelsPerCell = Math.Max(1, Map.Grid.CellPixels);
            return (pxDx / (pixelsPerCell * _zoom), pxDy / (pixelsPerCell * _zoom));
        }

        private async Task OnImageMouseDown(MouseEventArgs e, MapImage img)
        {
            if (!IsHost || e.Button != LeftMouseButton) return;
            if (SelectedImageId != img.Id)
            {
                await SelectedImageIdChanged.InvokeAsync(img.Id);
            }
            _drag = NewDrag(e, img, HandleKind.Body);
        }

        private void OnHandleMouseDown(MouseEventArgs e, MapImage img, HandleKind kind)
        {
            if (!IsHost || e.Button != LeftMouseButton) return;
            _drag = NewDrag(e, img, kind);
        }

        private async Task OnBackgroundClick(MouseEventArgs e)
        {
            if (!IsHost) return;
            if (SelectedImageId is not null)
            {
                await SelectedImageIdChanged.InvokeAsync(null);
            }
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

        private static void ApplyDragDelta(DragState d, double dx, double dy)
        {
            switch (d.Kind)
            {
                case HandleKind.Body:
                    d.X = d.OrigX + dx;
                    d.Y = d.OrigY + dy;
                    break;
                case HandleKind.NW:
                    d.X = d.OrigX + dx;
                    d.Y = d.OrigY + dy;
                    d.W = Math.Max(0.1, d.OrigW - dx);
                    d.H = Math.Max(0.1, d.OrigH - dy);
                    break;
                case HandleKind.NE:
                    d.Y = d.OrigY + dy;
                    d.W = Math.Max(0.1, d.OrigW + dx);
                    d.H = Math.Max(0.1, d.OrigH - dy);
                    break;
                case HandleKind.SW:
                    d.X = d.OrigX + dx;
                    d.W = Math.Max(0.1, d.OrigW - dx);
                    d.H = Math.Max(0.1, d.OrigH + dy);
                    break;
                case HandleKind.SE:
                    d.W = Math.Max(0.1, d.OrigW + dx);
                    d.H = Math.Max(0.1, d.OrigH + dy);
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
    }
}
