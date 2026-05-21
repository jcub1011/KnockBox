using System.Globalization;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class MapSettingsModal : ComponentBase
    {
        [Parameter] public bool IsOpen { get; set; }
        [Parameter] public Map? Map { get; set; }
        [Parameter] public EventCallback<GridConfig> OnSave { get; set; }
        [Parameter] public EventCallback OnCancel { get; set; }

        private int _width;
        private int _height;
        private int _cellPixels;
        private bool _snap;
        private bool _showGrid;
        private string? _error;

        private Guid? _syncedFor;

        protected override void OnParametersSet()
        {
            if (!IsOpen) { _syncedFor = null; return; }
            if (Map is null) return;
            if (_syncedFor != Map.Id)
            {
                _syncedFor = Map.Id;
                _width = Map.Grid.WidthCells;
                _height = Map.Grid.HeightCells;
                _cellPixels = Map.Grid.CellPixels;
                _snap = Map.Grid.SnapToGrid;
                _showGrid = Map.Grid.ShowGridLines;
                _error = null;
            }
            base.OnParametersSet();
        }

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
        }

        private Task OnCancelInternal() => OnCancel.InvokeAsync();

        private async Task OnSaveInternal()
        {
            if (_width < 1 || _width > 1000) { _error = "Width must be 1–1000 cells."; return; }
            if (_height < 1 || _height > 1000) { _error = "Height must be 1–1000 cells."; return; }
            if (_cellPixels < 1 || _cellPixels > 1000) { _error = "Cell size must be 1–1000 pixels."; return; }

            _error = null;
            var grid = new GridConfig
            {
                WidthCells = _width,
                HeightCells = _height,
                CellPixels = _cellPixels,
                SnapToGrid = _snap,
                ShowGridLines = _showGrid,
                LineColor = Map?.Grid.LineColor ?? "#222",
            };
            await OnSave.InvokeAsync(grid);
        }
    }
}
