namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class GridConfig
    {
        public int WidthCells { get; set; } = 30;
        public int HeightCells { get; set; } = 20;
        public int CellPixels { get; set; } = 50;
        public bool ShowGridLines { get; set; } = true;
        public bool SnapToGrid { get; set; } = true;
        public string LineColor { get; set; } = "#222";

        public GridConfig Clone() => new()
        {
            WidthCells = WidthCells,
            HeightCells = HeightCells,
            CellPixels = CellPixels,
            ShowGridLines = ShowGridLines,
            SnapToGrid = SnapToGrid,
            LineColor = LineColor,
        };
    }
}
