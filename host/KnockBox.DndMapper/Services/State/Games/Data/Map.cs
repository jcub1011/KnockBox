namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class Map
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public GridConfig Grid { get; set; } = new();
        public List<MapImage> Images { get; } = [];
        public List<Token> Tokens { get; } = [];
        public DateTime CreatedUtc { get; set; }
        public int ListOrder { get; set; }
        public (double X, double Y)? DefaultSpawnPosition { get; set; }
        // v1.x markup overlay (§5.6). Serialized SVG inner markup written by
        // the host's drawing canvas. Null when the host hasn't drawn anything.
        public string? MarkupSvg { get; set; }
    }
}
