namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class MapImage
    {
        public Guid Id { get; set; }
        public string ContentType { get; set; } = string.Empty;
        // Live blob-share token published by the host's circuit. Null when the host is
        // disconnected; player UIs render a placeholder until the host reconnects and
        // republishes. Never persisted to IndexedDB — capability tokens are
        // circuit-scoped and recomputed on every attach.
        public Guid? ShareToken { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        // Intrinsic size in cell units (pxW/CellPixels at upload time). Zero on legacy state.
        public double OriginalWidth { get; set; }
        public double OriginalHeight { get; set; }
        public double Rotation { get; set; }
        public double Opacity { get; set; } = 1.0;
        public int LayerOrder { get; set; }
        public bool Locked { get; set; }
        public long ByteSize { get; set; }
    }
}
