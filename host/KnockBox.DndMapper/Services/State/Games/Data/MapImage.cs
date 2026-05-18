namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class MapImage
    {
        public Guid Id { get; set; }
        // Host-set display name for this layer in the Layers panel. Empty until
        // the host renames the layer; the panel falls back to "Layer #N" using
        // LayerOrder when this is empty.
        public string Name { get; set; } = string.Empty;
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
        // Host-toggled visibility. When true the image is excluded from the SVG
        // for every viewer (host and players) and cannot be selected from the
        // layers panel. The layer row remains visible to the host so they can
        // toggle it back on.
        public bool Hidden { get; set; }
        public long ByteSize { get; set; }

        // Single source of truth for the layer label the host sees. Used by the
        // Layers panel view AND by the inline-rename flow so the rename input
        // opens on the same text that's currently shown — keep view and
        // rename-detection in sync by routing both through this property.
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Layer #{LayerOrder}" : Name;
    }
}
