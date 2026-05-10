namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class MapImage
    {
        public Guid Id { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public double Opacity { get; set; } = 1.0;
        public int LayerOrder { get; set; }
        public bool Locked { get; set; }
        public long ByteSize { get; set; }
    }
}
