namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record GridConfig
    {
        public int WidthCells { get; init; } = 30;
        public int HeightCells { get; init; } = 20;
        public int CellPixels { get; init; } = 50;
        public bool ShowGridLines { get; init; } = true;
        public bool SnapToGrid { get; init; } = true;
        public string LineColor { get; init; } = "#222";
    }
}
