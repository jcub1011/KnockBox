using System.Globalization;
using KnockBox.Core.Components.Shared;
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

        private const double MinZoom = 0.25;
        private const double MaxZoom = 4.0;
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
            // Zoom about the viewBox center to keep the visible area roughly centered.
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
            // Middle-mouse pan only — left mouse is reserved for token drag (handled by JS).
            if (e.Button != MiddleMouseButton) return;
            _panning = true;
            _panLastClientX = e.ClientX;
            _panLastClientY = e.ClientY;
        }

        private void OnSvgMouseMove(MouseEventArgs e)
        {
            if (!_panning) return;
            double dx = e.ClientX - _panLastClientX;
            double dy = e.ClientY - _panLastClientY;
            _panLastClientX = e.ClientX;
            _panLastClientY = e.ClientY;
            // Convert pixel delta to cell-space delta. We don't have the SVG's exact
            // pixel size on the server; use CellPixels as a proxy at zoom=1.
            double pixelsPerCell = Math.Max(1, Map.Grid.CellPixels);
            _panX -= dx / (pixelsPerCell * _zoom);
            _panY -= dy / (pixelsPerCell * _zoom);
            ClampPan();
        }

        private void OnSvgMouseUp(MouseEventArgs e) => _panning = false;
    }
}
