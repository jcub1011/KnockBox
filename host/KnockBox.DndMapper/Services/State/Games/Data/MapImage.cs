namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record MapImage
    {
        public Guid Id { get; init; }
        // Host-set display name for this layer in the Layers panel. Empty until
        // the host renames the layer; the panel falls back to "Layer #N" using
        // LayerOrder when this is empty.
        public string Name { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        // Live blob-share token published by the host's circuit. Null when the host is
        // disconnected; player UIs render a placeholder until the host reconnects and
        // republishes. Never persisted to IndexedDB — capability tokens are
        // circuit-scoped and recomputed on every attach.
        public Guid? ShareToken { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        // Intrinsic size in cell units (pxW/CellPixels at upload time). Zero on legacy state.
        public double OriginalWidth { get; init; }
        public double OriginalHeight { get; init; }
        public double Rotation { get; init; }
        public double Opacity { get; init; } = 1.0;
        public int LayerOrder { get; init; }
        public bool Locked { get; init; }
        // Host-toggled visibility. When true the image is excluded from the SVG
        // for every viewer (host and players) and cannot be selected from the
        // layers panel. The layer row remains visible to the host so they can
        // toggle it back on.
        public bool Hidden { get; init; }
        public long ByteSize { get; init; }

        // Single source of truth for the layer label the host sees. Used by the
        // Layers panel view AND by the inline-rename flow so the rename input
        // opens on the same text that's currently shown — keep view and
        // rename-detection in sync by routing both through this property.
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Layer #{LayerOrder}" : Name;
    }
}
